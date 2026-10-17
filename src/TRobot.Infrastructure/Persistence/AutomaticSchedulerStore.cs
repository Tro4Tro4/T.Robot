using Microsoft.EntityFrameworkCore;
using TRobot.Application.Messaging;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

public sealed class AutomaticSchedulerStore(TRobotDbContext db) : IAutomaticSchedulerStore
{
    public async Task<IReadOnlyList<AutomaticScheduleCandidate>> CandidatesAsync(CancellationToken ct = default) =>
        await db.Set<BotSettings>().AsNoTracking()
            .Where(s => s.AutomaticEnabled && s.AutomaticActorId.HasValue
                && db.Users.Any(u => u.PersonId == s.AutomaticActorId && u.IsEnabled)
                && db.EventOrganizers.Any(o => o.EventId == s.EventId && o.PersonId == s.AutomaticActorId && o.IsActive))
            .OrderBy(s => s.EventId)
            .Select(s => new AutomaticScheduleCandidate(s.EventId, s.AutomaticActorId!.Value)).ToListAsync(ct);
}
