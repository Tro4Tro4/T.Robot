namespace TRobot.Domain;

public sealed class AccountAudit
{
    private AccountAudit() { }
    public AccountAudit(Guid personId, Guid? actorId, string action, DateTime occurredAtUtc, Guid? eventId = null)
    {
        PersonId = Guard.Id(personId, nameof(personId));
        ActorId = actorId.HasValue ? Guard.Id(actorId.Value, nameof(actorId)) : null;
        Action = Guard.Text(action, 50, nameof(action));
        EventId = eventId.HasValue ? Guard.Id(eventId.Value, nameof(eventId)) : null;
        if (occurredAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("La data deve essere UTC.", nameof(occurredAtUtc));
        OccurredAtUtc = occurredAtUtc;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid PersonId { get; private set; }
    public Guid? ActorId { get; private set; }
    public Guid? EventId { get; private set; }
    public string Action { get; private set; } = null!;
    public DateTime OccurredAtUtc { get; private set; }
}
