using Microsoft.EntityFrameworkCore;
using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

public sealed class LocalDashboardStore(TRobotDbContext db) : ILocalDashboardStore
{
    public async Task<LocalCounts> CountAsync(Guid eventId, DateTime nowUtc, CancellationToken ct)
    {
        var households = await db.Set<Household>().CountAsync(x => x.EventId == eventId, ct);
        var members = await db.Set<HouseholdMember>().CountAsync(x => x.EventId == eventId && x.IsActive, ct);
        var menu = db.Set<MenuItem>().Where(x => x.EventId == eventId);
        var ready = await menu.CountAsync(x => x.Availability == MenuAvailability.Ready, ct);
        var assigned = await menu.CountAsync(x => x.Availability == MenuAvailability.Assigned, ct);
        var tasks = db.Set<OrganizationTask>().Where(x => x.EventId == eventId && x.Status != OrganizationTaskStatus.Completed && x.Status != OrganizationTaskStatus.Cancelled);
        var openTasks = await tasks.CountAsync(ct);
        var overdue = await tasks.CountAsync(x => x.DueAtUtc.HasValue && x.DueAtUtc < nowUtc, ct);
        var polls = db.Set<Poll>().Where(x => x.EventId == eventId && x.Status == PollStatus.PublicationQueued
            && db.Set<OutboundMessage>().Any(m => m.EventId == eventId && m.Id == x.PublicationMessageId && m.Status == OutboundMessageStatus.Sent));
        var openPolls = await polls.CountAsync(x => x.DeadlineUtc > nowUtc, ct);
        var expiredPolls = await polls.CountAsync(x => x.DeadlineUtc <= nowUtc, ct);
        var queued = await db.Set<OutboundMessage>().CountAsync(x => x.EventId == eventId && x.Status == OutboundMessageStatus.Queued, ct);
        var expenses = db.Set<Expense>().Where(x => x.EventId == eventId);
        // Aggregate exact integer cents in SQL: SQLite has no native decimal SUM.
        var approvedCents = await expenses.Where(x => x.Status == ExpenseStatus.Approved).Select(x => (long)(x.Amount * 100m)).SumAsync(ct);
        var pending = await expenses.CountAsync(x => x.Status == ExpenseStatus.Pending, ct);
        var declared = await db.Set<Payment>().CountAsync(x => x.EventId == eventId && x.Status == PaymentStatus.Declared, ct);
        var players = await db.Set<GameMembership>().CountAsync(x => x.EventId == eventId && x.IsActive, ct);
        var quizzes = db.Set<Quiz>().Where(x => x.EventId == eventId && x.Status == QuizStatus.PublicationQueued
            && db.Set<OutboundMessage>().Any(m => m.EventId == eventId && m.Id == x.PublicationMessageId && m.Status == OutboundMessageStatus.Sent));
        var openQuizzes = await quizzes.CountAsync(x => x.DeadlineUtc > nowUtc, ct);
        var expiredQuizzes = await quizzes.CountAsync(x => x.DeadlineUtc <= nowUtc, ct);
        return new(households, members, ready, assigned, openTasks, overdue, openPolls, expiredPolls, queued,
            approvedCents / 100m, pending, declared, players, openQuizzes, expiredQuizzes);
    }
}
