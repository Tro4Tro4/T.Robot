using System.Text.Json;

namespace TRobot.Domain;

public enum MealVotingState { Collection, Preparation, Open, Closed }

public sealed class MealVotingCycle : ILocalRecord
{
    private MealVotingCycle() { }
    public MealVotingCycle(Guid eventId, Guid id, DateTime utc, int number = 1)
    { EventId = Guard.Id(eventId, nameof(eventId)); Id = Guard.Id(id, nameof(id)); RequireUtc(utc); if (number < 1) throw new ArgumentException("Ciclo non valido."); CreatedAtUtc = utc; Number = number; }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public MealVotingState State { get; private set; }
    public int Number { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? OpenedAtUtc { get; private set; }
    public DateTime? ClosedAtUtc { get; private set; }
    public string? ClosedSnapshotJson { get; private set; }
    public int Revision { get; private set; }
    public void CloseCollection() { Require(MealVotingState.Collection); State = MealVotingState.Preparation; Touch(); }
    public void Open(DateTime utc) { Require(MealVotingState.Preparation); RequireUtc(utc); State = MealVotingState.Open; OpenedAtUtc = utc; Touch(); }
    public void Close(string snapshotJson, DateTime utc)
    { Require(MealVotingState.Open); RequireUtc(utc); ClosedSnapshotJson = Guard.Text(snapshotJson, 5_000_000, nameof(snapshotJson)); State = MealVotingState.Closed; ClosedAtUtc = utc; Touch(); }
    public void Touch() => Revision = checked(Revision + 1);
    public void Require(MealVotingState state) { if (State != state) throw new InvalidOperationException("Fase pasti non valida per questa operazione."); }
    private static void RequireUtc(DateTime utc) { if (utc.Kind != DateTimeKind.Utc) throw new ArgumentException("La data deve essere UTC."); }
}

public sealed class MealVotingEntry : ILocalRecord
{
    private MealVotingEntry() { }
    public MealVotingEntry(Guid eventId, Guid cycleId, Guid id, DateOnly day, MealKind meal, string name, bool excluded)
    { EventId = Guard.Id(eventId, nameof(eventId)); CycleId = Guard.Id(cycleId, nameof(cycleId)); Id = Guard.Id(id, nameof(id));
        if (day == default || !Enum.IsDefined(meal)) throw new ArgumentException("Pasto non valido.");
        Day = day; Meal = meal; Name = MealProposal.ValidateDish(name); IsExcluded = excluded; }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid CycleId { get; private set; }
    public DateOnly Day { get; private set; }
    public MealKind Meal { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsExcluded { get; private set; }
    public int Revision { get; private set; }
    public void Update(string name, bool excluded) { Name = MealProposal.ValidateDish(name); IsExcluded = excluded; Revision = checked(Revision + 1); }
}

public sealed class MealVotingSource : ILocalRecord
{
    private MealVotingSource() { }
    public MealVotingSource(Guid eventId, Guid cycleId, Guid entryId, Guid proposalId)
    { EventId = eventId; CycleId = cycleId; EntryId = entryId; ProposalId = proposalId; }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public Guid CycleId { get; private set; }
    public Guid EntryId { get; private set; }
    public Guid ProposalId { get; private set; }
    public int Revision { get; private set; }
    public void MoveTo(Guid entryId) { EntryId = Guard.Id(entryId, nameof(entryId)); Revision = checked(Revision + 1); }
}

public sealed class MealPreferenceBallot : ILocalRecord
{
    private MealPreferenceBallot() { }
    public MealPreferenceBallot(Guid eventId, Guid cycleId, Guid personId, DateOnly day, MealKind meal)
    { EventId = eventId; CycleId = cycleId; PersonId = personId; Day = day; Meal = meal; }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public Guid CycleId { get; private set; }
    public Guid PersonId { get; private set; }
    public DateOnly Day { get; private set; }
    public MealKind Meal { get; private set; }
    public string SelectionJson { get; private set; } = "[]";
    public int Revision { get; private set; }
    public IReadOnlyList<Guid> Selections() => JsonSerializer.Deserialize<Guid[]>(SelectionJson)!;
    public void Replace(IEnumerable<Guid> ids) { SelectionJson = JsonSerializer.Serialize(ids.Distinct().Order().ToArray()); Revision = checked(Revision + 1); }
}

/// <summary>Persistent operation receipt and audit, retaining previous and replacement selections.</summary>
public sealed class MealPreferenceChange : ILocalRecord
{
    private MealPreferenceChange() { }
    public MealPreferenceChange(Guid eventId, Guid operationId, Guid cycleId, Guid voterId, Guid personId, Guid actorId,
        DateOnly day, MealKind meal, string payloadFingerprint, string previousJson, string selectionJson, int ballotRevision, DateTime utc)
    { EventId = eventId; Id = operationId; CycleId = cycleId; VoterId = voterId; PersonId = personId; ActorId = actorId; Day = day;
        Meal = meal; PayloadFingerprint = payloadFingerprint; PreviousJson = previousJson; SelectionJson = selectionJson; BallotRevision = ballotRevision; AtUtc = utc; }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid CycleId { get; private set; }
    public Guid VoterId { get; private set; }
    public Guid PersonId { get; private set; }
    public Guid ActorId { get; private set; }
    public DateOnly Day { get; private set; }
    public MealKind Meal { get; private set; }
    public string PayloadFingerprint { get; private set; } = null!;
    public string PreviousJson { get; private set; } = null!;
    public string SelectionJson { get; private set; } = null!;
    public int BallotRevision { get; private set; }
    public DateTime AtUtc { get; private set; }
    public int Revision { get; private set; }
}
