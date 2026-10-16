using Microsoft.EntityFrameworkCore;
using TRobot.Application.History;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

public sealed class EventHistoryStore(TRobotDbContext db) : IEventHistoryStore
{
    public Task<bool> IsOrganizerAsync(Guid actorId, Guid eventId, CancellationToken ct) =>
        db.EventOrganizers.AnyAsync(x => x.PersonId == actorId && x.EventId == eventId && x.IsActive
            && db.Users.Any(u => u.PersonId == actorId && u.IsEnabled), ct);
    public Task<HistoryEvent?> GetEventAsync(Guid eventId, CancellationToken ct) =>
        db.Events.AsNoTracking().Where(x => x.Id == eventId).Select(x => new HistoryEvent(x.Id, x.Name, x.TimeZoneId)).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<HistoryEntry>> ReadAsync(Guid eventId, HistoryCategory category, int skip, int take, CancellationToken ct)
    {
        var attendance = from receipt in db.ParticipationReceipts.AsNoTracking()
                         join actor in db.People on receipt.OrganizerPersonId equals actor.Id
                         join person in db.People on receipt.PersonId equals person.Id
                         where receipt.EventId == eventId
                         select new HistoryRow
                         {
                             Id = receipt.Id, OccurredAtUtc = receipt.ProcessedAtUtc, Category = HistoryCategory.Attendance,
                             ActorName = actor.DisplayName, PersonName = person.DisplayName, PreviousStatus = receipt.PreviousStatus.ToString(),
                             Status = receipt.Status.ToString(), EventAction = null, AccessAction = null, Origin = receipt.Origin.ToString(), LocalModule = null, LocalAction = null
                         };
        var events = from audit in db.EventAudits.AsNoTracking()
                     join actor in db.People on audit.ActorId equals actor.Id
                     join target in db.People on audit.PersonId equals target.Id into targets
                     from person in targets.DefaultIfEmpty()
                     where audit.EventId == eventId
                     select new HistoryRow
                     {
                         Id = audit.Id, OccurredAtUtc = audit.OccurredAtUtc, Category = HistoryCategory.Event,
                         ActorName = actor.DisplayName, PersonName = person == null ? null : person.DisplayName,
                         PreviousStatus = null, Status = null, EventAction = audit.Action.ToString(), AccessAction = null, Origin = null, LocalModule = null, LocalAction = null
                     };
        var access = from audit in db.AccountAudits.AsNoTracking()
                     join source in db.People on audit.ActorId equals source.Id into actors
                     from actor in actors.DefaultIfEmpty()
                     join person in db.People on audit.PersonId equals person.Id
                     where audit.EventId == eventId && (audit.Action == "OrganizerCreated" || audit.Action == "EventGranted" || audit.Action == "EventRevoked")
                     select new HistoryRow
                     {
                         Id = audit.Id, OccurredAtUtc = audit.OccurredAtUtc, Category = HistoryCategory.Access,
                         ActorName = actor == null ? "Operatore locale" : actor.DisplayName, PersonName = person.DisplayName,
                         PreviousStatus = null, Status = null, EventAction = null, AccessAction = audit.Action, Origin = null, LocalModule = null, LocalAction = null
                     };
        var local = from audit in db.Set<LocalAudit>().AsNoTracking()
                    join actor in db.People on audit.ActorId equals actor.Id
                    where audit.EventId == eventId
                    select new HistoryRow
                    {
                        Id = audit.Id, OccurredAtUtc = audit.AtUtc, Category = HistoryCategory.Local, ActorName = actor.DisplayName,
                        PersonName = null, PreviousStatus = null, Status = null, EventAction = null, AccessAction = null, Origin = null,
                        LocalModule = audit.Module, LocalAction = audit.Action
                    };
        var query = category switch
        {
            HistoryCategory.Attendance => attendance,
            HistoryCategory.Event => events,
            HistoryCategory.Access => access,
            HistoryCategory.Local => local,
            _ => attendance.Concat(events).Concat(access).Concat(local)
        };
        var rows = await query.OrderByDescending(x => x.OccurredAtUtc).ThenBy(x => x.Category).ThenBy(x => x.Id)
            .Skip(skip).Take(take).ToListAsync(ct);
        return rows.Select(x => new HistoryEntry(x.Id, DateTime.SpecifyKind(x.OccurredAtUtc, DateTimeKind.Utc), x.Category, x.ActorName,
            x.PersonName, Parse<ParticipationStatus>(x.PreviousStatus), Parse<ParticipationStatus>(x.Status),
            Parse<EventAuditAction>(x.EventAction), x.AccessAction, Parse<AttendanceOrigin>(x.Origin), x.LocalModule, x.LocalAction)).ToArray();
    }

    private static T? Parse<T>(string? value) where T : struct, Enum => value is null ? null : Enum.Parse<T>(value);

    // Member projection keeps UNION ALL and pagination in SQL; construct the public record afterwards.
    private sealed class HistoryRow
    {
        public Guid Id { get; set; }
        public DateTime OccurredAtUtc { get; set; }
        public HistoryCategory Category { get; set; }
        public string ActorName { get; set; } = "";
        public string? PersonName { get; set; }
        public string? PreviousStatus { get; set; }
        public string? Status { get; set; }
        public string? EventAction { get; set; }
        public string? AccessAction { get; set; }
        public string? Origin { get; set; }
        public string? LocalModule { get; set; }
        public string? LocalAction { get; set; }
    }
}
