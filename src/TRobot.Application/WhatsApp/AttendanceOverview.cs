using TRobot.Domain;

namespace TRobot.Application.WhatsApp;

public sealed record AttendanceStayRow(Guid PersonId, string Name, ParticipationStatus Status, AttendanceStay? Stay);
public sealed record AttendanceDailyCount(DateOnly Day, int People);
public sealed record AttendanceOverview(Guid EventId, string EventName, IReadOnlyList<AttendanceStayRow> People, IReadOnlyList<AttendanceDailyCount> Days);

public sealed partial class WhatsAppService
{
    public async Task<AttendanceOverview> AttendanceOverviewAsync(Guid actorId, Guid eventId, CancellationToken ct = default)
    {
        await operations.AuthorizeAsync(actorId, eventId, ct);
        var ev = (await store.EventAsync(eventId, ct))!;
        var stays = await store.ListAsync<AttendanceStay>(eventId, ct);
        var rows = (await store.ParticipationsAsync(eventId, ct)).Select(p => new AttendanceStayRow(p.PersonId, p.Name, p.Status,
            stays.SingleOrDefault(s => s.Id == p.PersonId))).ToArray();
        var counts = rows.Where(p => p.Status == ParticipationStatus.Confirmed && p.Stay is not null)
            .SelectMany(p => Enumerable.Range(0, p.Stay!.Days).Select(offset => p.Stay.Arrival.AddDays(offset)))
            .GroupBy(day => day).OrderBy(g => g.Key).Select(g => new AttendanceDailyCount(g.Key, g.Count())).ToArray();
        await operations.AuthorizeAsync(actorId, eventId, ct);
        return new(eventId, ev.Name, rows, counts);
    }
}
