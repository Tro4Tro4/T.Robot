namespace TRobot.Domain;

public enum MessageKind { Operational, Summary, Entertainment }
public enum OutboundMessageStatus { Draft, Queued, Sent, Cancelled }

public sealed class OutboundMessage : ILocalRecord
{
    private OutboundMessage() { }
    public OutboundMessage(Guid eventId, Guid id, string body, MessageKind kind, DateTime scheduledAtUtc)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); Id = Guard.Id(id, nameof(id));
        Set(body, kind, scheduledAtUtc);
    }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public int Revision { get; private set; }
    public string Body { get; private set; } = null!;
    public MessageKind Kind { get; private set; }
    public OutboundMessageStatus Status { get; private set; }
    public DateTime ScheduledAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public string? SummaryFingerprint { get; private set; }
    private void Set(string body, MessageKind kind, DateTime scheduledAtUtc)
    {
        var text = Guard.Text(body?.ReplaceLineEndings("\n")!, 4000, nameof(body));
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Tipo messaggio non valido.");
        if (scheduledAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Data di invio deve essere UTC.");
        Body = text; Kind = kind; ScheduledAtUtc = scheduledAtUtc;
    }
    public bool Edit(string body, MessageKind kind, DateTime scheduledAtUtc)
    {
        if (Status != OutboundMessageStatus.Draft) throw new InvalidOperationException("Solo le bozze sono modificabili.");
        var candidate = new OutboundMessage(EventId, Id, body, kind, scheduledAtUtc);
        if (SummaryFingerprint is not null && (candidate.Body != Body || kind != Kind || scheduledAtUtc != ScheduledAtUtc))
            throw new InvalidOperationException("Il riepilogo aggregato generato non può essere modificato.");
        if (Body == candidate.Body && Kind == kind && ScheduledAtUtc == scheduledAtUtc) return false;
        Set(candidate.Body, kind, scheduledAtUtc); Revision = checked(Revision + 1); return true;
    }
    public bool Queue(bool confirmed)
    {
        if (!confirmed) throw new ArgumentException("Conferma l'anteprima prima di accodare.");
        if (Status == OutboundMessageStatus.Queued) return false;
        if (Status != OutboundMessageStatus.Draft) throw new InvalidOperationException("Messaggio non accodabile.");
        Status = OutboundMessageStatus.Queued; Revision = checked(Revision + 1); return true;
    }
    public void SetSummaryFingerprint(string value)
    {
        if (Status != OutboundMessageStatus.Draft || Kind != MessageKind.Summary || SummaryFingerprint is not null)
            throw new InvalidOperationException("Riepilogo non modificabile.");
        SummaryFingerprint = Guard.Text(value, 200, nameof(value));
    }
    public bool MarkSent(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Data esito deve essere UTC.");
        if (Status == OutboundMessageStatus.Sent) return false;
        if (Status != OutboundMessageStatus.Queued || utc < ScheduledAtUtc) throw new InvalidOperationException("Messaggio non pronto.");
        Status = OutboundMessageStatus.Sent; SentAtUtc = utc; Revision = checked(Revision + 1); return true;
    }
    public bool Cancel(bool confirmed)
    {
        if (!confirmed) throw new ArgumentException("Conferma l'annullamento.");
        if (Status == OutboundMessageStatus.Cancelled) return false;
        if (Status == OutboundMessageStatus.Sent) throw new InvalidOperationException("Un invio concluso resta nello storico.");
        Status = OutboundMessageStatus.Cancelled; Revision = checked(Revision + 1); return true;
    }
}

public sealed class BotSettings : ILocalRecord
{
    private BotSettings() { }
    public BotSettings(Guid eventId) { Id = EventId = Guard.Id(eventId, nameof(eventId)); }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public int Revision { get; private set; }
    public bool Paused { get; private set; }
    public int QuietStartHour { get; private set; } = 23;
    public int QuietEndHour { get; private set; } = 9;
    public int MaxSpontaneousPerDay { get; private set; } = 3;
    public int Sarcasm { get; private set; } = 5;
    public bool AutomaticEnabled { get; private set; }
    public Guid? AutomaticActorId { get; private set; }
    public DateTime? LastSummaryAtUtc { get; private set; }
    public string? LastSummaryFingerprint { get; private set; }
    public bool SetAutomatic(bool enabled, Guid actorId, bool confirmed)
    {
        if (!confirmed) throw new ArgumentException("Conferma la modifica dello scheduler automatico locale.");
        Guard.Id(actorId, nameof(actorId));
        if (AutomaticEnabled == enabled && (!enabled || AutomaticActorId == actorId)) return false;
        AutomaticEnabled = enabled;
        if (enabled) AutomaticActorId = actorId;
        Revision = checked(Revision + 1);
        return true;
    }
    public bool Update(bool paused, int quietStart, int quietEnd, int cap, int sarcasm)
    {
        if (quietStart is < 0 or > 23 || quietEnd is < 0 or > 23 || cap is < 0 or > 3 || sarcasm is < 0 or > 10)
            throw new ArgumentException("Orari 0–23, limite 0–3 e sarcasmo 0–10.");
        if (Paused == paused && QuietStartHour == quietStart && QuietEndHour == quietEnd && MaxSpontaneousPerDay == cap && Sarcasm == sarcasm) return false;
        Paused = paused; QuietStartHour = quietStart; QuietEndHour = quietEnd; MaxSpontaneousPerDay = cap; Sarcasm = sarcasm;
        Revision = checked(Revision + 1); return true;
    }
    public void RecordSummary(DateTime utc, string fingerprint)
    {
        if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Data riepilogo deve essere UTC.");
        var valid = Guard.Text(fingerprint, 200, nameof(fingerprint));
        LastSummaryAtUtc = utc; LastSummaryFingerprint = valid; Revision = checked(Revision + 1);
    }
}
