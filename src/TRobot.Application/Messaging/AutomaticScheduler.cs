namespace TRobot.Application.Messaging;

public sealed record AutomaticScheduleCandidate(Guid EventId, Guid ActorId);

/// <summary>Discovery is a hint. Execution must recheck saved settings and current access atomically.</summary>
public interface IAutomaticSchedulerStore
{
    Task<IReadOnlyList<AutomaticScheduleCandidate>> CandidatesAsync(CancellationToken ct = default);
}
