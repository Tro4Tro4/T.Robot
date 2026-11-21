namespace TRobot.Domain;

public enum MessageKind { Operational, Summary, Entertainment }
public enum OutboundMessageStatus { Draft, Queued, Sent, Cancelled, Sending, Failed, Uncertain }

/// <summary>
/// Simulated rows keep the historical behaviour: a local tick records "Sent" without any transport.
/// WhatsApp rows are only created from the organizer panel and are sent by the dispatcher through the gateway.
/// </summary>
public enum MessageDeliveryMode { Simulated, WhatsApp }

public sealed class OutboundMessage : ILocalRecord
{
    private OutboundMessage() { }
    public OutboundMessage(Guid eventId, Guid id, string body, MessageKind kind, DateTime scheduledAtUtc,
        MessageDeliveryMode deliveryMode = MessageDeliveryMode.Simulated, DateTime? createdAtUtc = null)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); Id = Guard.Id(id, nameof(id));
        if (!Enum.IsDefined(deliveryMode)) throw new ArgumentException("Modalità di consegna non valida.");
        DeliveryMode = deliveryMode;
        Set(body, kind, scheduledAtUtc);
        var created = createdAtUtc ?? scheduledAtUtc;
        if (created.Kind != DateTimeKind.Utc) throw new ArgumentException("Data di creazione deve essere UTC.");
        CreatedAtUtc = created; UpdatedAtUtc = created;
    }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public int Revision { get; private set; }
    public string Body { get; private set; } = null!;
    public MessageKind Kind { get; private set; }
    public OutboundMessageStatus Status { get; private set; }
    public MessageDeliveryMode DeliveryMode { get; private set; }
    public DateTime ScheduledAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public string? SummaryFingerprint { get; private set; }
    public DateTime? ConfirmedAtUtc { get; private set; }
    public Guid? ConfirmedByActorId { get; private set; }
    public string? TargetGroupId { get; private set; }
    public int Attempts { get; private set; }
    public DateTime? LastAttemptAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public string? OutcomeNote { get; private set; }
    private const int MaxBodyLength = 4000;
    // The gateway /send route accepts at most 1000 units for a manual group message.
    private const int MaxWhatsAppBodyLength = 1000;
    private void Set(string body, MessageKind kind, DateTime scheduledAtUtc)
    {
        var text = Guard.Text(body?.ReplaceLineEndings("\n")!, MaxBodyLength, nameof(body));
        if (text.Length > MaxWhatsAppBodyLength && DeliveryMode == MessageDeliveryMode.WhatsApp)
            throw new ArgumentException("Un messaggio WhatsApp non può superare 1000 caratteri.");
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Tipo messaggio non valido.");
        if (scheduledAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Data di invio deve essere UTC.");
        Body = text; Kind = kind; ScheduledAtUtc = scheduledAtUtc;
    }
    public bool Edit(string body, MessageKind kind, DateTime scheduledAtUtc, DateTime? atUtc = null)
    {
        if (Status != OutboundMessageStatus.Draft) throw new InvalidOperationException("Solo le bozze sono modificabili.");
        // Validate a detached copy first so a failed edit leaves the stored draft untouched.
        var candidate = new OutboundMessage(EventId, Id, body, kind, scheduledAtUtc, DeliveryMode);
        if (SummaryFingerprint is not null && (candidate.Body != Body || kind != Kind || scheduledAtUtc != ScheduledAtUtc))
            throw new InvalidOperationException("Il riepilogo aggregato generato non può essere modificato.");
        if (Body == candidate.Body && Kind == kind && ScheduledAtUtc == scheduledAtUtc) return false;
        Set(candidate.Body, kind, scheduledAtUtc); Touch(atUtc ?? UpdatedAtUtc); return true;
    }
    /// <summary>Simulated queue used by summaries, polls and quizzes. It never authorizes a real send.</summary>
    public bool Queue(bool confirmed, DateTime? atUtc = null)
    {
        if (!confirmed) throw new ArgumentException("Conferma l'anteprima prima di accodare.");
        if (DeliveryMode != MessageDeliveryMode.Simulated)
            throw new InvalidOperationException("Un messaggio WhatsApp richiede la conferma dell'organizzatore e del gruppo.");
        if (Status == OutboundMessageStatus.Queued) return false;
        if (Status != OutboundMessageStatus.Draft) throw new InvalidOperationException("Messaggio non accodabile.");
        Status = OutboundMessageStatus.Queued; Touch(atUtc ?? UpdatedAtUtc); return true;
    }
    /// <summary>Real authorization: records who confirmed, when, and the exact group shown in the preview.</summary>
    public bool QueueForWhatsApp(Guid actorId, string targetGroupId, DateTime utc, bool confirmed)
    {
        if (!confirmed) throw new ArgumentException("Conferma esplicitamente l'invio WhatsApp.");
        actorId = Guard.Id(actorId, nameof(actorId));
        targetGroupId = Guard.Text(targetGroupId, 200, nameof(targetGroupId));
        if (!targetGroupId.EndsWith("@g.us", StringComparison.Ordinal)) throw new ArgumentException("Gruppo WhatsApp non valido.");
        if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Data di conferma deve essere UTC.");
        if (Status == OutboundMessageStatus.Queued && DeliveryMode == MessageDeliveryMode.WhatsApp) return false;
        if (Status != OutboundMessageStatus.Draft) throw new InvalidOperationException("Messaggio non accodabile.");
        // A draft created before this milestone (simulated mode) is promoted only here, with explicit confirmation.
        DeliveryMode = MessageDeliveryMode.WhatsApp;
        if (Body.Length > MaxWhatsAppBodyLength)
            throw new InvalidOperationException("Un messaggio WhatsApp non può superare 1000 caratteri.");
        Status = OutboundMessageStatus.Queued;
        ConfirmedAtUtc = utc; ConfirmedByActorId = actorId; TargetGroupId = targetGroupId;
        LastError = null; Touch(utc); return true;
    }
    public void SetSummaryFingerprint(string value)
    {
        if (Status != OutboundMessageStatus.Draft || Kind != MessageKind.Summary || SummaryFingerprint is not null)
            throw new InvalidOperationException("Riepilogo non modificabile.");
        SummaryFingerprint = Guard.Text(value, 200, nameof(value));
    }
    /// <summary>Simulation only. A WhatsApp message can never be marked as sent without a gateway outcome.</summary>
    public bool MarkSent(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Data esito deve essere UTC.");
        if (DeliveryMode != MessageDeliveryMode.Simulated)
            throw new InvalidOperationException("Un messaggio WhatsApp non può essere inviato dalla simulazione.");
        if (Status == OutboundMessageStatus.Sent) return false;
        if (Status != OutboundMessageStatus.Queued || utc < ScheduledAtUtc) throw new InvalidOperationException("Messaggio non pronto.");
        Status = OutboundMessageStatus.Sent; SentAtUtc = utc; UpdatedAtUtc = utc; Revision = checked(Revision + 1); return true;
    }
    /// <summary>Persisted before the HTTP call so a crash leaves a visible claim instead of a duplicate send.</summary>
    public bool ClaimSending(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Data tentativo deve essere UTC.");
        if (DeliveryMode != MessageDeliveryMode.WhatsApp || Status != OutboundMessageStatus.Queued)
            throw new InvalidOperationException("Messaggio non inviabile.");
        if (utc < ScheduledAtUtc) throw new InvalidOperationException("Messaggio non ancora dovuto.");
        Status = OutboundMessageStatus.Sending;
        Attempts = checked(Attempts + 1); LastAttemptAtUtc = utc; UpdatedAtUtc = utc;
        Revision = checked(Revision + 1); return true;
    }
    public bool ConfirmSent(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Data esito deve essere UTC.");
        if (Status == OutboundMessageStatus.Sent) return false;
        if (Status != OutboundMessageStatus.Sending) throw new InvalidOperationException("Nessun invio in corso.");
        Status = OutboundMessageStatus.Sent; SentAtUtc = utc; LastError = null; UpdatedAtUtc = utc;
        Revision = checked(Revision + 1); return true;
    }
    /// <summary>The request may have reached WhatsApp: never retried automatically.</summary>
    public bool ConfirmUncertain(string reason, DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Data esito deve essere UTC.");
        if (Status != OutboundMessageStatus.Sending) throw new InvalidOperationException("Nessun invio in corso.");
        Status = OutboundMessageStatus.Uncertain; LastError = SafeReason(reason); UpdatedAtUtc = utc;
        Revision = checked(Revision + 1); return true;
    }
    /// <summary>Known failure: the gateway refused the request before transmitting it.</summary>
    public bool Fail(string reason, DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Data esito deve essere UTC.");
        if (Status != OutboundMessageStatus.Sending) throw new InvalidOperationException("Nessun invio in corso.");
        Status = OutboundMessageStatus.Failed; LastError = SafeReason(reason); UpdatedAtUtc = utc;
        Revision = checked(Revision + 1); return true;
    }
    /// <summary>Controlled human resolution of an ambiguous outcome. It never triggers a new send.</summary>
    public bool Resolve(bool asSent, Guid actorId, string note, DateTime utc)
    {
        actorId = Guard.Id(actorId, nameof(actorId));
        if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Data risoluzione deve essere UTC.");
        if (Status is not (OutboundMessageStatus.Uncertain or OutboundMessageStatus.Failed))
            throw new InvalidOperationException("Solo un esito incerto o fallito può essere risolto.");
        Status = asSent ? OutboundMessageStatus.Sent : OutboundMessageStatus.Failed;
        if (asSent) { SentAtUtc = utc; LastError = null; }
        OutcomeNote = SafeReason(note); UpdatedAtUtc = utc; Revision = checked(Revision + 1); return true;
    }
    public bool Cancel(bool confirmed)
    {
        if (!confirmed) throw new ArgumentException("Conferma l'annullamento.");
        if (Status == OutboundMessageStatus.Cancelled) return false;
        if (Status is OutboundMessageStatus.Sent or OutboundMessageStatus.Sending)
            throw new InvalidOperationException("Un invio avviato o concluso non può essere annullato.");
        Status = OutboundMessageStatus.Cancelled; Touch(UpdatedAtUtc); return true;
    }
    private static string SafeReason(string value)
    {
        var text = (value ?? "").ReplaceLineEndings(" ").Trim();
        if (text.Length == 0) throw new ArgumentException("Motivo obbligatorio.");
        return text.Length <= 500 ? text : text[..500];
    }
    private void Touch(DateTime utc)
    {
        if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Data di modifica deve essere UTC.");
        UpdatedAtUtc = utc; Revision = checked(Revision + 1);
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
