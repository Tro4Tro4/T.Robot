using TRobot.Domain;

namespace TRobot.Application.Attendance;

public sealed record SimulatedMessage(Guid EventId, string SenderId, string MessageId, string Text);
public sealed record SimulationResult(bool Duplicate, bool Changed, ParticipationStatus Status);
public sealed record EventOption(Guid Id, string Name);
public sealed record SenderOption(string SenderId, string DisplayName);
public sealed record AttendanceDashboard(Guid EventId, string EventName, int Confirmed, int Undecided,
    int Absent, int NoResponse, IReadOnlyList<SenderOption> Senders)
{
    public int Total => Confirmed + Undecided + Absent + NoResponse;
}

public sealed class AttendanceValidationException(string message) : Exception(message);
public sealed class AttendanceAccessException : Exception;
public sealed class AttendanceConflictException(string message) : Exception(message);

public interface IAttendanceStore
{
    Task<bool> IsOrganizerAsync(Guid eventId, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<EventOption>> GetEventsAsync(Guid actorId, CancellationToken ct);
    Task<AttendanceDashboard> GetDashboardAsync(Guid eventId, CancellationToken ct);
    Task<EventParticipant?> FindParticipantAsync(Guid eventId, string senderId, CancellationToken ct);
    Task<ParticipationReceipt?> FindReceiptAsync(Guid eventId, string messageId, CancellationToken ct);
    void AddReceipt(ParticipationReceipt receipt);
    Task SaveAsync(CancellationToken ct);
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct);
}
