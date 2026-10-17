namespace TRobot.Domain;

public enum WhatsAppListKind { Menu, Todo }

/// <summary>Immutable publication approval; stores a fingerprint, never a copy of list text.</summary>
public sealed class WhatsAppListApproval : ILocalRecord
{
    private WhatsAppListApproval() { }
    public WhatsAppListApproval(Guid eventId, Guid id, Guid actorId, WhatsAppListKind kind, string fingerprint,
        string groupId, int connectionRevision, DateTime utc)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); Id = Guard.Id(id, nameof(id)); ActorId = Guard.Id(actorId, nameof(actorId));
        if (!Enum.IsDefined(kind) || connectionRevision < 0 || utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Approvazione non valida.");
        Kind = kind; Fingerprint = WhatsAppIdentityAlias.Fingerprint(fingerprint); GroupId = Guard.Text(groupId, 200, nameof(groupId));
        if (!GroupId.EndsWith("@g.us", StringComparison.Ordinal)) throw new ArgumentException("Gruppo non valido.");
        ConnectionRevision = connectionRevision; ApprovedAtUtc = utc;
    }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid ActorId { get; private set; }
    public WhatsAppListKind Kind { get; private set; }
    public string Fingerprint { get; private set; } = null!;
    public string GroupId { get; private set; } = null!;
    public int ConnectionRevision { get; private set; }
    public DateTime ApprovedAtUtc { get; private set; }
    public int Revision { get; private set; }
}

public sealed class WhatsAppCommandReceipt : ILocalRecord
{
    private WhatsAppCommandReceipt() { }
    public WhatsAppCommandReceipt(Guid eventId, string messageHash, string commandHash, Guid personId, Guid actorId, DateTime utc)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); MessageHash = WhatsAppIdentityAlias.Fingerprint(messageHash);
        CommandHash = WhatsAppIdentityAlias.Fingerprint(commandHash); PersonId = Guard.Id(personId, nameof(personId)); ActorId = Guard.Id(actorId, nameof(actorId));
        if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("La data deve essere UTC."); AtUtc = utc;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public string MessageHash { get; private set; } = null!;
    public string CommandHash { get; private set; } = null!;
    public Guid PersonId { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTime AtUtc { get; private set; }
    public int Revision { get; private set; }
}
