using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.Households;

public sealed record HouseholdOverview(Event Event, IReadOnlyList<Household> Households,
    IReadOnlyList<HouseholdMember> Members, IReadOnlyList<LocalPerson> People);
public sealed record HouseholdMemberEdit(Guid PersonId, HouseholdMemberKind Kind, Guid? CompanionOfMemberId,
    int HouseholdRevision, int Revision, bool Confirm, Guid OperationId);

public sealed class HouseholdService(ILocalStore store, LocalOperations operations)
{
    public async Task<HouseholdOverview> ListAsync(Guid actorId, Guid eventId, CancellationToken ct = default)
    {
        var households = await operations.ReadAsync<Household>(actorId, eventId, ct);
        return new(await store.EventAsync(eventId, ct) ?? throw new LocalAccessException(),
            households.OrderBy(x => x.Name).ToArray(), await store.ListAsync<HouseholdMember>(eventId, ct),
            await store.ParticipantsAsync(eventId, ct));
    }
    public Task<Guid> CreateAsync(Guid actorId, Guid eventId, string name, Guid operationId, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var item = new Household(eventId, name, operationId);
            var existing = await store.FindAsync<Household>(eventId, item.Id, token);
            if (existing is not null)
            {
                if (existing.MatchesCreationName(item.Name)) return existing.Id;
                throw new LocalConflictException("Identificativo già usato o nucleo modificato.");
            }
            store.Add(item); operations.Audit(eventId, actorId, item.Id, "Households", "Created");
            await store.SaveAsync(token); return item.Id;
        }, ct);

    public Task<bool> RenameAsync(Guid actorId, Guid eventId, Guid householdId, int? expectedRevision,
        string name, bool confirm, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var household = await GetHousehold(eventId, householdId, token);
            if (!expectedRevision.HasValue) throw new LocalValidationException("La revisione del nucleo è obbligatoria. Ricarica la pagina.");
            LocalOperations.Revision(household, expectedRevision.Value);
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 150)
                throw new LocalValidationException("Inserisci un nome nucleo da 1 a 150 caratteri.");
            if (household.Name == name.Trim()) return false;
            if (!confirm) throw new LocalValidationException("Conferma esplicitamente la rinomina del nucleo.");
            household.Rename(name);
            operations.Audit(eventId, actorId, household.Id, "Households", "Renamed");
            await store.SaveAsync(token);
            return true;
        }, ct);

    public Task<Guid> AddAsync(Guid actorId, Guid eventId, Guid householdId, HouseholdMemberEdit input, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var household = await GetHousehold(eventId, householdId, token);
            var member = new HouseholdMember(eventId, householdId, input.PersonId, input.Kind, input.CompanionOfMemberId, input.OperationId);
            var existing = await store.FindAsync<HouseholdMember>(eventId, member.Id, token);
            if (existing is not null)
            {
                if (existing.HouseholdId == householdId && existing.PersonId == input.PersonId && existing.Revision == 0
                    && existing.IsActive && existing.Kind == input.Kind && existing.CompanionOfMemberId == input.CompanionOfMemberId)
                    return existing.Id;
                throw new LocalConflictException("Identificativo già usato o appartenenza modificata.");
            }
            LocalOperations.Revision(household, input.HouseholdRevision);
            await CheckParticipant(eventId, input.PersonId, token);
            var members = await store.ListAsync<HouseholdMember>(eventId, token);
            if (members.Any(x => x.HouseholdId == householdId && x.PersonId == input.PersonId))
                throw new LocalValidationException("La persona era già membro di questo nucleo: riattiva la membership esistente con conferma.");
            CheckMembership(members, member.Id, householdId, input.PersonId, input.Kind, input.CompanionOfMemberId, true);
            store.Add(member); household.Touch();
            operations.Audit(eventId, actorId, member.Id, "Households", "MemberAdded");
            await store.SaveAsync(token); return member.Id;
        }, ct);

    public Task<bool> UpdateAsync(Guid actorId, Guid eventId, Guid householdId, Guid memberId,
        HouseholdMemberEdit input, bool active, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var household = await GetHousehold(eventId, householdId, token);
            var member = await store.FindAsync<HouseholdMember>(eventId, memberId, token) ?? throw new LocalAccessException();
            if (member.HouseholdId != householdId || member.PersonId != input.PersonId) throw new LocalAccessException();
            LocalOperations.Revision(household, input.HouseholdRevision); LocalOperations.Revision(member, input.Revision);
            if (member.Kind == input.Kind && member.CompanionOfMemberId == input.CompanionOfMemberId && member.IsActive == active) return false;
            if (!input.Confirm) throw new LocalValidationException("Conferma esplicitamente la modifica dell'appartenenza.");
            await CheckParticipant(eventId, member.PersonId, token);
            var members = await store.ListAsync<HouseholdMember>(eventId, token);
            CheckMembership(members, memberId, householdId, member.PersonId, input.Kind, input.CompanionOfMemberId, active);
            var changed = member.Update(input.Kind, input.CompanionOfMemberId, active);
            if (changed)
            {
                household.Touch(); operations.Audit(eventId, actorId, member.Id, "Households", active ? "MemberUpdated" : "MemberRemoved");
                await store.SaveAsync(token);
            }
            return changed;
        }, ct);

    private async Task<Household> GetHousehold(Guid eventId, Guid householdId, CancellationToken ct) =>
        await store.FindAsync<Household>(eventId, householdId, ct) ?? throw new LocalAccessException();
    private async Task CheckParticipant(Guid eventId, Guid personId, CancellationToken ct)
    {
        if (!(await store.ParticipantsAsync(eventId, ct)).Any(x => x.Id == personId))
            throw new LocalValidationException("La persona deve essere già partecipante di questo evento.");
    }
    private static void CheckMembership(IReadOnlyList<HouseholdMember> members, Guid memberId, Guid householdId,
        Guid personId, HouseholdMemberKind kind, Guid? companionId, bool active)
    {
        if (active && members.Any(x => x.Id != memberId && x.PersonId == personId && x.IsActive))
            throw new LocalValidationException("La persona appartiene già a un nucleo attivo dell'evento.");
        if (active && companionId.HasValue)
        {
            var primary = members.SingleOrDefault(x => x.Id == companionId && x.HouseholdId == householdId && x.IsActive);
            if (primary is null || primary.Id == memberId || primary.Kind != HouseholdMemberKind.Adult || primary.CompanionOfMemberId.HasValue)
                throw new LocalValidationException("Il riferimento deve essere un adulto primario attivo dello stesso nucleo.");
        }
        if ((!active || kind != HouseholdMemberKind.Adult || companionId.HasValue)
            && members.Any(x => x.IsActive && x.CompanionOfMemberId == memberId))
            throw new LocalValidationException("Scollega prima gli accompagnatori che fanno riferimento a questa persona.");
    }
}
