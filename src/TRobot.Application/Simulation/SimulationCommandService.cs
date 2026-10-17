using TRobot.Application.Attendance;
using TRobot.Application.Local;
using TRobot.Domain;
using static TRobot.Application.Simulation.SimulationReplies;

namespace TRobot.Application.Simulation;

public sealed record SimulationCommandResult(string Reply, bool Duplicate = false);

/// <summary>Deterministic organizer-only local commands; never queues replies for transport.</summary>
public sealed class SimulationCommandService(AttendanceService attendance, IAttendanceStore attendanceStore,
    ILocalStore store, LocalOperations operations, PollQuizCommandService questionnaires, OperationalCommandService operational)
{
    // The prototype runs in one local process. Share the gate across request scopes
    // so attendance and vote receipts cannot race in this simulator.
    private static readonly SemaphoreSlim WriteGate = new(1, 1);
    public const string Help = "Comandi: !presenza si|forse|no; !menu [pagina]; !todo [pagina]; !porto <idvoce>; !prendo <idattività>; !fatto <idattività>; "
        + "!sondaggi [pagina]; !sondaggio <id>; !vota <id> <numero opzione>; !quiz; !quiz lista [pagina]; !quiz <id>; !rispondi <id> <numero opzione>; !aiuto. "
        + "Usa i riferimenti brevi degli elenchi o i GUID completi. Conferma voti, risposte e impegni. Quiz: adesione volontaria dalla pagina Giochi e una sola risposta definitiva.";

    public async Task<SimulationCommandResult> ExecuteAsync(Guid actorId, SimulatedMessage message,
        bool confirmVote = false, CancellationToken ct = default)
    {
        var command = message.Text?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var writes = string.Equals(command, "!presenza", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "!vota", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "!porto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "!prendo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "!fatto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "!rispondi", StringComparison.OrdinalIgnoreCase);
        if (!writes) return await ExecuteCoreAsync(actorId, message, confirmVote, ct);
        await WriteGate.WaitAsync(ct);
        try { return await ExecuteCoreAsync(actorId, message, confirmVote, ct); }
        finally { WriteGate.Release(); }
    }

    private async Task<SimulationCommandResult> ExecuteCoreAsync(Guid actorId, SimulatedMessage message,
        bool confirmVote, CancellationToken ct)
    {
        await operations.AuthorizeAsync(actorId, message.EventId, ct);
        var sender = Required(message.SenderId, 200, "Seleziona il mittente.");
        var messageId = Required(message.MessageId, 100, "Identificativo messaggio obbligatorio, massimo 100 caratteri.");
        var text = Required(message.Text, 100, Help);
        var person = await attendanceStore.FindParticipantAsync(message.EventId, sender, ct)
            ?? throw new LocalValidationException("Il mittente non è un partecipante abilitato per questo evento.");
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].ToLowerInvariant();
        var operationId = SimulationReferences.MessageId(message.EventId, messageId);
        if (command is "!porto" or "!prendo" or "!fatto")
        {
            if (parts.Length != 2) throw new LocalValidationException(Help);
            var action = command == "!porto" ? SimulationAction.BringMenu
                : command == "!prendo" ? SimulationAction.ClaimTask : SimulationAction.CompleteTask;
            return await operational.ExecuteAsync(actorId, message with { SenderId = sender, MessageId = messageId }, action, parts[1], confirmVote, ct);
        }
        if (await store.FindAsync<SimulationActionReceipt>(message.EventId, operationId, ct) is not null)
            throw new LocalConflictException("Identificativo messaggio già utilizzato per un impegno.");
        if (command != "!rispondi" && await store.FindAsync<QuizAnswer>(message.EventId, operationId, ct) is not null)
            throw new LocalConflictException("Identificativo messaggio già utilizzato per una risposta quiz.");
        var receipt = await store.FindAsync<PollVoteReceipt>(message.EventId, operationId, ct);
        if (command != "!vota" && receipt is not null)
            throw new LocalConflictException("Identificativo messaggio già utilizzato per un voto.");
        if (command == "!presenza")
        {
            var result = await attendance.SimulateAsync(actorId, message with { SenderId = sender, MessageId = messageId }, ct);
            var label = result.Status switch
            {
                ParticipationStatus.Confirmed => "Confermato",
                ParticipationStatus.Undecided => "Indeciso",
                ParticipationStatus.Absent => "Assente",
                _ => "Senza risposta"
            };
            return new(result.Duplicate ? "Messaggio già elaborato: nessun duplicato."
                : result.Changed ? $"Stato aggiornato: {label}. Conteggi aggiornati."
                : $"Stato già impostato: {label}. Conteggi invariati.", result.Duplicate);
        }
        if (await attendanceStore.FindReceiptAsync(message.EventId, messageId, ct) is not null)
            throw new LocalConflictException("Identificativo messaggio già utilizzato per una presenza.");
        if (parts.Length == 1 && command == "!aiuto") return new(Help);
        if (command is "!sondaggi" or "!sondaggio" or "!quiz" or "!vota" or "!rispondi")
            return await questionnaires.ExecuteAsync(actorId, message with { SenderId = sender, MessageId = messageId }, parts, confirmVote, ct);
        if (command == "!menu")
        {
            var listPage = ListPage(parts);
            var menu = await operations.ReadAsync<MenuItem>(actorId, message.EventId, ct);
            return new(OperationalListText.Page(OperationalListText.Menu(menu), listPage, "!menu"));
        }
        if (command == "!todo")
        {
            var listPage = ListPage(parts);
            var tasks = await operations.ReadAsync<OrganizationTask>(actorId, message.EventId, ct);
            return new(OperationalListText.Page(OperationalListText.Todo(tasks), listPage, "!todo"));
        }
        throw new LocalValidationException(Help);
    }

    private static string Required(string? value, int maximum, string error) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximum ? value.Trim() : throw new LocalValidationException(error);
}
