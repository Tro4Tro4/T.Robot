namespace TRobot.Domain;

public enum TaskPriority { Low, Normal, High }
public enum OrganizationTaskStatus { Todo, InProgress, Completed, Cancelled }

public sealed class OrganizationTask : ILocalRecord
{
    private OrganizationTask() { }
    public OrganizationTask(Guid eventId, string title, string? description, TaskPriority priority,
        Guid? assigneeId, DateTime? dueAtUtc, Guid? id = null)
    {
        if (id.HasValue) Id = Guard.Id(id.Value, nameof(id));
        EventId = Guard.Id(eventId, nameof(eventId));
        Set(title, description, priority, OrganizationTaskStatus.Todo, assigneeId, dueAtUtc);
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public int Revision { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public TaskPriority Priority { get; private set; }
    public OrganizationTaskStatus Status { get; private set; }
    public Guid? AssigneeId { get; private set; }
    public DateTime? DueAtUtc { get; private set; }

    private void Set(string title, string? description, TaskPriority priority, OrganizationTaskStatus status,
        Guid? assigneeId, DateTime? dueAtUtc)
    {
        var validTitle = Guard.Text(title, 160, nameof(title));
        var validDescription = Guard.OptionalText(description?.ReplaceLineEndings("\n"), 2000, nameof(description));
        if (!Enum.IsDefined(priority) || !Enum.IsDefined(status)) throw new ArgumentException("Stato o priorità non validi.");
        if (assigneeId == Guid.Empty) throw new ArgumentException("Responsabile non valido.");
        if (dueAtUtc.HasValue && dueAtUtc.Value.Kind != DateTimeKind.Utc) throw new ArgumentException("La scadenza deve essere UTC.");
        Title = validTitle; Description = validDescription; Priority = priority;
        Status = status; AssigneeId = assigneeId; DueAtUtc = dueAtUtc;
    }
    public bool Update(string title, string? description, TaskPriority priority, OrganizationTaskStatus status,
        Guid? assigneeId, DateTime? dueAtUtc)
    {
        var candidate = new OrganizationTask(EventId, title, description, priority, assigneeId, dueAtUtc);
        if (!Enum.IsDefined(status)) throw new ArgumentException("Stato non valido.");
        if (Title == candidate.Title && Description == candidate.Description && Priority == priority && Status == status
            && AssigneeId == assigneeId && DueAtUtc == dueAtUtc) return false;
        var revision = checked(Revision + 1);
        Set(candidate.Title, candidate.Description, priority, status, assigneeId, dueAtUtc);
        Revision = revision;
        return true;
    }
}
