using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

public sealed class LocalStore(TRobotDbContext db, TimeProvider clock) : ILocalStore
{
    public async Task<IReadOnlyList<LocalParticipation>> ParticipationsAsync(Guid eventId, CancellationToken ct) =>
        await db.EventParticipants.AsNoTracking().Where(x => x.EventId == eventId)
            .Join(db.People.AsNoTracking(), x => x.PersonId, p => p.Id,
                (x, p) => new { p.Id, p.DisplayName, x.Status }).OrderBy(x => x.DisplayName)
            .Select(x => new LocalParticipation(x.Id, x.DisplayName, x.Status)).ToListAsync(ct);
    public Task<bool> CanManageAsync(Guid actorId, Guid eventId, CancellationToken ct) => db.EventOrganizers.AnyAsync(x =>
        x.PersonId == actorId && x.EventId == eventId && x.IsActive && db.Users.Any(u => u.PersonId == actorId && u.IsEnabled), ct);
    public Task<Event?> EventAsync(Guid eventId, CancellationToken ct) => db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId, ct);
    public async Task<IReadOnlyList<LocalPerson>> PeopleAsync(Guid eventId, CancellationToken ct) => await db.People.AsNoTracking()
        .Where(p => db.EventParticipants.Any(m => m.EventId == eventId && m.PersonId == p.Id)
            || db.EventOrganizers.Any(o => o.EventId == eventId && o.PersonId == p.Id && o.IsActive))
        .OrderBy(p => p.DisplayName).Select(p => new LocalPerson(p.Id, p.DisplayName)).ToListAsync(ct);
    public async Task<IReadOnlyList<LocalPerson>> ParticipantsAsync(Guid eventId, CancellationToken ct) => await db.People.AsNoTracking()
        .Where(p => db.EventParticipants.Any(m => m.EventId == eventId && m.PersonId == p.Id))
        .OrderBy(p => p.DisplayName).Select(p => new LocalPerson(p.Id, p.DisplayName)).ToListAsync(ct);
    public async Task<PublicEventSnapshot> SnapshotAsync(Guid eventId, CancellationToken ct)
    {
        var counts = await db.EventParticipants.Where(x => x.EventId == eventId).GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        var tasks = await db.Set<OrganizationTask>().CountAsync(x => x.EventId == eventId
            && x.Status != OrganizationTaskStatus.Completed && x.Status != OrganizationTaskStatus.Cancelled, ct);
        var menu = db.Model.FindEntityType(typeof(MenuItem)) is null ? 0 : await db.Set<MenuItem>()
            .CountAsync(x => x.EventId == eventId && x.Availability == MenuAvailability.Ready, ct);
        var confirmed = counts.GetValueOrDefault(ParticipationStatus.Confirmed);
        var undecided = counts.GetValueOrDefault(ParticipationStatus.Undecided);
        var absent = counts.GetValueOrDefault(ParticipationStatus.Absent);
        var noResponse = counts.GetValueOrDefault(ParticipationStatus.NoResponse);
        var now = clock.GetUtcNow().UtcDateTime;
        var polls = await db.Set<Poll>().CountAsync(x => x.EventId == eventId && x.Status == PollStatus.PublicationQueued
            && x.DeadlineUtc > now && db.Set<OutboundMessage>().Any(m => m.EventId == eventId && m.Id == x.PublicationMessageId && m.Status == OutboundMessageStatus.Sent), ct);
        return new(confirmed, undecided, absent, noResponse, tasks, polls, menu, $"{confirmed}:{undecided}:{absent}:{noResponse}:{tasks}:{polls}:{menu}");
    }
    public Task<T?> FindAsync<T>(Guid eventId, Guid id, CancellationToken ct) where T : class, ILocalRecord => db.Set<T>()
        .SingleOrDefaultAsync(x => EF.Property<Guid>(x, "EventId") == eventId && EF.Property<Guid>(x, "Id") == id, ct);
    public async Task<IReadOnlyList<T>> ListAsync<T>(Guid eventId, CancellationToken ct) where T : class, ILocalRecord => await db.Set<T>()
        .AsNoTracking().Where(x => EF.Property<Guid>(x, "EventId") == eventId).ToListAsync(ct);
    public void Add<T>(T entity) where T : class => db.Add(entity);
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
    public async Task<T> TransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await action(ct); await transaction.CommitAsync(ct); return result;
        }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); throw new LocalConflictException("Dati modificati. Ricarica la pagina."); }
        catch (Exception ex) when (ex is SqliteException { SqliteErrorCode: 5 or 6 }
            || ex is DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 5 or 6 or 19 } })
        { db.ChangeTracker.Clear(); throw new LocalConflictException("Salvataggio in conflitto. Ricarica la pagina."); }
        catch { db.ChangeTracker.Clear(); throw; }
    }
}
