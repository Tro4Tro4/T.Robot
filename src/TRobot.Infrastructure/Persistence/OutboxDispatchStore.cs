using Microsoft.EntityFrameworkCore;
using TRobot.Application.Messaging;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

/// <summary>Read-only discovery for the production dispatcher. It never mutates the outbox.</summary>
public sealed class OutboxDispatchStore(TRobotDbContext db) : IOutboxDispatchStore
{
    public async Task<IReadOnlyList<Guid>> DispatchCandidatesAsync(DateTime nowUtc, DateTime staleBeforeUtc, CancellationToken ct = default) =>
        await db.Set<OutboundMessage>().AsNoTracking()
            .Where(x => x.DeliveryMode == MessageDeliveryMode.WhatsApp
                && ((x.Status == OutboundMessageStatus.Queued && x.ScheduledAtUtc <= nowUtc)
                    || (x.Status == OutboundMessageStatus.Sending && x.LastAttemptAtUtc != null && x.LastAttemptAtUtc <= staleBeforeUtc)))
            .Select(x => x.EventId).Distinct().OrderBy(x => x).ToListAsync(ct);
}
