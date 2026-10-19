namespace TRobot.Domain;

public sealed class AttendanceCollectionSettings : ILocalRecord
{
    private AttendanceCollectionSettings() { }
    public AttendanceCollectionSettings(Guid eventId)
    { Id = EventId = Guard.Id(eventId, nameof(eventId)); }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public DateTime? DeadlineUtc { get; private set; }
    public int Revision { get; private set; }

    public bool SetDeadline(DateTime? deadlineUtc)
    {
        if (deadlineUtc.HasValue && deadlineUtc.Value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("La scadenza deve essere UTC.");
        if (DeadlineUtc == deadlineUtc) return false;
        DeadlineUtc = deadlineUtc; Revision++; return true;
    }
}
