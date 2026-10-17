using System.Globalization;
using TRobot.Application.Attendance;
using TRobot.Application.Games;
using TRobot.Application.Local;
using TRobot.Application.Polls;
using TRobot.Domain;
using static TRobot.Application.Simulation.SimulationReplies;

namespace TRobot.Application.Simulation;

/// <summary>Explicitly formats public questionnaire fields; never formats answers, scores or solutions.</summary>
public sealed class PollQuizCommandService(ILocalStore store, IAttendanceStore attendanceStore,
    LocalOperations operations, PollService polls, GameService games, TimeProvider clock)
{
    public async Task<SimulationCommandResult> ExecuteAsync(Guid actorId, SimulatedMessage message,
        string[] parts, bool confirmed, CancellationToken ct = default)
    {
        await operations.AuthorizeAsync(actorId, message.EventId, ct);
        var person = await attendanceStore.FindParticipantAsync(message.EventId, message.SenderId.Trim(), ct)
            ?? throw new LocalValidationException("Il mittente non è un partecipante abilitato per questo evento.");
        var command = parts[0].ToLowerInvariant();
        if (command == "!sondaggi") return await PollList(actorId, message.EventId, ListPage(parts), ct);
        if (command == "!sondaggio" && parts.Length == 2)
        {
            var poll = SimulationReferences.Resolve(await store.ListAsync<Poll>(message.EventId, ct), parts[1]);
            var page = await polls.ReadAsync(actorId, message.EventId, poll.Id, ct);
            RequireSent(page.Publication?.Status, page.Poll.Status == PollStatus.Cancelled);
            return new($"Sondaggio [{SimulationReferences.Short(poll.Id)}]: {OneLine(poll.Title)}\nStato: {PollState(poll, page.Open)}"
                + $"\nScadenza: {Deadline(page.Event, poll.DeadlineUtc)}"
                + string.Concat(page.Counts.Take(10).Select(x => $"\n{x.Option.Position}. {OneLine(x.Option.Text)}"))
                + $"\nVota: !vota {SimulationReferences.Short(poll.Id)} <numero opzione>");
        }
        if (command == "!quiz")
        {
            if (parts.Length == 1) return await QuizList(actorId, message.EventId, 1, ct);
            if (parts[1].Equals("lista", StringComparison.OrdinalIgnoreCase))
                return await QuizList(actorId, message.EventId, ListPage(parts.Skip(1).ToArray()), ct);
            if (parts.Length == 2)
            {
                var quiz = SimulationReferences.Resolve(await store.ListAsync<Quiz>(message.EventId, ct), parts[1]);
                var page = await games.ReadAsync(actorId, message.EventId, quiz.Id, ct);
                RequireSent(page.Publication?.Status, page.Quiz.Status == QuizStatus.Cancelled);
                return new($"Quiz [{SimulationReferences.Short(quiz.Id)}]: {OneLine(quiz.Question)}\nStato: {QuizState(quiz, page.Open)}"
                    + $"\nScadenza: {Deadline(page.Event, quiz.DeadlineUtc)}"
                    + string.Concat(page.Options.Take(10).Select(x => $"\n{x.Position}. {OneLine(x.Text)}"))
                    + $"\nRispondi: !rispondi {SimulationReferences.Short(quiz.Id)} <numero opzione>. Una sola risposta definitiva; serve adesione volontaria attiva.");
            }
        }
        if (command is not ("!vota" or "!rispondi") || parts.Length != 3
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var position) || position is < 1 or > 10)
            throw new LocalValidationException(SimulationCommandService.Help);
        if (!confirmed) throw new LocalValidationException("Conferma esplicitamente l'operazione selezionando la casella nel simulatore.");
        var operationId = SimulationReferences.MessageId(message.EventId, message.MessageId.Trim());
        if (await attendanceStore.FindReceiptAsync(message.EventId, message.MessageId.Trim(), ct) is not null
            || await store.FindAsync<SimulationActionReceipt>(message.EventId, operationId, ct) is not null)
            throw new LocalConflictException("Identificativo messaggio già utilizzato per un'altra operazione.");
        if (command == "!vota")
        {
            if (await store.FindAsync<QuizAnswer>(message.EventId, operationId, ct) is not null)
                throw new LocalConflictException("Identificativo messaggio già utilizzato per una risposta quiz.");
            var poll = SimulationReferences.Resolve(await store.ListAsync<Poll>(message.EventId, ct), parts[1]);
            var page = await polls.ReadAsync(actorId, message.EventId, poll.Id, ct);
            var option = page.Counts.SingleOrDefault(x => x.Option.Position == position)?.Option
                ?? throw new LocalValidationException("Numero opzione non presente nel sondaggio.");
            var duplicate = await store.FindAsync<PollVoteReceipt>(message.EventId, operationId, ct) is not null;
            await polls.VoteAsync(actorId, message.EventId, poll.Id, new(operationId, person.PersonId, option.Id, page.Poll.Revision, true), ct);
            return new(duplicate ? "Messaggio già elaborato: nessun voto duplicato." : "Voto registrato. Nessun messaggio reale inviato.", duplicate);
        }
        if (await store.FindAsync<PollVoteReceipt>(message.EventId, operationId, ct) is not null)
            throw new LocalConflictException("Identificativo messaggio già utilizzato per un voto.");
        var target = SimulationReferences.Resolve(await store.ListAsync<Quiz>(message.EventId, ct), parts[1]);
        var quizPage = await games.ReadAsync(actorId, message.EventId, target.Id, ct);
        var answer = quizPage.Options.SingleOrDefault(x => x.Position == position)
            ?? throw new LocalValidationException("Numero opzione non presente nel quiz.");
        var replay = await store.FindAsync<QuizAnswer>(message.EventId, operationId, ct) is not null;
        await games.AnswerAsync(actorId, message.EventId, target.Id, new(operationId, person.PersonId, answer.Id, quizPage.Quiz.Revision, true), ct);
        return new(replay ? "Messaggio già elaborato: nessuna risposta duplicata." : "Risposta registrata, definitiva per questo quiz.", replay);
    }

    private async Task<SimulationCommandResult> PollList(Guid actorId, Guid eventId, int page, CancellationToken ct)
    {
        await operations.AuthorizeAsync(actorId, eventId, ct);
        var ev = await store.EventAsync(eventId, ct) ?? throw new LocalAccessException();
        var sent = await Sent(eventId, ct); var now = clock.GetUtcNow().UtcDateTime;
        var rows = (await store.ListAsync<Poll>(eventId, ct)).Where(x => x.Status == PollStatus.PublicationQueued
            && x.DeadlineUtc > now && x.PublicationMessageId.HasValue && sent.Contains(x.PublicationMessageId.Value))
            .OrderBy(x => x.DeadlineUtc).ThenBy(x => x.Id).ToArray();
        return new($"Sondaggi aperti: {rows.Length}." + PageText(rows, page, "!sondaggi", x =>
            $"[{SimulationReferences.Short(x.Id)}] {OneLine(x.Title)} — {Deadline(ev, x.DeadlineUtc)}")
            + "\nDettaglio: !sondaggio <riferimento>.");
    }

    private async Task<SimulationCommandResult> QuizList(Guid actorId, Guid eventId, int page, CancellationToken ct)
    {
        await operations.AuthorizeAsync(actorId, eventId, ct);
        var ev = await store.EventAsync(eventId, ct) ?? throw new LocalAccessException();
        var sent = await Sent(eventId, ct); var now = clock.GetUtcNow().UtcDateTime;
        var rows = (await store.ListAsync<Quiz>(eventId, ct)).Where(x => x.Status == QuizStatus.PublicationQueued
            && x.DeadlineUtc > now && x.PublicationMessageId.HasValue && sent.Contains(x.PublicationMessageId.Value))
            .OrderBy(x => x.DeadlineUtc).ThenBy(x => x.Id).ToArray();
        return new($"Quiz aperti: {rows.Length}." + PageText(rows, page, "!quiz lista", x =>
            $"[{SimulationReferences.Short(x.Id)}] {OneLine(x.Question)} — {Deadline(ev, x.DeadlineUtc)}")
            + "\nDettaglio: !quiz <riferimento>. Adesione volontaria gestita dagli organizzatori nella pagina Giochi.");
    }

    private async Task<HashSet<Guid>> Sent(Guid eventId, CancellationToken ct) =>
        (await store.ListAsync<OutboundMessage>(eventId, ct)).Where(x => x.Status == OutboundMessageStatus.Sent).Select(x => x.Id).ToHashSet();
    private static void RequireSent(OutboundMessageStatus? status, bool cancelled)
    { if (status != OutboundMessageStatus.Sent || cancelled) throw new LocalValidationException("Contenuto non pubblicato con invio simulato concluso."); }
    private static string Deadline(Event ev, DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " (" + ev.TimeZoneId + ")";
    private static string PollState(Poll row, bool open) => row.Status == PollStatus.Closed ? "Chiuso" : open ? "Aperto" : "Scaduto";
    private static string QuizState(Quiz row, bool open) => row.Status == QuizStatus.Closed ? "Chiuso" : open ? "Aperto" : "Scaduto";
}
