using TRobot.Domain;

namespace TRobot.Application.Events;

public sealed record ParticipantNameEditor(Guid EventId, string EventName, Guid PersonId, string DisplayName, int Revision, bool IsShared);

public sealed class ParticipantNameService(IEventManagementStore store, TimeProvider clock)
{
    public async Task<ParticipantNameEditor> GetAsync(Guid actorId, Guid eventId, Guid personId, CancellationToken ct = default)
    {
        var (person, eventIds) = await LoadAuthorizedAsync(actorId, eventId, personId, ct);
        var ev = await store.FindEventAsync(eventId, ct) ?? throw new EventAccessException();
        return new(eventId, ev.Name, person.Id, person.DisplayName, person.Revision, eventIds.Count > 1);
    }

    public Task<bool> RenameAsync(Guid actorId, Guid eventId, Guid personId, int expectedRevision,
        string displayName, bool confirmedShared, CancellationToken ct = default) => store.InTransactionAsync(async token =>
    {
        var (person, eventIds) = await LoadAuthorizedAsync(actorId, eventId, personId, token);
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 150)
            throw new EventValidationException("Il nome deve contenere da 1 a 150 caratteri.");
        if (expectedRevision < 0 || person.Revision != expectedRevision)
            throw new EventConflictException("Il nome è stato modificato. Ricarica la pagina prima di riprovare.");
        var name = displayName.Trim();
        if (person.DisplayName == name) return false;
        if (eventIds.Count > 1 && !confirmedShared)
            throw new EventValidationException("Conferma la correzione del nome in tutti gli eventi che condividono questa persona.");

        // Preserve the original request identity for pre-existing registrations before the first rename.
        var legacy = await store.GetLegacyRegistrationsAsync(personId, token);
        foreach (var registration in legacy)
            registration.CaptureRegistrationFingerprint(RegistrationFingerprint.Compute(person.DisplayName, person.WhatsAppSenderId is not null));
        person.Rename(name);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var affectedEvent in eventIds)
            store.AddAudit(new(affectedEvent, actorId, EventAuditAction.ParticipantRenamed, now, personId));
        await store.SaveAsync(token);
        return true;
    }, ct);

    private async Task<(Person Person, IReadOnlyList<Guid> EventIds)> LoadAuthorizedAsync(Guid actorId, Guid eventId, Guid personId, CancellationToken ct)
    {
        if (actorId == Guid.Empty || eventId == Guid.Empty || personId == Guid.Empty
            || !await store.IsOrganizerAsync(eventId, actorId, ct)) throw new EventAccessException();
        var person = await store.FindParticipantPersonAsync(eventId, personId, ct) ?? throw new EventAccessException();
        if (!await store.CanRenameParticipantAsync(actorId, personId, ct)) throw new EventAccessException();
        return (person, await store.GetParticipantEventIdsAsync(personId, ct));
    }
}
