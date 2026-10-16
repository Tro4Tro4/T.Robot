using TRobot.Domain;

namespace TRobot.Application.Events;

public sealed record EventInput(string Name, DateTime LocalStart, string TimeZoneId);
public sealed record ParticipantSummary(Guid PersonId, string DisplayName, ParticipationStatus Status, bool HasSimulationSender, bool CanRename);
public sealed record EventDetails(Event Event, IReadOnlyList<ParticipantSummary> Participants);
public sealed class EventValidationException(string message) : Exception(message);
public sealed class EventAccessException : Exception;
public sealed class EventConflictException(string message) : Exception(message);

public interface IEventManagementStore
{
    Task<bool> IsAdministratorAsync(Guid actorId, CancellationToken ct);
    Task<bool> IsOrganizerAsync(Guid eventId, Guid actorId, CancellationToken ct);
    Task<Event?> FindEventAsync(Guid eventId, CancellationToken ct);
    Task<IReadOnlyList<ParticipantSummary>> GetParticipantsAsync(Guid actorId, Guid eventId, CancellationToken ct);
    Task<EventAudit?> FindAuditAsync(Guid operationId, CancellationToken ct);
    Task<Person?> FindPersonAsync(Guid personId, CancellationToken ct);
    Task<Person?> FindParticipantPersonAsync(Guid eventId, Guid personId, CancellationToken ct);
    Task<bool> CanRenameParticipantAsync(Guid actorId, Guid personId, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetParticipantEventIdsAsync(Guid personId, CancellationToken ct);
    Task<IReadOnlyList<EventAudit>> GetLegacyRegistrationsAsync(Guid personId, CancellationToken ct);
    void AddEvent(Event ev, EventOrganizer organizer);
    void AddParticipant(Person person, EventParticipant participant);
    void AddAudit(EventAudit audit);
    Task SaveAsync(CancellationToken ct);
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
}

public sealed class EventManagementService(IEventManagementStore store, TimeProvider clock)
{
    public Task<bool> CanCreateAsync(Guid actorId, CancellationToken ct = default) =>
        actorId == Guid.Empty ? Task.FromResult(false) : store.IsAdministratorAsync(actorId, ct);

    public async Task<EventDetails> GetAsync(Guid actorId, Guid eventId, CancellationToken ct = default)
    {
        await AuthorizeAsync(actorId, eventId, ct);
        var ev = await store.FindEventAsync(eventId, ct) ?? throw new EventAccessException();
        return new(ev, await store.GetParticipantsAsync(actorId, eventId, ct));
    }

    public Task<Guid> CreateAsync(Guid actorId, EventInput input, CancellationToken ct = default) =>
        store.InTransactionAsync(async token =>
        {
            if (!await CanCreateAsync(actorId, token)) throw new EventAccessException();
            var ev = Validate(input);
            store.AddEvent(ev, new EventOrganizer(ev.Id, actorId, OrganizerRole.Administrator));
            store.AddAudit(new(ev.Id, actorId, EventAuditAction.EventCreated, clock.GetUtcNow().UtcDateTime));
            await store.SaveAsync(token);
            return ev.Id;
        }, ct);

    public Task<bool> UpdateAsync(Guid actorId, Guid eventId, int expectedRevision, EventInput input, CancellationToken ct = default) =>
        store.InTransactionAsync(async token =>
        {
            await AuthorizeAsync(actorId, eventId, token);
            var valid = Validate(input);
            var ev = await store.FindEventAsync(eventId, token) ?? throw new EventAccessException();
            if (expectedRevision < 0 || ev.Revision != expectedRevision)
                throw new EventConflictException("L'evento è stato modificato. Ricarica la pagina prima di riprovare.");
            var changed = ev.UpdateDetails(valid.Name, valid.StartsAtUtc, valid.TimeZoneId);
            if (changed)
            {
                store.AddAudit(new(eventId, actorId, EventAuditAction.EventUpdated, clock.GetUtcNow().UtcDateTime));
                await store.SaveAsync(token);
            }
            return changed;
        }, ct);

    public Task<Guid> AddParticipantAsync(Guid actorId, Guid eventId, Guid operationId, string displayName,
        bool enableSimulation, CancellationToken ct = default) => store.InTransactionAsync(async token =>
    {
        await AuthorizeAsync(actorId, eventId, token);
        if (operationId == Guid.Empty) throw new EventValidationException("Identificativo operazione non valido. Ricarica la pagina.");
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 150)
            throw new EventValidationException("Il nome deve contenere da 1 a 150 caratteri.");
        var name = displayName.Trim();
        var fingerprint = RegistrationFingerprint.Compute(name, enableSimulation);
        var previous = await store.FindAuditAsync(operationId, token);
        if (previous is not null)
        {
            var person = previous.PersonId.HasValue ? await store.FindPersonAsync(previous.PersonId.Value, token) : null;
            if (previous.EventId != eventId || previous.ActorId != actorId || previous.Action != EventAuditAction.ParticipantAdded
                || person is null || (previous.RegistrationFingerprint
                    ?? RegistrationFingerprint.Compute(person.DisplayName, person.WhatsAppSenderId is not null)) != fingerprint)
                throw new EventConflictException("Identificativo operazione già usato con dati diversi. Ricarica la pagina.");
            return person.Id;
        }
        var added = new Person(name, enableSimulation ? "sim-" + Guid.NewGuid().ToString("N") : null);
        store.AddParticipant(added, new EventParticipant(eventId, added.Id));
        store.AddAudit(new(eventId, actorId, EventAuditAction.ParticipantAdded, clock.GetUtcNow().UtcDateTime, added.Id, operationId, fingerprint));
        await store.SaveAsync(token);
        return added.Id;
    }, ct);

    private async Task AuthorizeAsync(Guid actorId, Guid eventId, CancellationToken ct)
    {
        if (actorId == Guid.Empty || eventId == Guid.Empty || !await store.IsOrganizerAsync(eventId, actorId, ct))
            throw new EventAccessException();
    }

    private static Event Validate(EventInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 200)
            throw new EventValidationException("Il nome evento deve contenere da 1 a 200 caratteri.");
        if (input.LocalStart == default || input.LocalStart.Kind != DateTimeKind.Unspecified)
            throw new EventValidationException("Inserisci la data e l'ora locali dell'evento senza offset.");
        if (string.IsNullOrWhiteSpace(input.TimeZoneId) || input.TimeZoneId.Trim().Length > 100)
            throw new EventValidationException("Inserisci un fuso orario valido, ad esempio Europe/Rome.");
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId.Trim()); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        { throw new EventValidationException("Fuso orario non riconosciuto."); }
        if (zone.IsInvalidTime(input.LocalStart))
            throw new EventValidationException("L'ora scelta non esiste per il cambio dell'ora legale. Scegli un altro orario.");
        if (zone.IsAmbiguousTime(input.LocalStart))
            throw new EventValidationException("L'ora scelta è ambigua per il cambio dell'ora legale. Scegli un altro orario.");
        var utc = TimeZoneInfo.ConvertTimeToUtc(input.LocalStart, zone);
        // Avoid silent clamping at DateTime boundaries during time-zone conversion.
        if (TimeZoneInfo.ConvertTimeFromUtc(utc, zone) != input.LocalStart)
            throw new EventValidationException("Data fuori dall'intervallo supportato dal fuso orario.");
        return new Event(input.Name.Trim(), utc, zone.Id);
    }
}
