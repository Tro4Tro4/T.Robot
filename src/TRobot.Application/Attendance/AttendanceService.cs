using TRobot.Domain;

namespace TRobot.Application.Attendance;

public sealed class AttendanceService(IAttendanceStore store, TimeProvider clock)
{
    public Task<IReadOnlyList<EventOption>> GetEventsAsync(Guid actorId, CancellationToken ct = default) =>
        store.GetEventsAsync(actorId, ct);

    public async Task<AttendanceDashboard> GetDashboardAsync(Guid actorId, Guid eventId, CancellationToken ct = default)
    {
        await AuthorizeAsync(actorId, eventId, ct);
        return await store.GetDashboardAsync(eventId, ct);
    }

    public async Task<SimulationResult> SimulateAsync(Guid actorId, SimulatedMessage message, CancellationToken ct = default)
    {
        await AuthorizeAsync(actorId, message.EventId, ct);
        var sender = Required(message.SenderId, 200, "Seleziona il mittente.");
        var messageId = Required(message.MessageId, 100, "L'identificativo messaggio deve contenere da 1 a 100 caratteri.");
        var status = Parse(message.Text);

        return await store.InTransactionAsync(async token =>
        {
            // Riconvalida i permessi nella stessa transazione che effettua la scrittura.
            await AuthorizeAsync(actorId, message.EventId, token);
            var participant = await store.FindParticipantAsync(message.EventId, sender, token)
                ?? throw new AttendanceValidationException("Il mittente non è un partecipante abilitato per questo evento.");
            var previous = await store.FindReceiptAsync(message.EventId, messageId, token);
            if (previous is not null)
            {
                if (previous.PersonId != participant.PersonId || previous.Status != status || previous.Origin != AttendanceOrigin.Simulation)
                    throw new AttendanceConflictException("Questo identificativo messaggio è già stato usato con dati diversi.");
                return new SimulationResult(true, false, participant.Status);
            }

            var now = clock.GetUtcNow().UtcDateTime;
            var oldStatus = participant.Status;
            var changed = participant.ChangeStatus(status, now);
            store.AddReceipt(new(message.EventId, participant.PersonId, actorId, messageId, oldStatus, status, now));
            await store.SaveAsync(token);
            return new SimulationResult(false, changed, participant.Status);
        }, ct);
    }

    public static ParticipationStatus Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 100)
            throw new AttendanceValidationException("Scrivi !presenza si, !presenza forse o !presenza no per una sola persona.");
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && parts[0].Equals("!presenza", StringComparison.OrdinalIgnoreCase))
        {
            switch (parts[1].ToLowerInvariant())
            {
                case "si":
                case "sì": return ParticipationStatus.Confirmed;
                case "forse": return ParticipationStatus.Undecided;
                case "no": return ParticipationStatus.Absent;
            }
        }
        throw new AttendanceValidationException("Comando non supportato. Usa !presenza si, !presenza forse o !presenza no, senza conteggi o accompagnatori.");
    }

    private async Task AuthorizeAsync(Guid actorId, Guid eventId, CancellationToken ct)
    {
        if (actorId == Guid.Empty || eventId == Guid.Empty || !await store.IsOrganizerAsync(eventId, actorId, ct))
            throw new AttendanceAccessException();
    }

    private static string Required(string? value, int maxLength, string error)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength)
            throw new AttendanceValidationException(error);
        return value.Trim();
    }
}
