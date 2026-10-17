namespace TRobot.Domain;

public enum SimulationAction { BringMenu, ClaimTask, CompleteTask }

/// <summary>Minimal canonical receipt: no incoming message text is retained.</summary>
public sealed class SimulationActionReceipt : ILocalRecord
{
    private SimulationActionReceipt() { }
    public SimulationActionReceipt(Guid eventId, Guid id, Guid actorId, Guid personId,
        SimulationAction action, Guid targetId, DateTime atUtc)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); Id = Guard.Id(id, nameof(id));
        ActorId = Guard.Id(actorId, nameof(actorId)); PersonId = Guard.Id(personId, nameof(personId));
        TargetId = Guard.Id(targetId, nameof(targetId));
        if (!Enum.IsDefined(action)) throw new ArgumentException("Azione simulata non valida.");
        if (atUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("La data deve essere UTC.");
        Action = action; AtUtc = atUtc;
    }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid PersonId { get; private set; }
    public SimulationAction Action { get; private set; }
    public Guid TargetId { get; private set; }
    public DateTime AtUtc { get; private set; }
    public int Revision { get; private set; }
}
