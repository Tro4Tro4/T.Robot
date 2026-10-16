using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TRobot.Application.Attendance;
using TRobot.Application.Demo;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

public sealed class AttendanceStore(TRobotDbContext db) : IAttendanceStore, IDemoSetupStore
{
    public Task<bool> IsOrganizerAsync(Guid eventId, Guid actorId, CancellationToken ct) =>
        db.EventOrganizers.AnyAsync(x => x.EventId == eventId && x.PersonId == actorId && x.IsActive
            && !db.Users.Any(u => u.PersonId == actorId && !u.IsEnabled), ct);

    public async Task<IReadOnlyList<EventOption>> GetEventsAsync(Guid actorId, CancellationToken ct) =>
        await (from ev in db.Events.AsNoTracking()
               join organizer in db.EventOrganizers on ev.Id equals organizer.EventId
               where organizer.PersonId == actorId && organizer.IsActive
                   && !db.Users.Any(u => u.PersonId == actorId && !u.IsEnabled)
               orderby ev.StartsAtUtc
               select new EventOption(ev.Id, ev.Name)).ToListAsync(ct);

    public async Task<AttendanceDashboard> GetDashboardAsync(Guid eventId, CancellationToken ct)
    {
        var name = await db.Events.Where(x => x.Id == eventId).Select(x => x.Name).SingleAsync(ct);
        var counts = await db.EventParticipants.Where(x => x.EventId == eventId)
            .GroupBy(x => x.Status).Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);
        var senders = await (from participant in db.EventParticipants.AsNoTracking()
                             join person in db.People.AsNoTracking() on participant.PersonId equals person.Id
                             where participant.EventId == eventId && person.WhatsAppSenderId != null
                             orderby person.DisplayName
                             select new SenderOption(person.WhatsAppSenderId!, person.DisplayName)).ToListAsync(ct);
        return new(eventId, name, counts.GetValueOrDefault(ParticipationStatus.Confirmed),
            counts.GetValueOrDefault(ParticipationStatus.Undecided), counts.GetValueOrDefault(ParticipationStatus.Absent),
            counts.GetValueOrDefault(ParticipationStatus.NoResponse), senders);
    }

    public Task<EventParticipant?> FindParticipantAsync(Guid eventId, string senderId, CancellationToken ct) =>
        (from participant in db.EventParticipants
         join person in db.People on participant.PersonId equals person.Id
         where participant.EventId == eventId && person.WhatsAppSenderId == senderId
         select participant).SingleOrDefaultAsync(ct);

    public Task<ParticipationReceipt?> FindReceiptAsync(Guid eventId, string messageId, CancellationToken ct) =>
        db.ParticipationReceipts.SingleOrDefaultAsync(x => x.EventId == eventId && x.MessageId == messageId, ct);

    public void AddReceipt(ParticipationReceipt receipt) => db.ParticipationReceipts.Add(receipt);
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
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
            throw new AttendanceConflictException("La partecipazione è cambiata durante il salvataggio. Riprova con lo stesso identificativo.");
        }
        catch (Exception ex) when (ex is SqliteException { SqliteErrorCode: 5 or 6 }
            || ex is DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 5 or 6 or 19 } })
        {
            db.ChangeTracker.Clear();
            throw new AttendanceConflictException("Un altro invio è in corso o il messaggio è già registrato. Riprova con lo stesso identificativo.");
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public Task<Guid> CreateIfEmptyAsync(Event ev, Person organizer, Person participant, CancellationToken ct) =>
        InTransactionAsync(async token =>
        {
            if (await db.Events.AnyAsync(token) || await db.People.AnyAsync(token))
                throw new InvalidOperationException("La preparazione demo richiede un database vuoto; nessun dato esistente è stato modificato.");
            db.AddRange(ev, organizer, participant,
                new EventOrganizer(ev.Id, organizer.Id, OrganizerRole.Administrator),
                new EventParticipant(ev.Id, participant.Id));
            await db.SaveChangesAsync(token);
            return organizer.Id;
        }, ct);
}
