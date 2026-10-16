using TRobot.Domain;

namespace TRobot.Application.Local;

public sealed class LocalAccessException : Exception;
public sealed class LocalValidationException(string message) : Exception(message);
public sealed class LocalConflictException(string message) : Exception(message);
public sealed record LocalPerson(Guid Id, string Name);
public sealed record LocalParticipation(Guid PersonId, string Name, ParticipationStatus Status);
public sealed record PublicEventSnapshot(int Confirmed, int Undecided, int Absent, int NoResponse,
    int OpenTasks, int OpenPolls, int ReadyMenu, string ChangeToken);

public interface ILocalStore
{
    Task<bool> CanManageAsync(Guid actorId, Guid eventId, CancellationToken ct);
    Task<Event?> EventAsync(Guid eventId, CancellationToken ct);
    Task<IReadOnlyList<LocalPerson>> PeopleAsync(Guid eventId, CancellationToken ct);
    Task<IReadOnlyList<LocalPerson>> ParticipantsAsync(Guid eventId, CancellationToken ct);
    Task<IReadOnlyList<LocalParticipation>> ParticipationsAsync(Guid eventId, CancellationToken ct);
    Task<PublicEventSnapshot> SnapshotAsync(Guid eventId, CancellationToken ct);
    Task<T?> FindAsync<T>(Guid eventId, Guid id, CancellationToken ct) where T : class, ILocalRecord;
    Task<IReadOnlyList<T>> ListAsync<T>(Guid eventId, CancellationToken ct) where T : class, ILocalRecord;
    void Add<T>(T entity) where T : class;
    Task SaveAsync(CancellationToken ct);
    Task<T> TransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
}

public sealed class LocalOperations(ILocalStore store, TimeProvider clock)
{
    public async Task AuthorizeAsync(Guid actorId, Guid eventId, CancellationToken ct = default)
    {
        if (actorId == Guid.Empty || eventId == Guid.Empty || !await store.CanManageAsync(actorId, eventId, ct))
            throw new LocalAccessException();
    }
    public async Task<IReadOnlyList<T>> ReadAsync<T>(Guid actorId, Guid eventId, CancellationToken ct = default) where T : class, ILocalRecord
    { await AuthorizeAsync(actorId, eventId, ct); return await store.ListAsync<T>(eventId, ct); }

    public Task<T> ChangeAsync<T>(Guid actorId, Guid eventId, Func<CancellationToken, Task<T>> action, CancellationToken ct = default) =>
        store.TransactionAsync(async token =>
        {
            await AuthorizeAsync(actorId, eventId, token);
            try { return await action(token); }
            catch (ArgumentException ex) { throw new LocalValidationException(ex.Message); }
            catch (InvalidOperationException ex) { throw new LocalValidationException(ex.Message); }
        }, ct);

    public void Audit(Guid eventId, Guid actorId, Guid entityId, string module, string action) =>
        store.Add(new LocalAudit(eventId, actorId, entityId, module, action, clock.GetUtcNow().UtcDateTime));
    public static void Revision(ILocalRecord entity, int expected)
    { if (expected < 0 || entity.Revision != expected) throw new LocalConflictException("Dati modificati. Ricarica la pagina."); }

    public async Task CheckPersonAsync(Guid eventId, Guid? personId, CancellationToken ct)
    {
        if (personId.HasValue && !(await store.PeopleAsync(eventId, ct)).Any(x => x.Id == personId))
            throw new LocalValidationException("La persona deve appartenere all'evento.");
    }
    public async Task<DateTime?> ToUtcAsync(Guid eventId, DateTime? local, CancellationToken ct)
    {
        if (!local.HasValue) return null;
        if (local.Value.Kind != DateTimeKind.Unspecified) throw new LocalValidationException("Inserisci una data locale senza offset.");
        var ev = await store.EventAsync(eventId, ct) ?? throw new LocalAccessException();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId);
        if (zone.IsInvalidTime(local.Value) || zone.IsAmbiguousTime(local.Value)) throw new LocalValidationException("Ora ambigua o inesistente al cambio dell'ora.");
        var utc = TimeZoneInfo.ConvertTimeToUtc(local.Value, zone);
        if (TimeZoneInfo.ConvertTimeFromUtc(utc, zone) != local.Value) throw new LocalValidationException("Data fuori dall'intervallo del fuso orario.");
        return utc;
    }
}
