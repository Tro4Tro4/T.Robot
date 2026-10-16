using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TRobot.Application.Events;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

public sealed class EventManagementStore(TRobotDbContext db) : IEventManagementStore
{
    public Task<bool> IsAdministratorAsync(Guid actorId, CancellationToken ct) =>
        db.Users.AnyAsync(x => x.PersonId == actorId && x.IsEnabled && x.IsSystemAdministrator, ct);

    public Task<bool> IsOrganizerAsync(Guid eventId, Guid actorId, CancellationToken ct) =>
        db.EventOrganizers.AnyAsync(x => x.EventId == eventId && x.PersonId == actorId && x.IsActive
            && db.Users.Any(u => u.PersonId == actorId && u.IsEnabled), ct);

    public Task<Event?> FindEventAsync(Guid eventId, CancellationToken ct) => db.Events.SingleOrDefaultAsync(x => x.Id == eventId, ct);

    private IQueryable<Person> EditablePeople(Guid actorId) => db.People.Where(person =>
        db.Users.Any(actor => actor.PersonId == actorId && actor.IsEnabled)
        && !db.Users.Any(account => account.PersonId == person.Id)
        && !db.EventOrganizers.Any(organizer => organizer.PersonId == person.Id)
        && !db.EventParticipants.Any(member => member.PersonId == person.Id
            && !db.EventOrganizers.Any(access => access.EventId == member.EventId && access.PersonId == actorId && access.IsActive)));

    public async Task<IReadOnlyList<ParticipantSummary>> GetParticipantsAsync(Guid actorId, Guid eventId, CancellationToken ct)
    {
        var editablePeople = EditablePeople(actorId);
        return await (from member in db.EventParticipants.AsNoTracking()
               join person in db.People.AsNoTracking() on member.PersonId equals person.Id
               where member.EventId == eventId
               orderby person.DisplayName, person.Id
               select new ParticipantSummary(person.Id, person.DisplayName, member.Status, person.WhatsAppSenderId != null,
                   editablePeople.Any(editable => editable.Id == person.Id))).ToListAsync(ct);
    }

    public Task<Person?> FindParticipantPersonAsync(Guid eventId, Guid personId, CancellationToken ct) =>
        db.People.SingleOrDefaultAsync(person => person.Id == personId
            && db.EventParticipants.Any(member => member.EventId == eventId && member.PersonId == person.Id), ct);
    public Task<bool> CanRenameParticipantAsync(Guid actorId, Guid personId, CancellationToken ct) =>
        EditablePeople(actorId).AnyAsync(x => x.Id == personId, ct);
    public async Task<IReadOnlyList<Guid>> GetParticipantEventIdsAsync(Guid personId, CancellationToken ct) =>
        await db.EventParticipants.Where(x => x.PersonId == personId).Select(x => x.EventId).ToListAsync(ct);
    public async Task<IReadOnlyList<EventAudit>> GetLegacyRegistrationsAsync(Guid personId, CancellationToken ct) =>
        await db.EventAudits.Where(x => x.PersonId == personId && x.Action == EventAuditAction.ParticipantAdded
            && x.RegistrationFingerprint == null).ToListAsync(ct);

    public Task<EventAudit?> FindAuditAsync(Guid operationId, CancellationToken ct) =>
        db.EventAudits.AsNoTracking().SingleOrDefaultAsync(x => x.Id == operationId, ct);
    public Task<Person?> FindPersonAsync(Guid personId, CancellationToken ct) =>
        db.People.AsNoTracking().SingleOrDefaultAsync(x => x.Id == personId, ct);
    public void AddEvent(Event ev, EventOrganizer organizer) => db.AddRange(ev, organizer);
    public void AddParticipant(Person person, EventParticipant participant) => db.AddRange(person, participant);
    public void AddAudit(EventAudit audit) => db.EventAudits.Add(audit);
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        // Each operation reads current data, including when the scoped service is reused.
        db.ChangeTracker.Clear();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await action(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            throw new EventConflictException("I dati sono stati modificati. Ricarica la pagina prima di riprovare.");
        }
        catch (Exception ex) when (ex is SqliteException { SqliteErrorCode: 5 or 6 }
            || ex is DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 5 or 6 or 19 } })
        {
            db.ChangeTracker.Clear();
            throw new EventConflictException("Salvataggio in conflitto. Riprova l'operazione; se il problema persiste ricarica la pagina.");
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }
}
