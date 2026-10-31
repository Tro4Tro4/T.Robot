namespace TRobot.Domain;

public sealed class LodgingPlan : ILocalRecord
{
    private LodgingPlan() { }
    public LodgingPlan(Guid eventId) { Id = EventId = Guard.Id(eventId, nameof(eventId)); }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public int Revision { get; private set; }
    public string? FinalizedFingerprint { get; private set; }
    public void Change() { Revision = checked(Revision + 1); FinalizedFingerprint = null; }
    public void FinalizePlan(string fingerprint) { Change(); FinalizedFingerprint = Guard.Text(fingerprint, 64, nameof(fingerprint)); }
}

public sealed class LodgingApartment : ILocalRecord
{
    private LodgingApartment() { }
    public LodgingApartment(Guid id, Guid eventId, string name, string? directions)
    { Id = Guard.Id(id, nameof(id)); EventId = Guard.Id(eventId, nameof(eventId)); Update(name, directions); }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Directions { get; private set; }
    public int Revision { get; private set; }
    public void Update(string name, string? directions)
    {
        var title = Guard.Text(name, 150, nameof(name)); var notes = Guard.OptionalText(directions, 1000, nameof(directions));
        Name = title; Directions = notes; Revision = checked(Revision + 1);
    }
}

public sealed class LodgingRoom : ILocalRecord
{
    private LodgingRoom() { }
    public LodgingRoom(Guid id, Guid eventId, Guid apartmentId, string name, int capacity, string? notes)
    { Id = Guard.Id(id, nameof(id)); EventId = Guard.Id(eventId, nameof(eventId)); ApartmentId = Guard.Id(apartmentId, nameof(apartmentId)); Update(name, capacity, notes); }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid ApartmentId { get; private set; }
    public string Name { get; private set; } = null!;
    public int Capacity { get; private set; }
    public string? Notes { get; private set; }
    public int Revision { get; private set; }
    public void Update(string name, int capacity, string? notes)
    {
        var title = Guard.Text(name, 150, nameof(name)); var text = Guard.OptionalText(notes, 1000, nameof(notes));
        if (capacity is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(capacity), "Capienza ammessa da 1 a 1000.");
        Name = title; Capacity = capacity; Notes = text; Revision = checked(Revision + 1);
    }
}

public sealed class LodgingAssignment : ILocalRecord
{
    private LodgingAssignment() { }
    public LodgingAssignment(Guid eventId, Guid personId, Guid roomId)
    { Id = Guid.NewGuid(); EventId = Guard.Id(eventId, nameof(eventId)); PersonId = Guard.Id(personId, nameof(personId)); SetRoom(roomId); }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid PersonId { get; private set; }
    public Guid? RoomId { get; private set; }
    public int Revision { get; private set; }
    public void SetRoom(Guid? roomId)
    {
        if (roomId == Guid.Empty) throw new ArgumentException("Camera non valida.");
        RoomId = roomId; Revision = checked(Revision + 1);
    }
}
