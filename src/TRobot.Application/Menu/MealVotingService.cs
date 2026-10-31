using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.Menu;

public sealed record MealVotingEntryInput(Guid Id, Guid CycleId, string Name, IReadOnlyList<Guid> ProposalIds,
    bool IsExcluded, int CycleRevision, int? Revision);
public sealed record MealPreferenceInput(Guid OperationId, Guid CycleId, int CycleRevision, Guid VoterId, Guid PersonId,
    DateOnly Day, MealKind Meal, IReadOnlyList<Guid> EntryIds, int BallotRevision, string EligibilityFingerprint);
public sealed record MealVotingPerson(Guid PersonId, string Name, HouseholdMemberKind Kind);
public sealed record MealVotingExclusion(Guid PersonId, string Reason);
public sealed record MealVotingCount(Guid EntryId, int Adults, int Children, int Total, IReadOnlyList<MealVotingExclusion> Excluded);
public sealed record MealVotingSnapshotPerson(Guid PersonId, HouseholdMemberKind? Kind, string? ExclusionReason, IReadOnlyList<Guid> EntryIds);
public sealed record MealVotingSnapshot(IReadOnlyList<MealVotingCount> Counts, IReadOnlyList<MealVotingSnapshotPerson> People);
public sealed record MealVotingPage(Guid EventId, string EventName, MealVotingCycle? CurrentCycle, IReadOnlyList<MealVotingCycle> Cycles,
    IReadOnlyList<MealVotingEntry> Entries, IReadOnlyList<MealVotingSource> Sources, IReadOnlyList<MealVotingCount> CurrentCounts,
    IReadOnlyList<MealVotingCount> ClosedCounts);
public sealed record MealBallotPage(MealVotingCycle Cycle, Guid VoterId, Guid PersonId, DateOnly Day, MealKind Meal,
    IReadOnlyList<MealVotingEntry> Entries, IReadOnlyList<Guid> SelectedEntryIds, int BallotRevision,
    string EligibilityFingerprint, bool CanVote, string? ExclusionReason);
public sealed record MealPreferenceHistoryPage(int Page, IReadOnlyList<MealPreferenceChange> Changes, bool HasNext);

public sealed class MealVotingService(ILocalStore store, LocalOperations operations, TimeProvider clock)
{
    private DateTime Utc => clock.GetUtcNow().UtcDateTime;
    public async Task<MealPreferenceHistoryPage> ReadHistoryAsync(Guid actorId, Guid eventId, int page = 1, CancellationToken ct = default)
    {
        await operations.AuthorizeAsync(actorId, eventId, ct);
        if (page is < 1 or > 100000) throw new LocalValidationException("Pagina storico non valida.");
        var changes = (await store.ListAsync<MealPreferenceChange>(eventId, ct)).OrderByDescending(x => x.AtUtc).ThenBy(x => x.Id)
            .Skip((page - 1) * 25).Take(26).ToArray();
        return new(page, changes.Take(25).ToArray(), changes.Length > 25);
    }
    public async Task<MealVotingPage> ReadAsync(Guid actorId, Guid eventId, CancellationToken ct = default)
    {
        await operations.AuthorizeAsync(actorId, eventId, ct);
        var cycles = (await store.ListAsync<MealVotingCycle>(eventId, ct)).OrderByDescending(x => x.Number).ToArray();
        var current = Current(cycles);
        var entries = await store.ListAsync<MealVotingEntry>(eventId, ct);
        var counts = current is null ? [] : (await CountAsync(eventId, current.Id, ct)).Counts;
        var closed = current?.ClosedSnapshotJson is { } json ? JsonSerializer.Deserialize<MealVotingSnapshot>(json)!.Counts : [];
        return new(eventId, (await store.EventAsync(eventId, ct))!.Name, current, cycles, entries,
            await store.ListAsync<MealVotingSource>(eventId, ct), counts, closed);
    }

    public Task<Guid> StartCycleAsync(Guid actorId, Guid eventId, Guid id, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var cycles = await store.ListAsync<MealVotingCycle>(eventId, token);
            if (cycles.Any(x => x.Id == id)) return id;
            if (cycles.Any(x => x.State != MealVotingState.Closed)) throw new LocalConflictException("Esiste gia un ciclo pasti attivo.");
            store.Add(new MealVotingCycle(eventId, id, Utc, cycles.Select(x => x.Number).DefaultIfEmpty(0).Max() + 1));
            operations.Audit(eventId, actorId, id, "MealVoting", "CycleStarted");
            await store.SaveAsync(token); return id;
        }, ct);

    public Task<bool> CloseCollectionAsync(Guid actorId, Guid eventId, Guid cycleId, int revision, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var cycle = await CycleAsync(eventId, cycleId, token); LocalOperations.Revision(cycle, revision);
            var confirmedProposals = (await store.ListAsync<MealProposal>(eventId, token)).Where(x => x.WithdrawnAtUtc is null).ToArray();
            if (confirmedProposals.Length == 0) throw new LocalValidationException("Raccogli almeno una proposta confermata prima di chiudere la raccolta.");
            cycle.CloseCollection();
            // Each confirmed original is imported once. Originals and cooking offers remain untouched.
            foreach (var source in confirmedProposals)
            {
                var entry = new MealVotingEntry(eventId, cycleId, Guid.NewGuid(), source.Day, source.Meal, source.DishName, false);
                store.Add(entry); store.Add(new MealVotingSource(eventId, cycleId, entry.Id, source.Id));
            }
            operations.Audit(eventId, actorId, cycleId, "MealVoting", "CollectionClosed");
            await store.SaveAsync(token); return true;
        }, ct);

    public Task<Guid> SaveEntryAsync(Guid actorId, Guid eventId, MealVotingEntryInput input, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var cycle = await CycleAsync(eventId, input.CycleId, token); cycle.Require(MealVotingState.Preparation); LocalOperations.Revision(cycle, input.CycleRevision);
            if (input.ProposalIds.Count == 0 || input.ProposalIds.Distinct().Count() != input.ProposalIds.Count)
                throw new LocalValidationException("Seleziona le fonti originali senza duplicati.");
            var proposals = (await store.ListAsync<MealProposal>(eventId, token)).Where(x => input.ProposalIds.Contains(x.Id)).ToArray();
            if (proposals.Length != input.ProposalIds.Count || proposals.Any(x => x.WithdrawnAtUtc.HasValue)) throw new LocalValidationException("Fonte non disponibile nell'evento.");
            var first = proposals[0];
            if (proposals.Any(x => x.Day != first.Day || x.Meal != first.Meal)) throw new LocalValidationException("Accorpa soltanto piatti dello stesso giorno e pasto.");
            var allEntries = await store.ListAsync<MealVotingEntry>(eventId, token);
            var current = await store.FindAsync<MealVotingEntry>(eventId, input.Id, token);
            if (current is not null && current.CycleId != cycle.Id) throw new LocalConflictException("Identificativo gia utilizzato in un altro ciclo.");
            if (current is null && allEntries.Any(x => x.Id == input.Id)) throw new LocalConflictException("Identificativo gia utilizzato.");
            if (current is not null)
            {
                LocalOperations.Revision(current, input.Revision ?? -1);
                if (current.Day != first.Day || current.Meal != first.Meal) throw new LocalValidationException("Il giorno e pasto della voce non possono cambiare.");
                current.Update(input.Name, input.IsExcluded);
            }
            else { current = new(eventId, cycle.Id, input.Id, first.Day, first.Meal, input.Name, input.IsExcluded); store.Add(current); }
            var sources = (await store.ListAsync<MealVotingSource>(eventId, token)).Where(x => x.CycleId == cycle.Id).ToArray();
            if (sources.Any(x => x.EntryId == current.Id && !input.ProposalIds.Contains(x.ProposalId)))
                throw new LocalValidationException("Per separare una fonte spostala prima in un'altra voce.");
            foreach (var proposal in proposals)
            {
                var source = sources.SingleOrDefault(x => x.ProposalId == proposal.Id);
                if (source is null) store.Add(new MealVotingSource(eventId, cycle.Id, current.Id, proposal.Id));
                else if (source.EntryId != current.Id) (await store.FindAsync<MealVotingSource>(eventId, source.Id, token))!.MoveTo(current.Id);
            }
            cycle.Touch(); operations.Audit(eventId, actorId, current.Id, "MealVoting", "EntryPrepared");
            await store.SaveAsync(token); return current.Id;
        }, ct);

    public Task<bool> OpenAsync(Guid actorId, Guid eventId, Guid cycleId, int revision, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var cycle = await CycleAsync(eventId, cycleId, token); LocalOperations.Revision(cycle, revision);
            if (!(await PublishedAsync(eventId, cycleId, token)).Any()) throw new LocalValidationException("Prepara almeno una voce valida prima dell'apertura.");
            cycle.Open(Utc); operations.Audit(eventId, actorId, cycleId, "MealVoting", "VotingOpened");
            await store.SaveAsync(token); return true;
        }, ct);

    public Task<bool> CloseAsync(Guid actorId, Guid eventId, Guid cycleId, int revision, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var cycle = await CycleAsync(eventId, cycleId, token); LocalOperations.Revision(cycle, revision); cycle.Require(MealVotingState.Open);
            var snapshot = await CountAsync(eventId, cycleId, token);
            cycle.Close(JsonSerializer.Serialize(snapshot), Utc); operations.Audit(eventId, actorId, cycleId, "MealVoting", "VotingClosed");
            await store.SaveAsync(token); return true;
        }, ct);

    public async Task<IReadOnlyList<MealVotingPerson>> ReadVoterAsync(Guid actorId, Guid eventId, Guid voterId, CancellationToken ct = default)
    {
        await operations.AuthorizeAsync(actorId, eventId, ct);
        var members = await store.ListAsync<HouseholdMember>(eventId, ct);
        var adult = Adult(members, voterId);
        var people = await store.ParticipantsAsync(eventId, ct);
        if (!people.Any(x => x.Id == voterId)) throw new LocalAccessException();
        return members.Where(x => x.IsActive && (x.Id == adult.Id || x.Kind == HouseholdMemberKind.Child && x.CompanionOfMemberId == adult.Id && x.HouseholdId == adult.HouseholdId))
            .Where(x => people.Any(p => p.Id == x.PersonId))
            .Select(x => new MealVotingPerson(x.PersonId, people.Single(p => p.Id == x.PersonId).Name, x.Kind)).ToArray();
    }

    public async Task<MealBallotPage> ReadBallotAsync(Guid actorId, Guid eventId, Guid voterId, Guid targetPersonId, DateOnly day, MealKind meal, CancellationToken ct = default)
    {
        await operations.AuthorizeAsync(actorId, eventId, ct);
        if (day == default || !Enum.IsDefined(meal)) throw new LocalValidationException("Pasto non valido.");
        var cycle = Current(await store.ListAsync<MealVotingCycle>(eventId, ct)) ?? throw new LocalValidationException("Nessun ciclo pasti disponibile.");
        var eligibility = await EligibilityAsync(eventId, voterId, targetPersonId, day, ct);
        var entries = (await PublishedAsync(eventId, cycle.Id, ct)).Where(x => x.Day == day && x.Meal == meal).ToArray();
        var ballot = await BallotAsync(eventId, cycle.Id, targetPersonId, day, meal, ct);
        var reason = eligibility.Reason ?? (cycle.State != MealVotingState.Open ? "La votazione non e aperta." : entries.Length == 0 ? "Nessuna voce pubblicata per il pasto." : null);
        return new(cycle, voterId, targetPersonId, day, meal, entries, ballot?.Selections() ?? [], ballot?.Revision ?? 0, eligibility.Fingerprint, reason is null, reason);
    }

    public Task<int> SavePreferencesAsync(Guid actorId, Guid eventId, MealPreferenceInput input, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, token => SavePreferencesInTransactionAsync(actorId, eventId, input, token), ct);

    public async Task<int> SavePreferencesInTransactionAsync(Guid actorId, Guid eventId, MealPreferenceInput input, CancellationToken ct = default)
    {
        await operations.AuthorizeAsync(actorId, eventId, ct);
        if (input.OperationId == Guid.Empty || input.EntryIds.Count > 1000) throw new LocalValidationException("Operazione preferenze non valida.");
        var fingerprint = Hash(JsonSerializer.Serialize(new { actorId, input.CycleId, input.CycleRevision, input.VoterId, input.PersonId, input.Day, input.Meal,
            Entries = input.EntryIds.Distinct().Order().ToArray(), input.BallotRevision, input.EligibilityFingerprint }));
        var receipt = await store.FindAsync<MealPreferenceChange>(eventId, input.OperationId, ct);
        if (receipt is not null)
        {
            if (receipt.PayloadFingerprint != fingerprint) throw new LocalConflictException("Identificativo operazione riutilizzato con dati diversi.");
            return receipt.BallotRevision;
        }
        var page = await ReadBallotAsync(actorId, eventId, input.VoterId, input.PersonId, input.Day, input.Meal, ct);
        if (page.Cycle.Id != input.CycleId || page.Cycle.Revision != input.CycleRevision || page.BallotRevision != input.BallotRevision || page.EligibilityFingerprint != input.EligibilityFingerprint)
            throw new LocalConflictException("Riepilogo obsoleto. Consulta di nuovo le preferenze.");
        if (!page.CanVote) throw new LocalValidationException(page.ExclusionReason!);
        if (input.EntryIds.Any(x => !page.Entries.Any(e => e.Id == x))) throw new LocalValidationException("Voce non disponibile per questo pasto.");
        var ballot = await BallotAsync(eventId, input.CycleId, input.PersonId, input.Day, input.Meal, ct);
        if (ballot is not null) ballot = await store.FindAsync<MealPreferenceBallot>(eventId, ballot.Id, ct);
        if (ballot is null) { ballot = new(eventId, input.CycleId, input.PersonId, input.Day, input.Meal); store.Add(ballot); }
        var previous = ballot.SelectionJson; ballot.Replace(input.EntryIds);
        store.Add(new MealPreferenceChange(eventId, input.OperationId, input.CycleId, input.VoterId, input.PersonId, actorId,
            input.Day, input.Meal, fingerprint, previous, ballot.SelectionJson, ballot.Revision, Utc));
        operations.Audit(eventId, actorId, ballot.Id, "MealVoting", "PreferencesConfirmed");
        await store.SaveAsync(ct); return ballot.Revision;
    }

    private async Task<MealVotingSnapshot> CountAsync(Guid eventId, Guid cycleId, CancellationToken ct)
    {
        var entries = await PublishedAsync(eventId, cycleId, ct);
        var ballots = (await store.ListAsync<MealPreferenceBallot>(eventId, ct)).Where(x => x.CycleId == cycleId).ToArray();
        var members = await store.ListAsync<HouseholdMember>(eventId, ct);
        var participants = await store.ParticipationsAsync(eventId, ct);
        var stays = await store.ListAsync<AttendanceStay>(eventId, ct);
        var persons = ballots.Select(b =>
        {
            var active = members.Where(x => x.IsActive && x.PersonId == b.PersonId).ToArray();
            var kind = active.Length == 1 ? (HouseholdMemberKind?)active[0].Kind : null;
            var reason = Ineligible(b.PersonId, b.Day, active, participants, stays);
            return new MealVotingSnapshotPerson(b.PersonId, kind, reason, b.Selections().Where(id => entries.Any(e => e.Id == id && e.Day == b.Day && e.Meal == b.Meal)).ToArray());
        }).ToArray();
        var counts = entries.Select(e =>
        {
            var selected = persons.Where(x => x.EntryIds.Contains(e.Id)).ToArray();
            var adults = selected.Count(x => x.ExclusionReason is null && x.Kind == HouseholdMemberKind.Adult);
            var children = selected.Count(x => x.ExclusionReason is null && x.Kind == HouseholdMemberKind.Child);
            return new MealVotingCount(e.Id, adults, children, adults + children, selected.Where(x => x.ExclusionReason is not null).Select(x => new MealVotingExclusion(x.PersonId, x.ExclusionReason!)).ToArray());
        }).ToArray();
        return new(counts, persons);
    }

    private async Task<(string Fingerprint, string? Reason)> EligibilityAsync(Guid eventId, Guid voterId, Guid targetId, DateOnly day, CancellationToken ct)
    {
        var members = await store.ListAsync<HouseholdMember>(eventId, ct); var adult = Adult(members, voterId);
        var target = members.Where(x => x.IsActive && x.PersonId == targetId).ToArray();
        if (targetId != voterId && !(target.Length == 1 && target[0].Kind == HouseholdMemberKind.Child && target[0].CompanionOfMemberId == adult.Id && target[0].HouseholdId == adult.HouseholdId))
            throw new LocalAccessException();
        var participants = await store.ParticipationsAsync(eventId, ct); var stays = await store.ListAsync<AttendanceStay>(eventId, ct);
        if (!participants.Any(x => x.PersonId == voterId)) throw new LocalAccessException();
        var reason = Ineligible(targetId, day, target, participants, stays);
        var participant = participants.SingleOrDefault(x => x.PersonId == targetId); var stay = stays.SingleOrDefault(x => x.Id == targetId);
        return (Hash(JsonSerializer.Serialize(new { eventId, voterId, targetId, day, AdultId = adult.Id, AdultRevision = adult.Revision,
            AdultHousehold = adult.HouseholdId, Target = target.Select(x => new { x.Id, x.Revision, x.Kind, x.CompanionOfMemberId, x.HouseholdId }),
            Status = participant?.Status, Arrival = stay?.Arrival, Days = stay?.Days, StayRevision = stay?.Revision })), reason);
    }
    private static string? Ineligible(Guid personId, DateOnly day, IReadOnlyList<HouseholdMember> active, IReadOnlyList<LocalParticipation> participants, IReadOnlyList<AttendanceStay> stays)
    {
        if (active.Count != 1) return "Classificazione attiva adulto/bambino mancante o ambigua.";
        if (participants.SingleOrDefault(x => x.PersonId == personId)?.Status != ParticipationStatus.Confirmed) return "Partecipazione non confermata.";
        var stay = stays.SingleOrDefault(x => x.Id == personId);
        if (stay is null || stay.Days < 1 || stay.Arrival == default) return "Soggiorno non disponibile.";
        return day < stay.Arrival || day > stay.LastDay ? "Giorno fuori dal soggiorno confermato." : null;
    }
    private static HouseholdMember Adult(IReadOnlyList<HouseholdMember> members, Guid voterId)
    { var active = members.Where(x => x.IsActive && x.PersonId == voterId).ToArray(); return active.Length == 1 && active[0].Kind == HouseholdMemberKind.Adult ? active[0] : throw new LocalAccessException(); }
    private static MealVotingCycle? Current(IEnumerable<MealVotingCycle> cycles) => cycles.OrderByDescending(x => x.Number).FirstOrDefault();
    private async Task<MealVotingCycle> CycleAsync(Guid eventId, Guid id, CancellationToken ct) => await store.FindAsync<MealVotingCycle>(eventId, id, ct) ?? throw new LocalAccessException();
    private async Task<IReadOnlyList<MealVotingEntry>> PublishedAsync(Guid eventId, Guid cycleId, CancellationToken ct)
    { var sources = await store.ListAsync<MealVotingSource>(eventId, ct); return (await store.ListAsync<MealVotingEntry>(eventId, ct)).Where(x => x.CycleId == cycleId && !x.IsExcluded && sources.Any(s => s.CycleId == cycleId && s.EntryId == x.Id)).OrderBy(x => x.Day).ThenBy(x => x.Meal).ThenBy(x => x.Name).ThenBy(x => x.Id).ToArray(); }
    private async Task<MealPreferenceBallot?> BallotAsync(Guid eventId, Guid cycleId, Guid personId, DateOnly day, MealKind meal, CancellationToken ct) =>
        (await store.ListAsync<MealPreferenceBallot>(eventId, ct)).SingleOrDefault(x => x.CycleId == cycleId && x.PersonId == personId && x.Day == day && x.Meal == meal);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
