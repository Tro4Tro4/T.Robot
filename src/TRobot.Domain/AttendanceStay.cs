namespace TRobot.Domain;

public sealed class AttendanceStay : ILocalRecord
{
    private AttendanceStay() { }
    public AttendanceStay(Guid eventId, Guid personId) { EventId = eventId; Id = personId; }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public DateOnly Arrival { get; private set; }
    public int Days { get; private set; }
    public int Revision { get; private set; }
    public DateOnly LastDay => Arrival.AddDays(Days - 1);
    public int Nights => Days - 1;
    public void Set(DateOnly arrival, int days)
    {
        if (days is < 1 or > 31 || arrival == default) throw new ArgumentException("Soggiorno non valido.");
        _ = arrival.AddDays(days - 1);
        Arrival = arrival; Days = days; Revision = checked(Revision + 1);
    }
}

public sealed class AttendanceStayChange : ILocalRecord
{
    private AttendanceStayChange() { }
    public AttendanceStayChange(Guid eventId, Guid personId, Guid senderId, Guid actorId, DateOnly? previousArrival,
        int? previousDays, ParticipationStatus previousStatus, DateOnly arrival, int days, DateTime utc)
    { EventId = eventId; PersonId = personId; SenderId = senderId; ActorId = actorId; PreviousArrival = previousArrival;
        PreviousDays = previousDays; PreviousStatus = previousStatus; Arrival = arrival; Days = days; AtUtc = utc; }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public Guid PersonId { get; private set; }
    public Guid SenderId { get; private set; }
    public Guid ActorId { get; private set; }
    public DateOnly? PreviousArrival { get; private set; }
    public int? PreviousDays { get; private set; }
    public ParticipationStatus PreviousStatus { get; private set; }
    public DateOnly Arrival { get; private set; }
    public int Days { get; private set; }
    public DateTime AtUtc { get; private set; }
    public int Revision { get; private set; }
}

public enum AttendanceDialogueStep { Closed, Arrival, Days, Confirmation, EditChoice, FamilyStatus }

/// <summary>One current technical dialogue per participant; never stores chat text or names.</summary>
public sealed class AttendanceDialogue : ILocalRecord
{
    private AttendanceDialogue() { }
    public AttendanceDialogue(Guid eventId, Guid personId) { EventId = eventId; Id = personId; }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid ActorId { get; private set; }
    public string GroupId { get; private set; } = "";
    public int ConnectionRevision { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public AttendanceDialogueStep Step { get; private set; }
    public DateOnly? Arrival { get; private set; }
    public int? Days { get; private set; }
    public Guid? HouseholdId { get; private set; }
    public string? SnapshotHash { get; private set; }
    public string? InitialSnapshotHash { get; private set; }
    public bool IsEdit { get; private set; }
    public bool PreserveDays { get; private set; }
    /// <summary>Status to extend to the household; set only while Step is FamilyStatus.</summary>
    public ParticipationStatus? FamilyStatus { get; private set; }
    public int Revision { get; private set; }
    public void Start(Guid actor, string group, int connectionRevision, DateTime utc)
    { ActorId = actor; GroupId = group; ConnectionRevision = connectionRevision; StartedAtUtc = utc; ExpiresAtUtc = utc.AddMinutes(15);
        Step = AttendanceDialogueStep.Arrival; Arrival = null; Days = null; HouseholdId = null; SnapshotHash = null;
        InitialSnapshotHash = null; IsEdit = false; PreserveDays = false; FamilyStatus = null; Revision++; }
    public void StartFamilyStatus(Guid actor, string group, int connectionRevision, DateTime utc, ParticipationStatus status, Guid household, string snapshot)
    {
        if (status is not (ParticipationStatus.Undecided or ParticipationStatus.Absent)) throw new ArgumentOutOfRangeException(nameof(status));
        if (household == Guid.Empty || string.IsNullOrWhiteSpace(snapshot)) throw new ArgumentException("Nucleo o snapshot non validi.");
        ActorId = actor; GroupId = group; ConnectionRevision = connectionRevision; StartedAtUtc = utc; ExpiresAtUtc = utc.AddMinutes(15);
        Step = AttendanceDialogueStep.FamilyStatus; FamilyStatus = status; Arrival = null; Days = null; HouseholdId = household;
        SnapshotHash = snapshot; InitialSnapshotHash = null; IsEdit = false; PreserveDays = false; Revision++;
    }
    public void BeginEdit(DateOnly? arrival, int? days, Guid? household, string snapshot)
    { IsEdit = true; Arrival = arrival; Days = days; HouseholdId = household; InitialSnapshotHash = snapshot;
        Step = arrival.HasValue && days.HasValue ? AttendanceDialogueStep.EditChoice : AttendanceDialogueStep.Arrival; Revision++; }
    public void ChooseEdit(int choice)
    {
        if (Step != AttendanceDialogueStep.EditChoice || choice is < 1 or > 3) throw new InvalidOperationException("Scelta modifica non valida.");
        PreserveDays = choice == 1; Step = choice == 2 ? AttendanceDialogueStep.Days : AttendanceDialogueStep.Arrival;
        if (choice != 2) Arrival = null;
        if (choice != 1) Days = null;
        Revision++;
    }
    public void SetArrival(DateOnly arrival) { Arrival = arrival; Step = AttendanceDialogueStep.Days; Revision++; }
    public void Summarize(int days, Guid? household, string snapshot)
    { Days = days; HouseholdId = household; SnapshotHash = snapshot; Step = AttendanceDialogueStep.Confirmation; Revision++; }
    public void Close() { Step = AttendanceDialogueStep.Closed; FamilyStatus = null; Revision++; }
}
