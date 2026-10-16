namespace TRobot.Domain;

public sealed class Event
{
    private Event() { }

    public Event(string name, DateTime startsAtUtc, string timeZoneId = "Europe/Rome")
    {
        Name = Guard.Text(name, 200, nameof(name));
        if (startsAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("La data deve essere UTC.", nameof(startsAtUtc));
        TimeZoneId = Guard.Text(timeZoneId, 100, nameof(timeZoneId));
        _ = TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        StartsAtUtc = startsAtUtc;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = null!;
    public DateTime StartsAtUtc { get; private set; }
    public string TimeZoneId { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
}
