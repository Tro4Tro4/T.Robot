using System.Security.Cryptography;
using System.Text.Json;
using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.Lodging;

public sealed record LodgingVersion(int Revision, string Fingerprint);
public sealed record LodgingHousehold(Guid? Id, string Name, IReadOnlyList<LocalParticipation> People);
public sealed record LodgingPage(Event Event, LodgingVersion Version, bool IsFinal, bool FinalizationOutdated,
    IReadOnlyList<LodgingApartment> Apartments, IReadOnlyList<LodgingRoom> Rooms,
    IReadOnlyList<LodgingAssignment> Assignments, IReadOnlyList<LodgingHousehold> Households)
{
    public Guid? RoomFor(Guid personId) => Assignments.SingleOrDefault(x => x.PersonId == personId)?.RoomId;
    public int Occupancy(Guid roomId) => Assignments.Count(x => x.RoomId == roomId);
    public int UnassignedConfirmed => Households.SelectMany(x => x.People)
        .Count(x => x.Status == ParticipationStatus.Confirmed && RoomFor(x.PersonId) is null);
}

public sealed class LodgingService(ILocalStore store, LocalOperations operations)
{
    public async Task<LodgingPage> ReadAsync(Guid actorId, Guid eventId, CancellationToken ct = default)
    {
        await operations.AuthorizeAsync(actorId, eventId, ct);
        return await Page(eventId, ct);
    }

    public Task<Guid> SaveApartmentAsync(Guid actorId, Guid eventId, LodgingVersion version, Guid id,
        string name, string? directions, CancellationToken ct = default) => Change(actorId, eventId, version, async (page, token) =>
    {
        var current = await store.FindAsync<LodgingApartment>(eventId, id, token);
        if (current is null) store.Add(new LodgingApartment(id, eventId, name, directions));
        else current.Update(name, directions);
        operations.Audit(eventId, actorId, id, "Lodging", current is null ? "ApartmentCreated" : "ApartmentUpdated");
        return id;
    }, ct);

    public Task<Guid> SaveRoomAsync(Guid actorId, Guid eventId, LodgingVersion version, Guid id, Guid apartmentId,
        string name, int capacity, string? notes, CancellationToken ct = default) => Change(actorId, eventId, version, async (page, token) =>
    {
        if (!page.Apartments.Any(x => x.Id == apartmentId)) throw new LocalValidationException("Appartamento non trovato nell'evento.");
        var current = await store.FindAsync<LodgingRoom>(eventId, id, token);
        if (current is not null && current.ApartmentId != apartmentId) throw new LocalValidationException("La camera appartiene a un altro appartamento.");
        if (capacity < page.Occupancy(id)) throw new LocalValidationException("La capienza non può essere inferiore agli occupanti attuali.");
        if (current is null) store.Add(new LodgingRoom(id, eventId, apartmentId, name, capacity, notes));
        else current.Update(name, capacity, notes);
        operations.Audit(eventId, actorId, id, "Lodging", current is null ? "RoomCreated" : "RoomUpdated");
        return id;
    }, ct);

    public Task<bool> AssignPersonAsync(Guid actorId, Guid eventId, LodgingVersion version, Guid personId,
        Guid? roomId, CancellationToken ct = default) => Change(actorId, eventId, version,
            (page, token) => Assign(actorId, page, [personId], roomId, token), ct);

    public Task<bool> AssignHouseholdAsync(Guid actorId, Guid eventId, LodgingVersion version, Guid householdId,
        Guid roomId, CancellationToken ct = default) => Change(actorId, eventId, version, (page, token) =>
    {
        var household = page.Households.SingleOrDefault(x => x.Id == householdId)
            ?? throw new LocalValidationException("Nucleo attivo non trovato nell'evento.");
        return Assign(actorId, page, household.People.Select(x => x.PersonId).ToArray(), roomId, token);
    }, ct);

    public Task<bool> FinalizeAsync(Guid actorId, Guid eventId, LodgingVersion version, CancellationToken ct = default) =>
        Change(actorId, eventId, version, (page, token) =>
        {
            if (page.UnassignedConfirmed > 0) throw new LocalValidationException("Assegna una camera a tutti i partecipanti confermati prima di rendere definitivo il piano.");
            operations.Audit(eventId, actorId, eventId, "Lodging", "Finalized");
            return Task.FromResult(true);
        }, ct, finalize: true);

    private Task<T> Change<T>(Guid actorId, Guid eventId, LodgingVersion version,
        Func<LodgingPage, CancellationToken, Task<T>> action, CancellationToken ct, bool finalize = false) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var page = await Page(eventId, token);
            if (page.Version != version) throw new LocalConflictException("Piano, presenze o nuclei modificati. Ricarica la pagina.");
            var result = await action(page, token);
            if (result is bool changed && !changed) return result;
            var plan = await store.FindAsync<LodgingPlan>(eventId, eventId, token);
            if (plan is null) { plan = new LodgingPlan(eventId); store.Add(plan); }
            LocalOperations.Revision(plan, version.Revision);
            if (finalize) plan.FinalizePlan(page.Version.Fingerprint);
            else
            {
                plan.Change(); operations.Audit(eventId, actorId, plan.Id, "Lodging", "DraftChanged");
            }
            await store.SaveAsync(token); return result;
        }, ct);

    private async Task<bool> Assign(Guid actorId, LodgingPage page, IReadOnlyList<Guid> people, Guid? roomId, CancellationToken ct)
    {
        var participants = page.Households.SelectMany(x => x.People).Select(x => x.PersonId).ToHashSet();
        if (people.Count == 0 || people.Any(x => !participants.Contains(x)))
            throw new LocalValidationException("Si possono assegnare solo partecipanti dell'evento.");
        if (roomId.HasValue)
        {
            var room = page.Rooms.SingleOrDefault(x => x.Id == roomId)
                ?? throw new LocalValidationException("Camera non trovata nell'evento.");
            var others = page.Assignments.Count(x => x.RoomId == roomId && !people.Contains(x.PersonId));
            if (others + people.Distinct().Count() > room.Capacity) throw new LocalValidationException("Posti insufficienti nella camera scelta. Nessuna assegnazione modificata.");
        }
        var changed = false;
        foreach (var personId in people.Distinct())
        {
            var snapshot = page.Assignments.SingleOrDefault(x => x.PersonId == personId);
            var oldRoom = snapshot?.RoomId;
            if (oldRoom == roomId) continue;
            changed = true;
            Guid assignmentId;
            if (snapshot is null)
            {
                var assignment = new LodgingAssignment(page.Event.Id, personId, roomId!.Value);
                assignmentId = assignment.Id; store.Add(assignment);
            }
            else
            {
                var assignment = await store.FindAsync<LodgingAssignment>(page.Event.Id, snapshot.Id, ct) ?? throw new LocalConflictException("Assegnazione modificata.");
                assignment.SetRoom(roomId); assignmentId = assignment.Id;
            }
            operations.Audit(page.Event.Id, actorId, assignmentId, "Lodging", $"{oldRoom:N}>{roomId:N}");
        }
        return changed;
    }

    private async Task<LodgingPage> Page(Guid eventId, CancellationToken ct)
    {
        var ev = await store.EventAsync(eventId, ct) ?? throw new LocalAccessException();
        var plan = (await store.ListAsync<LodgingPlan>(eventId, ct)).SingleOrDefault();
        var apartments = (await store.ListAsync<LodgingApartment>(eventId, ct)).OrderBy(x => x.Name).ThenBy(x => x.Id).ToArray();
        var rooms = (await store.ListAsync<LodgingRoom>(eventId, ct)).OrderBy(x => x.Name).ThenBy(x => x.Id).ToArray();
        var assignments = (await store.ListAsync<LodgingAssignment>(eventId, ct)).OrderBy(x => x.PersonId).ToArray();
        var people = (await store.ParticipationsAsync(eventId, ct)).OrderBy(x => x.PersonId).ToArray();
        var households = (await store.ListAsync<Household>(eventId, ct)).OrderBy(x => x.Id).ToArray();
        var members = (await store.ListAsync<HouseholdMember>(eventId, ct)).Where(x => x.IsActive).OrderBy(x => x.PersonId).ToArray();
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { EventName = ev.Name, apartments, rooms, assignments, people,
            households = households.Select(x => new { x.Id, x.EventId, x.Name, x.Revision }), members })));
        var groups = households.Select(h => new LodgingHousehold(h.Id, h.Name,
            people.Where(p => members.Any(m => m.HouseholdId == h.Id && m.PersonId == p.PersonId)).OrderBy(p => p.Name).ToArray()))
            .Where(x => x.People.Count > 0).OrderBy(x => x.Name).ToList();
        groups.Add(new(null, "Senza nucleo", people.Where(p => !members.Any(m => m.PersonId == p.PersonId)).OrderBy(p => p.Name).ToArray()));
        return new(ev, new(plan?.Revision ?? 0, fingerprint), plan?.FinalizedFingerprint == fingerprint,
            plan?.FinalizedFingerprint is not null && plan.FinalizedFingerprint != fingerprint, apartments, rooms, assignments, groups);
    }
}
