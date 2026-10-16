namespace TRobot.Domain;

public enum OrganizerRole { Organizer, Administrator }

public sealed class EventOrganizer
{
    private EventOrganizer() { }

    public EventOrganizer(Guid eventId, Guid personId, OrganizerRole role = OrganizerRole.Organizer)
    {
        EventId = Guard.Id(eventId, nameof(eventId));
        PersonId = Guard.Id(personId, nameof(personId));
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        Role = role;
    }

    public Guid EventId { get; private set; }
    public Guid PersonId { get; private set; }
    public OrganizerRole Role { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    public bool SetActive(bool active)
    {
        if (IsActive == active) return false;
        IsActive = active;
        return true;
    }
}
