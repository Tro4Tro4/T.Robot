namespace TRobot.Domain;

public sealed class Event
{
    private Event() { }

    public Event(string name, DateTime startsAtUtc, string timeZoneId = "Europe/Rome")
    {
        SetDetails(name, startsAtUtc, timeZoneId);
    }

    private void SetDetails(string name, DateTime startsAtUtc, string timeZoneId)
    {
        var validName = Guard.Text(name, 200, nameof(name));
        if (startsAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("La data deve essere UTC.", nameof(startsAtUtc));
        var validZone = Guard.Text(timeZoneId, 100, nameof(timeZoneId));
        _ = TimeZoneInfo.FindSystemTimeZoneById(validZone);
        Name = validName;
        TimeZoneId = validZone;
        StartsAtUtc = startsAtUtc;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = null!;
    public DateTime StartsAtUtc { get; private set; }
    public string TimeZoneId { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public int Revision { get; private set; }

    public bool UpdateDetails(string name, DateTime startsAtUtc, string timeZoneId)
    {
        // Validate before changing any field; failed edits leave the entity unchanged.
        var validated = new Event(name, startsAtUtc, timeZoneId);
        if (Name == validated.Name && StartsAtUtc == validated.StartsAtUtc && TimeZoneId == validated.TimeZoneId)
            return false;
        var nextRevision = checked(Revision + 1);
        SetDetails(validated.Name, validated.StartsAtUtc, validated.TimeZoneId);
        Revision = nextRevision;
        return true;
    }
}
