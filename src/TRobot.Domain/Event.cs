namespace TRobot.Domain;

public sealed class Event
{
    private Event() { }

    public Event(string name, DateTime startsAtUtc, string timeZoneId = "Europe/Rome", string? location = null, string? program = null)
    {
        SetDetails(name, startsAtUtc, timeZoneId, location, program);
    }

    private void SetDetails(string name, DateTime startsAtUtc, string timeZoneId, string? location, string? program)
    {
        var validName = Guard.Text(name, 200, nameof(name));
        if (startsAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("La data deve essere UTC.", nameof(startsAtUtc));
        var validZone = Guard.Text(timeZoneId, 100, nameof(timeZoneId));
        _ = TimeZoneInfo.FindSystemTimeZoneById(validZone);
        var validLocation = Guard.OptionalText(location, 300, nameof(location));
        var validProgram = Guard.OptionalText(program?.ReplaceLineEndings("\n"), 4000, nameof(program));
        Name = validName;
        TimeZoneId = validZone;
        StartsAtUtc = startsAtUtc;
        Location = validLocation;
        Program = validProgram;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Name { get; private set; } = null!;
    public DateTime StartsAtUtc { get; private set; }
    public string TimeZoneId { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public int Revision { get; private set; }
    public string? Location { get; private set; }
    public string? Program { get; private set; }

    public bool UpdateDetails(string name, DateTime startsAtUtc, string timeZoneId, string? location = null, string? program = null)
    {
        // Validate before changing any field; failed edits leave the entity unchanged.
        var validated = new Event(name, startsAtUtc, timeZoneId, location, program);
        if (Name == validated.Name && StartsAtUtc == validated.StartsAtUtc && TimeZoneId == validated.TimeZoneId
            && Location == validated.Location && Program == validated.Program)
            return false;
        var nextRevision = checked(Revision + 1);
        SetDetails(validated.Name, validated.StartsAtUtc, validated.TimeZoneId, validated.Location, validated.Program);
        Revision = nextRevision;
        return true;
    }
}
