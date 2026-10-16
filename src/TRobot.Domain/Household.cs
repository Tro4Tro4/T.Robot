namespace TRobot.Domain;

public enum HouseholdMemberKind { Adult, Child }

public sealed class Household : ILocalRecord
{
    private Household() { }
    public Household(Guid eventId, string name, Guid id)
    { Id = Guard.Id(id, nameof(id)); EventId = Guard.Id(eventId, nameof(eventId)); Name = Guard.Text(name, 150, nameof(name)); }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string Name { get; private set; } = null!;
    public int Revision { get; private set; }
    public void Touch() => Revision = checked(Revision + 1);
}

public sealed class HouseholdMember : ILocalRecord
{
    private HouseholdMember() { }
    public HouseholdMember(Guid eventId, Guid householdId, Guid personId, HouseholdMemberKind kind, Guid? companionOfMemberId, Guid id)
    {
        Id = Guard.Id(id, nameof(id)); EventId = Guard.Id(eventId, nameof(eventId));
        HouseholdId = Guard.Id(householdId, nameof(householdId)); PersonId = Guard.Id(personId, nameof(personId));
        Validate(kind, companionOfMemberId); Kind = kind; CompanionOfMemberId = companionOfMemberId;
    }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid HouseholdId { get; private set; }
    public Guid PersonId { get; private set; }
    public HouseholdMemberKind Kind { get; private set; }
    public Guid? CompanionOfMemberId { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int Revision { get; private set; }
    private void Validate(HouseholdMemberKind kind, Guid? companionOfMemberId)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("Classificazione non valida.");
        if (companionOfMemberId == Guid.Empty || companionOfMemberId == Id)
            throw new ArgumentException("Accompagnatore di riferimento non valido.");
    }
    public bool Update(HouseholdMemberKind kind, Guid? companionOfMemberId, bool active)
    {
        Validate(kind, companionOfMemberId);
        if (Kind == kind && CompanionOfMemberId == companionOfMemberId && IsActive == active) return false;
        var revision = checked(Revision + 1);
        Kind = kind; CompanionOfMemberId = companionOfMemberId; IsActive = active; Revision = revision;
        return true;
    }
}
