using System.Globalization;
using TRobot.Application.Attendance;
using TRobot.Application.Local;
using TRobot.Application.Polls;
using TRobot.Domain;

namespace TRobot.Application.Simulation;

public sealed record SimulationCommandResult(string Reply, bool Duplicate = false);

/// <summary>Deterministic organizer-only local commands; never queues replies for transport.</summary>
public sealed class SimulationCommandService(AttendanceService attendance, IAttendanceStore attendanceStore,
    ILocalStore store, LocalOperations operations, PollService polls, OperationalCommandService operational)
{
    // The prototype runs in one local process. Share the gate across request scopes
    // so attendance and vote receipts cannot race in this simulator.
    private static readonly SemaphoreSlim WriteGate = new(1, 1);
    public const string Help = "Comandi: !presenza si|forse|no; !menu [pagina]; !todo [pagina]; !porto <idvoce>; !prendo <idattività>; !fatto <idattività>; !vota <GUID sondaggio> <numero opzione>; !aiuto. "
        + "Usa i riferimenti brevi mostrati negli elenchi o i GUID completi. Conferma esplicitamente voti e impegni nel simulatore.";

    public async Task<SimulationCommandResult> ExecuteAsync(Guid actorId, SimulatedMessage message,
        bool confirmVote = false, CancellationToken ct = default)
    {
        var command = message.Text?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var writes = string.Equals(command, "!presenza", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "!vota", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "!porto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "!prendo", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command, "!fatto", StringComparison.OrdinalIgnoreCase);
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
        if (command == "!menu")
        {
            var listPage = ListPage(parts);
            var menu = (await operations.ReadAsync<MenuItem>(actorId, message.EventId, ct))
                .Where(x => x.Availability != MenuAvailability.Cancelled).ToArray();
            return new($"Menu: {menu.Length} voci attive; {menu.Count(x => x.Availability == MenuAvailability.Ready)} pronte, "
                + $"{menu.Count(x => x.Availability == MenuAvailability.Assigned)} assegnate, "
                + $"{menu.Count(x => x.Availability == MenuAvailability.Planned)} da organizzare."
                + PageText(menu.OrderBy(x => x.Name).ThenBy(x => x.Id).ToArray(), listPage, "!menu", x =>
                    $"[{SimulationReferences.Short(x.Id)}] {OneLine(x.Name)} — {x.Quantity.ToString("0.###", CultureInfo.InvariantCulture)} {OneLine(x.Unit)} — {MenuState(x.Availability)}"));
        }
        if (command == "!todo")
        {
            var listPage = ListPage(parts);
            var tasks = await operations.ReadAsync<OrganizationTask>(actorId, message.EventId, ct);
            return new($"Attività: {tasks.Count(x => x.Status == OrganizationTaskStatus.Todo)} da iniziare, "
                + $"{tasks.Count(x => x.Status == OrganizationTaskStatus.InProgress)} in corso, "
                + $"{tasks.Count(x => x.Status == OrganizationTaskStatus.Completed)} completate."
                + PageText(tasks.Where(x => x.Status is OrganizationTaskStatus.Todo or OrganizationTaskStatus.InProgress)
                    .OrderBy(x => x.Title).ThenBy(x => x.Id).ToArray(), listPage, "!todo", x =>
                        $"[{SimulationReferences.Short(x.Id)}] {OneLine(x.Title)} — {(x.Status == OrganizationTaskStatus.Todo ? "Da iniziare" : "In corso")}"));
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

    private static string MenuState(MenuAvailability state) => state switch
    { MenuAvailability.Ready => "Pronto", MenuAvailability.Assigned => "Assegnato", _ => "Da organizzare" };

    private static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ');

    private static int ListPage(string[] parts)
    {
        if (parts.Length == 1) return 1;
        if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var page)
            && page is >= 1 and <= 100000) return page;
        throw new LocalValidationException("Indica una pagina intera tra 1 e 100000, per esempio !menu 2 oppure !todo 2.");
    }

    private static string PageText<T>(IReadOnlyList<T> items, int page, string command, Func<T, string> render)
    {
        const int size = 5;
        var pages = Math.Max(1, (items.Count + size - 1) / size);
        if (page > pages) throw new LocalValidationException($"Pagina inesistente: disponibili {pages} pagine. Usa {command} 1.");
        return $"\nPagina {page}/{pages}; {items.Count} elementi nell'elenco."
            + string.Concat(items.Skip((page - 1) * size).Take(size).Select(x => "\n" + render(x)))
            + (page < pages ? $"\nPagina successiva: {command} {page + 1}" : "");
    }

    private static string Required(string? value, int maximum, string error) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximum ? value.Trim() : throw new LocalValidationException(error);
}
