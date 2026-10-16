using System.Globalization;
using TRobot.Domain;

namespace TRobot.Application.Attendance;

public sealed record ParticipantAttendance(Guid EventId, string EventName, Guid PersonId, string DisplayName,
    ParticipationStatus Status, DateTime? UpdatedAtUtc)
{
    public string Version => AttendanceVersion.Create(Status, UpdatedAtUtc);
}
public sealed record OrganizerAttendanceCommand(Guid EventId, Guid PersonId, Guid OperationId,
    ParticipationStatus Status, string ExpectedVersion, bool Confirmed);
public sealed record OrganizerAttendanceResult(bool Duplicate, bool Changed, ParticipationStatus Status);

internal static class AttendanceVersion
{
    public static string Create(ParticipationStatus status, DateTime? updatedAtUtc) =>
        ((int)status).ToString(CultureInfo.InvariantCulture) + ":" + (updatedAtUtc?.Ticks.ToString(CultureInfo.InvariantCulture) ?? "-");
}

public sealed class OrganizerAttendanceService(IAttendanceStore store, TimeProvider clock)
{
    public async Task<ParticipantAttendance> GetAsync(Guid actorId, Guid eventId, Guid personId, CancellationToken ct = default)
    {
        await AuthorizeAsync(actorId, eventId, ct);
        if (personId == Guid.Empty) throw new AttendanceValidationException("Seleziona un partecipante dell'evento.");
        return await store.ReadParticipantAsync(eventId, personId, ct)
            ?? throw new AttendanceValidationException("Partecipante non presente nell'evento.");
    }

    public Task<OrganizerAttendanceResult> SetAsync(Guid actorId, OrganizerAttendanceCommand command, CancellationToken ct = default) =>
        store.InTransactionAsync(async token =>
        {
            await AuthorizeAsync(actorId, command.EventId, token);
            if (!command.Confirmed) throw new AttendanceValidationException("Conferma esplicitamente la registrazione della presenza per questa persona.");
            if (command.PersonId == Guid.Empty || command.OperationId == Guid.Empty)
                throw new AttendanceValidationException("Partecipante o identificativo operazione non valido.");
            if (command.Status is not (ParticipationStatus.Confirmed or ParticipationStatus.Undecided or ParticipationStatus.Absent))
                throw new AttendanceValidationException("Scegli Confermato, Indeciso o Assente.");
            if (string.IsNullOrWhiteSpace(command.ExpectedVersion) || command.ExpectedVersion.Length > 100)
                throw new AttendanceValidationException("Versione presenza non valida. Ricarica la pagina.");
            var member = await store.FindParticipantByPersonAsync(command.EventId, command.PersonId, token)
                ?? throw new AttendanceValidationException("Partecipante non presente nell'evento.");
            var messageId = "organizer-" + command.OperationId.ToString("N");
            var previous = await store.FindReceiptAsync(command.EventId, messageId, token);
            if (previous is not null)
            {
                if (previous.Origin != AttendanceOrigin.Organizer || previous.PersonId != command.PersonId
                    || previous.OrganizerPersonId != actorId || previous.Status != command.Status)
                    throw new AttendanceConflictException("Identificativo operazione già usato con dati diversi. Ricarica la pagina.");
                return new OrganizerAttendanceResult(true, false, member.Status);
            }
            if (AttendanceVersion.Create(member.Status, member.UpdatedAtUtc) != command.ExpectedVersion)
                throw new AttendanceConflictException("La presenza è stata modificata. Ricarica la pagina prima di riprovare.");
            var now = clock.GetUtcNow().UtcDateTime;
            var oldStatus = member.Status;
            var changed = member.ChangeStatus(command.Status, now);
            store.AddReceipt(new(command.EventId, command.PersonId, actorId, messageId, oldStatus, command.Status, now, AttendanceOrigin.Organizer));
            await store.SaveAsync(token);
            return new OrganizerAttendanceResult(false, changed, member.Status);
        }, ct);

    private async Task AuthorizeAsync(Guid actorId, Guid eventId, CancellationToken ct)
    {
        if (actorId == Guid.Empty || eventId == Guid.Empty || !await store.IsEnabledOrganizerAsync(eventId, actorId, ct))
            throw new AttendanceAccessException();
    }
}
