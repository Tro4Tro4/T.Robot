namespace TRobot.Domain;

// Solo metadati del comando elaborato: non conserviamo il testo del messaggio.
public sealed class ParticipationReceipt
{
    private ParticipationReceipt() { }

    public ParticipationReceipt(Guid eventId, Guid personId, Guid organizerPersonId,
        string messageId, ParticipationStatus previousStatus, ParticipationStatus status, DateTime processedAtUtc)
    {
        EventId = Guard.Id(eventId, nameof(eventId));
        PersonId = Guard.Id(personId, nameof(personId));
        OrganizerPersonId = Guard.Id(organizerPersonId, nameof(organizerPersonId));
        MessageId = Guard.Text(messageId, 100, nameof(messageId));
        if (!Enum.IsDefined(previousStatus) || !Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (processedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("La data deve essere UTC.", nameof(processedAtUtc));
        PreviousStatus = previousStatus;
        Status = status;
        ProcessedAtUtc = processedAtUtc;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public Guid PersonId { get; private set; }
    public Guid OrganizerPersonId { get; private set; }
    public string MessageId { get; private set; } = null!;
    public ParticipationStatus PreviousStatus { get; private set; }
    public ParticipationStatus Status { get; private set; }
    public DateTime ProcessedAtUtc { get; private set; }
}
