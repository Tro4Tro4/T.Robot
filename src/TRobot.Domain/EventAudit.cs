namespace TRobot.Domain;

public enum EventAuditAction { EventCreated, EventUpdated, ParticipantAdded, ParticipantRenamed }

public sealed class EventAudit
{
    private EventAudit() { }

    public EventAudit(Guid eventId, Guid actorId, EventAuditAction action, DateTime occurredAtUtc,
        Guid? personId = null, Guid? operationId = null, string? registrationFingerprint = null)
    {
        EventId = Guard.Id(eventId, nameof(eventId));
        ActorId = Guard.Id(actorId, nameof(actorId));
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        if (occurredAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("La data deve essere UTC.", nameof(occurredAtUtc));
        if ((action is EventAuditAction.ParticipantAdded or EventAuditAction.ParticipantRenamed) != personId.HasValue)
            throw new ArgumentException("La persona è richiesta per le operazioni sui partecipanti.", nameof(personId));
        PersonId = personId.HasValue ? Guard.Id(personId.Value, nameof(personId)) : null;
        Id = operationId.HasValue ? Guard.Id(operationId.Value, nameof(operationId)) : Guid.NewGuid();
        Action = action;
        OccurredAtUtc = occurredAtUtc;
        if (registrationFingerprint is not null) CaptureRegistrationFingerprint(registrationFingerprint);
    }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid ActorId { get; private set; }
    public Guid? PersonId { get; private set; }
    public EventAuditAction Action { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string? RegistrationFingerprint { get; private set; }

    public void CaptureRegistrationFingerprint(string fingerprint)
    {
        if (Action != EventAuditAction.ParticipantAdded)
            throw new InvalidOperationException("L'impronta è prevista solo per la registrazione di un partecipante.");
        if (fingerprint is null || fingerprint.Length != 64 || fingerprint.Any(c => !char.IsAsciiHexDigitUpper(c)))
            throw new ArgumentException("Impronta di registrazione non valida.", nameof(fingerprint));
        if (RegistrationFingerprint is not null && RegistrationFingerprint != fingerprint)
            throw new InvalidOperationException("L'impronta della registrazione è immutabile.");
        RegistrationFingerprint = fingerprint;
    }
}
