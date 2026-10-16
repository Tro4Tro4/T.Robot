namespace TRobot.Application.Local;

public sealed record LocalCounts(int Households, int HouseholdMembers, int MenuReady, int MenuAssigned, int OpenTasks, int OverdueTasks,
    int OpenPolls, int ExpiredPolls, int QueuedMessages, decimal ApprovedExpenses, int PendingExpenses, int DeclaredPayments,
    int GameParticipants, int OpenQuizzes, int ExpiredQuizzes);
public sealed record LocalOverview(DateTime StartsAtUtc, string TimeZoneId, TimeSpan Remaining, LocalCounts Counts);
public interface ILocalDashboardStore
{
    Task<LocalCounts> CountAsync(Guid eventId, DateTime nowUtc, CancellationToken ct);
}
public sealed class LocalDashboardService(ILocalStore store, ILocalDashboardStore dashboard, LocalOperations operations, TimeProvider clock)
{
    public async Task<LocalOverview> ReadAsync(Guid actorId, Guid eventId, CancellationToken ct = default)
    {
        await operations.AuthorizeAsync(actorId, eventId, ct);
        var ev = await store.EventAsync(eventId, ct) ?? throw new LocalAccessException();
        var now = clock.GetUtcNow().UtcDateTime;
        return new(ev.StartsAtUtc, ev.TimeZoneId, ev.StartsAtUtc - now, await dashboard.CountAsync(eventId, now, ct));
    }
}
