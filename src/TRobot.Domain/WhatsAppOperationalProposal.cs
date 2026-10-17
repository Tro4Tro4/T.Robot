namespace TRobot.Domain;

/// <summary>Technical confirmation only: no message text or clear confirmation code.</summary>
public sealed class WhatsAppOperationalProposal : ILocalRecord
{
    private WhatsAppOperationalProposal() { }
    public WhatsAppOperationalProposal(Guid eventId, Guid personId, Guid actorId, string groupId,
        int connectionRevision, SimulationAction action, Guid targetId, int targetRevision, string codeHash, DateTime utc)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); PersonId = Guard.Id(personId, nameof(personId));
        ActorId = Guard.Id(actorId, nameof(actorId)); TargetId = Guard.Id(targetId, nameof(targetId));
        GroupId = Guard.Text(groupId, 200, nameof(groupId)); CodeHash = WhatsAppIdentityAlias.Fingerprint(codeHash);
        if (!GroupId.EndsWith("@g.us", StringComparison.Ordinal) || connectionRevision < 0 || targetRevision < 0
            || !Enum.IsDefined(action) || utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Proposta non valida.");
        ConnectionRevision = connectionRevision; Action = action; TargetRevision = targetRevision;
        CreatedAtUtc = utc; ExpiresAtUtc = utc.AddMinutes(5);
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public Guid PersonId { get; private set; }
    public Guid ActorId { get; private set; }
    public string GroupId { get; private set; } = null!;
    public int ConnectionRevision { get; private set; }
    public SimulationAction Action { get; private set; }
    public Guid TargetId { get; private set; }
    public int TargetRevision { get; private set; }
    public string CodeHash { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }
    public int Revision { get; private set; }
    public void Consume(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc || utc < CreatedAtUtc || utc >= ExpiresAtUtc || ConsumedAtUtc.HasValue)
            throw new ArgumentException("Proposta non disponibile.");
        ConsumedAtUtc = utc; Revision = checked(Revision + 1);
    }
}
