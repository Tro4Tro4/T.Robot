namespace TRobot.Domain;

public enum ParticipationStatus { NoResponse, Confirmed, Undecided, Absent }

public sealed class EventParticipant
{
    private EventParticipant() { }

    public EventParticipant(Guid eventId, Guid personId)
    {
        EventId = Guard.Id(eventId, nameof(eventId));
        PersonId = Guard.Id(personId, nameof(personId));
    }

    public Guid EventId { get; private set; }
    public Guid PersonId { get; private set; }
    public ParticipationStatus Status { get; private set; } = ParticipationStatus.NoResponse;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; private set; }

    public bool ChangeStatus(ParticipationStatus status, DateTime changedAtUtc)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (changedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("La data deve essere UTC.", nameof(changedAtUtc));
        if (Status == status) return false;
        Status = status;
        UpdatedAtUtc = changedAtUtc;
        return true;
    }
}
