namespace TRobot.Domain;

public interface ILocalRecord
{
    Guid Id { get; }
    Guid EventId { get; }
    int Revision { get; }
}

public sealed class LocalAudit : ILocalRecord
{
    private LocalAudit() { }
    public LocalAudit(Guid eventId, Guid actorId, Guid entityId, string module, string action, DateTime atUtc)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); ActorId = Guard.Id(actorId, nameof(actorId));
        EntityId = Guard.Id(entityId, nameof(entityId)); Module = Guard.Text(module, 40, nameof(module));
        Action = Guard.Text(action, 80, nameof(action));
        if (atUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("La data deve essere UTC.");
        AtUtc = atUtc;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid EntityId { get; private set; }
    public string Module { get; private set; } = null!;
    public string Action { get; private set; } = null!;
    public DateTime AtUtc { get; private set; }
    public int Revision { get; private set; }
}
