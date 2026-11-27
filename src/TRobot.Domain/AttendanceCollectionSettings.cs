namespace TRobot.Domain;

public sealed class AttendanceCollectionSettings : ILocalRecord
{
    private AttendanceCollectionSettings() { }
    public AttendanceCollectionSettings(Guid eventId)
    { Id = EventId = Guard.Id(eventId, nameof(eventId)); }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public DateTime? DeadlineUtc { get; private set; }
    /// <summary>Se falso la presenza vale per la data dell'evento: non si chiedono arrivo e durata.</summary>
    public bool AskStayDates { get; private set; } = true;
    /// <summary>Giorni coperti dalla presenza quando non si chiedono le date (da 1 a 31).</summary>
    public int EventDays { get; private set; } = 1;
    public int Revision { get; private set; }

    public bool SetStayMode(bool askStayDates, int eventDays)
    {
        if (eventDays is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(eventDays));
        if (AskStayDates == askStayDates && EventDays == eventDays) return false;
        AskStayDates = askStayDates; EventDays = eventDays; Revision++; return true;
    }

    public bool SetDeadline(DateTime? deadlineUtc)
    {
        if (deadlineUtc.HasValue && deadlineUtc.Value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("La scadenza deve essere UTC.");
        if (DeadlineUtc == deadlineUtc) return false;
        DeadlineUtc = deadlineUtc; Revision++; return true;
    }
}
