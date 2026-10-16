using System.Globalization;
using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.Games;

public sealed record QuizDraft(Guid OperationId, string Question, DateTime DeadlineLocal, IReadOnlyList<string> Options, int CorrectPosition);
public sealed record QuizBallot(Guid OperationId, Guid PersonId, Guid OptionId, int Revision, bool Confirmed);
public sealed record GamePerson(LocalPerson Person, GameMembership? Membership);
public sealed record GameScore(int Position, string Name, int Points);
public sealed record GameOverview(Event Event, IReadOnlyList<Quiz> Quizzes, IReadOnlyList<GamePerson> People, IReadOnlyList<GameScore> Scores);
public sealed record QuizPage(Event Event, Quiz Quiz, IReadOnlyList<QuizOption> Options, IReadOnlyList<LocalPerson> Eligible,
    IReadOnlyList<QuizAnswer> Answers, OutboundMessage? Publication, string Preview, bool Open);

public sealed class GameService(ILocalStore store, LocalOperations operations, TimeProvider clock)
{
    public async Task<GameOverview> ListAsync(Guid actorId, Guid eventId, CancellationToken ct = default)
    {
        var ev = await AuthorizedEvent(actorId, eventId, ct);
        var people = await store.ParticipantsAsync(eventId, ct); var memberships = await store.ListAsync<GameMembership>(eventId, ct);
        var quizzes = await store.ListAsync<Quiz>(eventId, ct); var answers = await store.ListAsync<QuizAnswer>(eventId, ct);
        var options = (await store.ListAsync<QuizOption>(eventId, ct)).ToDictionary(x => x.Id);
        var closed = quizzes.Where(x => x.Status == QuizStatus.Closed).ToDictionary(x => x.Id);
        var ordered = people.Where(p => memberships.Any(m => m.PersonId == p.Id && m.IsActive))
            .Select(p => new { p.Name, Points = answers.Count(a => a.PersonId == p.Id && closed.TryGetValue(a.QuizId, out var q) && options[a.OptionId].Position == q.CorrectPosition) })
            .OrderByDescending(x => x.Points).ThenBy(x => x.Name).ToArray();
        var scores = ordered.Select(x => new GameScore(1 + ordered.Count(y => y.Points > x.Points), x.Name, x.Points)).ToArray();
        return new(ev, quizzes.OrderBy(x => x.DeadlineUtc).ToArray(), people.Select(p => new GamePerson(p, memberships.SingleOrDefault(m => m.PersonId == p.Id))).ToArray(), scores);
    }
    public async Task<QuizPage> ReadAsync(Guid actorId, Guid eventId, Guid quizId, CancellationToken ct = default)
    {
        var ev = await AuthorizedEvent(actorId, eventId, ct); var quiz = await Get(eventId, quizId, ct);
        var options = await Options(eventId, quizId, ct);
        var answers = (await store.ListAsync<QuizAnswer>(eventId, ct)).Where(x => x.QuizId == quizId).ToArray();
        var members = await store.ListAsync<GameMembership>(eventId, ct);
        var eligible = (await store.ParticipantsAsync(eventId, ct)).Where(p => members.Any(m => m.PersonId == p.Id && m.IsActive) && !answers.Any(a => a.PersonId == p.Id)).ToArray();
        var message = quiz.PublicationMessageId.HasValue ? await store.FindAsync<OutboundMessage>(eventId, quiz.PublicationMessageId.Value, ct) : null;
        return new(ev, quiz, options, eligible, answers, message, Text(ev, quiz, options),
            quiz.Status == QuizStatus.PublicationQueued && message?.Status == OutboundMessageStatus.Sent && clock.GetUtcNow().UtcDateTime < quiz.DeadlineUtc);
    }
    public Task MembershipAsync(Guid actorId, Guid eventId, Guid personId, int revision, bool active, bool confirmed, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            Confirm(confirmed);
            if (!(await store.ParticipantsAsync(eventId, token)).Any(p => p.Id == personId)) throw new LocalValidationException("Serve un partecipante di questo evento.");
            var membership = (await store.ListAsync<GameMembership>(eventId, token)).SingleOrDefault(x => x.PersonId == personId);
            if (membership is null)
            {
                if (revision != 0) throw new LocalConflictException("Adesione modificata. Ricarica la pagina.");
                if (!active) throw new LocalValidationException("Nessuna adesione da ritirare.");
                membership = new GameMembership(eventId, personId); store.Add(membership);
            }
            else
            {
                membership = (await store.FindAsync<GameMembership>(eventId, membership.Id, token))!;
                LocalOperations.Revision(membership, revision);
                if (!membership.SetActive(active)) return true;
            }
            operations.Audit(eventId, actorId, membership.Id, "Games", active ? "Joined" : "Withdrawn"); await store.SaveAsync(token); return true;
        }, ct);
    public Task<Guid> CreateAsync(Guid actorId, Guid eventId, QuizDraft input, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var deadline = await operations.ToUtcAsync(eventId, input.DeadlineLocal, token) ?? throw new LocalValidationException("Scadenza obbligatoria.");
            if (deadline <= clock.GetUtcNow().UtcDateTime) throw new LocalValidationException("La scadenza deve essere futura.");
            var quiz = new Quiz(eventId, input.OperationId, input.Question, deadline, input.CorrectPosition);
            var texts = input.Options.Select(x => x.Trim()).ToArray();
            if (texts.Length is < 2 or > 10 || texts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != texts.Length || input.CorrectPosition > texts.Length)
                throw new LocalValidationException("Servono 2–10 opzioni distinte e una posizione corretta tra quelle presenti.");
            var options = texts.Select((x, i) => new QuizOption(eventId, quiz.Id, i + 1, x)).ToArray();
            var existing = await store.FindAsync<Quiz>(eventId, quiz.Id, token);
            if (existing is not null)
            {
                if (existing.Revision == 0 && existing.Question == quiz.Question && existing.DeadlineUtc == deadline && existing.CorrectPosition == quiz.CorrectPosition
                    && (await Options(eventId, quiz.Id, token)).Select(x => x.Text).SequenceEqual(texts)) return existing.Id;
                throw new LocalConflictException("Identificativo quiz già utilizzato.");
            }
            store.Add(quiz); foreach (var option in options) store.Add(option);
            operations.Audit(eventId, actorId, quiz.Id, "Games", "DraftCreated"); await store.SaveAsync(token); return quiz.Id;
        }, ct);
    public Task PublishAsync(Guid actorId, Guid eventId, Guid id, int revision, bool confirmed, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            Confirm(confirmed); var quiz = await Get(eventId, id, token); LocalOperations.Revision(quiz, revision);
            if (quiz.DeadlineUtc <= clock.GetUtcNow().UtcDateTime) throw new LocalValidationException("La scadenza è trascorsa.");
            var ev = await store.EventAsync(eventId, token) ?? throw new LocalAccessException();
            var message = new OutboundMessage(eventId, Guid.NewGuid(), Text(ev, quiz, await Options(eventId, id, token)), MessageKind.Entertainment, clock.GetUtcNow().UtcDateTime);
            message.Queue(true); quiz.Publish(message.Id); store.Add(message);
            operations.Audit(eventId, actorId, id, "Games", "PublicationQueued"); await store.SaveAsync(token); return true;
        }, ct);
    public Task AnswerAsync(Guid actorId, Guid eventId, Guid id, QuizBallot input, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var quiz = await Get(eventId, id, token); var receipt = await store.FindAsync<QuizAnswer>(eventId, input.OperationId, token);
            if (receipt is not null)
            {
                if (receipt.QuizId == id && receipt.ActorId == actorId && receipt.PersonId == input.PersonId && receipt.OptionId == input.OptionId) return true;
                throw new LocalConflictException("Identificativo risposta già utilizzato per altri dati.");
            }
            Confirm(input.Confirmed); LocalOperations.Revision(quiz, input.Revision); await RequirePublished(quiz, token);
            if (clock.GetUtcNow().UtcDateTime >= quiz.DeadlineUtc) throw new LocalValidationException("Quiz scaduto.");
            if (!(await store.ListAsync<GameMembership>(eventId, token)).Any(x => x.PersonId == input.PersonId && x.IsActive)) throw new LocalValidationException("Serve un'adesione volontaria attiva.");
            if (!(await Options(eventId, id, token)).Any(x => x.Id == input.OptionId)) throw new LocalValidationException("Opzione estranea al quiz.");
            if ((await store.ListAsync<QuizAnswer>(eventId, token)).Any(x => x.QuizId == id && x.PersonId == input.PersonId)) throw new LocalValidationException("Una sola risposta per persona: la riadesione non la azzera.");
            store.Add(new QuizAnswer(eventId, id, input.OperationId, actorId, input.PersonId, input.OptionId, clock.GetUtcNow().UtcDateTime));
            quiz.Touch(); operations.Audit(eventId, actorId, id, "Games", "AnswerSimulated"); await store.SaveAsync(token); return true;
        }, ct);
    public Task CloseAsync(Guid actorId, Guid eventId, Guid id, int revision, bool confirmed, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            Confirm(confirmed); var quiz = await Get(eventId, id, token); LocalOperations.Revision(quiz, revision); await RequirePublished(quiz, token);
            quiz.Close(clock.GetUtcNow().UtcDateTime); operations.Audit(eventId, actorId, id, "Games", "Closed"); await store.SaveAsync(token); return true;
        }, ct);
    public Task CancelAsync(Guid actorId, Guid eventId, Guid id, int revision, bool confirmed, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            Confirm(confirmed); var quiz = await Get(eventId, id, token); LocalOperations.Revision(quiz, revision);
            if (quiz.PublicationMessageId.HasValue)
            {
                var message = await store.FindAsync<OutboundMessage>(eventId, quiz.PublicationMessageId.Value, token) ?? throw new LocalAccessException();
                if (message.Status == OutboundMessageStatus.Sent) throw new LocalValidationException("Quiz pubblicato: usa la chiusura.");
                message.Cancel(true);
            }
            quiz.Cancel(); operations.Audit(eventId, actorId, id, "Games", "Cancelled"); await store.SaveAsync(token); return true;
        }, ct);
    private async Task RequirePublished(Quiz quiz, CancellationToken ct)
    {
        if (quiz.Status != QuizStatus.PublicationQueued || !quiz.PublicationMessageId.HasValue || (await store.FindAsync<OutboundMessage>(quiz.EventId, quiz.PublicationMessageId.Value, ct))?.Status != OutboundMessageStatus.Sent)
            throw new LocalValidationException("Serve l'invio simulato concluso della pubblicazione.");
    }
    private async Task<Event> AuthorizedEvent(Guid actorId, Guid eventId, CancellationToken ct)
    { await operations.AuthorizeAsync(actorId, eventId, ct); return await store.EventAsync(eventId, ct) ?? throw new LocalAccessException(); }
    private async Task<Quiz> Get(Guid eventId, Guid id, CancellationToken ct) => await store.FindAsync<Quiz>(eventId, id, ct) ?? throw new LocalAccessException();
    private async Task<QuizOption[]> Options(Guid eventId, Guid id, CancellationToken ct) => (await store.ListAsync<QuizOption>(eventId, ct)).Where(x => x.QuizId == id).OrderBy(x => x.Position).ToArray();
    private static void Confirm(bool value) { if (!value) throw new LocalValidationException("Conferma esplicitamente l'operazione e l'anteprima."); }
    private static string Text(Event ev, Quiz quiz, IReadOnlyList<QuizOption> options) => "Quiz volontario: " + quiz.Question + "\n"
        + string.Join("\n", options.Select(x => $"{x.Position}. {x.Text}")) + "\nScadenza: "
        + TimeZoneInfo.ConvertTimeFromUtc(quiz.DeadlineUtc, TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
        + " (" + ev.TimeZoneId + "). Una risposta per persona aderente. Partecipazione facoltativa; puoi ritirarti dai giochi.";
}
