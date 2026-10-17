namespace TRobot.Domain;

public sealed class WhatsAppConnection : ILocalRecord
{
    private WhatsAppConnection() { }
    public WhatsAppConnection(Guid eventId) { Id = EventId = Guard.Id(eventId, nameof(eventId)); }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string? GroupId { get; private set; }
    public Guid? ActorId { get; private set; }
    public bool Enabled { get; private set; }
    public DateTime? ActivatedAtUtc { get; private set; }
    public int Revision { get; private set; }
    public void Activate(string groupId, Guid actorId, DateTime utc)
    {
        groupId = Guard.Text(groupId, 200, nameof(groupId));
        if (!groupId.EndsWith("@g.us", StringComparison.Ordinal) || utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Gruppo o data non validi.");
        actorId = Guard.Id(actorId, nameof(actorId)); var next = checked(Revision + 1);
        GroupId = groupId; ActorId = actorId; ActivatedAtUtc = utc; Enabled = true; Revision = next;
    }
    public bool Disable()
    { if (!Enabled) return false; var next = checked(Revision + 1); Enabled = false; Revision = next; return true; }
}

public sealed class WhatsAppIdentityAlias : ILocalRecord
{
    private WhatsAppIdentityAlias() { }
    public WhatsAppIdentityAlias(Guid eventId, string senderHash, Guid personId)
    { EventId = Guard.Id(eventId, nameof(eventId)); SenderHash = Fingerprint(senderHash); PersonId = Guard.Id(personId, nameof(personId)); }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public string SenderHash { get; private set; } = null!;
    public Guid PersonId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int Revision { get; private set; }
    public bool SetActive(bool active)
    { if (IsActive == active) return false; var next = checked(Revision + 1); IsActive = active; Revision = next; return true; }
    internal static string Fingerprint(string value) => value.Length == 64 && value.All(Uri.IsHexDigit)
        ? value.ToLowerInvariant() : throw new ArgumentException("Fingerprint non valido.");
}

public sealed class WhatsAppIncomingReceipt : ILocalRecord
{
    private WhatsAppIncomingReceipt() { }
    public WhatsAppIncomingReceipt(Guid eventId, string messageHash, Guid personId, Guid actorId,
        ParticipationStatus previous, ParticipationStatus status, DateTime utc)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); MessageHash = WhatsAppIdentityAlias.Fingerprint(messageHash);
        PersonId = Guard.Id(personId, nameof(personId)); ActorId = Guard.Id(actorId, nameof(actorId));
        if (!Enum.IsDefined(previous) || !Enum.IsDefined(status) || utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Ricevuta non valida.");
        PreviousStatus = previous; Status = status; AtUtc = utc;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public string MessageHash { get; private set; } = null!;
    public Guid PersonId { get; private set; }
    public Guid ActorId { get; private set; }
    public ParticipationStatus PreviousStatus { get; private set; }
    public ParticipationStatus Status { get; private set; }
    public DateTime AtUtc { get; private set; }
    public int Revision { get; private set; }
}

public enum WhatsAppLiveMessageStatus { Draft, Sending, Sent, Uncertain }

public sealed class WhatsAppLiveMessage : ILocalRecord
{
    private WhatsAppLiveMessage() { }
    public WhatsAppLiveMessage(Guid eventId, Guid id, Guid actorId, string groupId, int connectionRevision, string body, DateTime utc)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); Id = Guard.Id(id, nameof(id)); ActorId = Guard.Id(actorId, nameof(actorId));
        GroupId = Guard.Text(groupId, 200, nameof(groupId)); Body = Guard.Text(body.ReplaceLineEndings("\n"), 1000, nameof(body));
        if (!GroupId.EndsWith("@g.us", StringComparison.Ordinal) || connectionRevision < 0 || utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Bozza non valida.");
        ConnectionRevision = connectionRevision; CreatedAtUtc = utc;
    }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid ActorId { get; private set; }
    public string GroupId { get; private set; } = null!;
    public string Body { get; private set; } = null!;
    public int ConnectionRevision { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? OutcomeAtUtc { get; private set; }
    public WhatsAppLiveMessageStatus Status { get; private set; }
    public int Revision { get; private set; }
    public void ClaimSending()
    { if (Status != WhatsAppLiveMessageStatus.Draft) throw new InvalidOperationException("Invio già avviato: nessun reinvio automatico consentito."); Status = WhatsAppLiveMessageStatus.Sending; Revision = checked(Revision + 1); }
    public void Complete(bool sent, DateTime utc)
    {
        if (Status != WhatsAppLiveMessageStatus.Sending || utc.Kind != DateTimeKind.Utc) throw new InvalidOperationException("Esito non applicabile.");
        Status = sent ? WhatsAppLiveMessageStatus.Sent : WhatsAppLiveMessageStatus.Uncertain; OutcomeAtUtc = utc; Revision = checked(Revision + 1);
    }
}
