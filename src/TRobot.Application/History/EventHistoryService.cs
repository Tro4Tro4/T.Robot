using TRobot.Domain;

namespace TRobot.Application.History;

public enum HistoryCategory { All, Attendance, Event, Access, Local }
public sealed record HistoryEvent(Guid Id, string Name, string TimeZoneId);
public sealed record HistoryEntry(Guid Id, DateTime OccurredAtUtc, HistoryCategory Category, string ActorName,
    string? PersonName, ParticipationStatus? PreviousStatus, ParticipationStatus? Status,
    EventAuditAction? EventAction, string? AccessAction, AttendanceOrigin? Origin, string? LocalModule = null, string? LocalAction = null);
public sealed record HistoryPage(HistoryEvent Event, HistoryCategory Category, int Page,
    IReadOnlyList<HistoryEntry> Entries, bool HasNext);
public sealed class HistoryAccessException : Exception;
public sealed class HistoryValidationException(string message) : Exception(message);

public interface IEventHistoryStore
{
    Task<bool> IsOrganizerAsync(Guid actorId, Guid eventId, CancellationToken ct);
    Task<HistoryEvent?> GetEventAsync(Guid eventId, CancellationToken ct);
    Task<IReadOnlyList<HistoryEntry>> ReadAsync(Guid eventId, HistoryCategory category, int skip, int take, CancellationToken ct);
}

public sealed class EventHistoryService(IEventHistoryStore store)
{
    public const int PageSize = 25;

    public async Task<HistoryPage> GetAsync(Guid actorId, Guid eventId, HistoryCategory category = HistoryCategory.All,
        int page = 1, CancellationToken ct = default)
    {
        if (actorId == Guid.Empty || eventId == Guid.Empty || !await store.IsOrganizerAsync(actorId, eventId, ct))
            throw new HistoryAccessException();
        if (!Enum.IsDefined(category) || page is < 1 or > 100000)
            throw new HistoryValidationException("Filtro o pagina non validi.");
        var ev = await store.GetEventAsync(eventId, ct) ?? throw new HistoryAccessException();
        var rows = await store.ReadAsync(eventId, category, (page - 1) * PageSize, PageSize + 1, ct);
        return new(ev, category, page, rows.Take(PageSize).ToArray(), rows.Count > PageSize);
    }
}
