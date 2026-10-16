using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TRobot.Application.Attendance;
using TRobot.Application.Local;
using TRobot.Application.Polls;
using TRobot.Domain;

namespace TRobot.Application.Simulation;

public sealed record SimulationCommandResult(string Reply, bool Duplicate = false);

/// <summary>Deterministic local commands; responses contain aggregate data only.</summary>
public sealed class SimulationCommandService(AttendanceService attendance, IAttendanceStore attendanceStore,
    ILocalStore store, LocalOperations operations, PollService polls)
{
    // The prototype runs in one local process. Share the gate across request scopes
    // so attendance and vote receipts cannot race in this simulator.
    private static readonly SemaphoreSlim WriteGate = new(1, 1);
    public const string Help = "Comandi: !presenza si|forse|no; !menu; !todo; !vota <GUID sondaggio> <numero opzione>; !aiuto. "
        + "Per votare usa l'identificativo nella pagina del sondaggio e conferma esplicitamente il voto nel simulatore.";

    public async Task<SimulationCommandResult> ExecuteAsync(Guid actorId, SimulatedMessage message,
        bool confirmVote = false, CancellationToken ct = default)
    {
        var command = message.Text?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var writes = string.Equals(command, "!presenza", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "!vota", StringComparison.OrdinalIgnoreCase);
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
        var operationId = VoteOperationId(message.EventId, messageId);
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
        if (parts.Length == 1 && command == "!menu")
        {
            var menu = (await operations.ReadAsync<MenuItem>(actorId, message.EventId, ct))
                .Where(x => x.Availability != MenuAvailability.Cancelled).ToArray();
            return new($"Menu: {menu.Length} voci attive; {menu.Count(x => x.Availability == MenuAvailability.Ready)} pronte, "
                + $"{menu.Count(x => x.Availability == MenuAvailability.Assigned)} assegnate, "
                + $"{menu.Count(x => x.Availability == MenuAvailability.Planned)} da organizzare.");
        }
        if (parts.Length == 1 && command == "!todo")
        {
            var tasks = await operations.ReadAsync<OrganizationTask>(actorId, message.EventId, ct);
            return new($"Attività: {tasks.Count(x => x.Status == OrganizationTaskStatus.Todo)} da iniziare, "
                + $"{tasks.Count(x => x.Status == OrganizationTaskStatus.InProgress)} in corso, "
                + $"{tasks.Count(x => x.Status == OrganizationTaskStatus.Completed)} completate.");
        }
        if (command != "!vota" || parts.Length != 3 || !Guid.TryParse(parts[1], out var pollId) || pollId == Guid.Empty
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var position) || position < 1)
            throw new LocalValidationException(Help);
        if (!confirmVote) throw new LocalValidationException("Conferma esplicitamente il voto selezionando la casella nel simulatore.");
        var page = await polls.ReadAsync(actorId, message.EventId, pollId, ct);
        var option = page.Counts.SingleOrDefault(x => x.Option.Position == position)?.Option
            ?? throw new LocalValidationException("Numero opzione non presente nel sondaggio.");
        await polls.VoteAsync(actorId, message.EventId, pollId,
            new(operationId, person.PersonId, option.Id, page.Poll.Revision, true), ct);
        return new(receipt is null ? "Voto registrato. Nessun messaggio reale inviato." : "Messaggio già elaborato: nessun voto duplicato.", receipt is not null);
    }

    private static Guid VoteOperationId(Guid eventId, string messageId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"TRobot:simulation-vote:v1:{eventId:N}:{messageId}"))[..16]);

    private static string Required(string? value, int maximum, string error) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximum ? value.Trim() : throw new LocalValidationException(error);
}
