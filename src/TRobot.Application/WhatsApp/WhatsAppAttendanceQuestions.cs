using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.WhatsApp;

public sealed record AttendanceQuestionOptionView(Guid Id, string Text, IReadOnlyList<string> People);
public sealed record AttendanceQuestionView(Guid Id, string Text, bool IsActive, int Revision,
    IReadOnlyList<AttendanceQuestionOptionView> Options);

public sealed partial class WhatsAppService
{
    private const int MaxAttendanceQuestions = 10;

    public Task SetAttendanceStayModeAsync(Guid actorId, Guid eventId, bool askStayDates, int eventDays, int revision,
        bool confirmed, CancellationToken ct = default) => operations.ChangeAsync(actorId, eventId, async token =>
    {
        Confirm(confirmed);
        var settings = await store.FindAsync<AttendanceCollectionSettings>(eventId, eventId, token);
        var fresh = settings is null; settings ??= new(eventId);
        LocalOperations.Revision(settings, revision);
        if (eventDays is < 1 or > 31) throw new LocalValidationException("I giorni dell'evento devono essere da 1 a 31.");
        if (settings.SetStayMode(askStayDates, eventDays))
        {
            if (fresh) store.Add(settings);
            operations.Audit(eventId, actorId, eventId, "Attendance", "StayModeChanged");
            await store.SaveAsync(token);
        }
        return true;
    }, ct);

    /// <summary>Aggiunge una domanda con da 2 a 8 opzioni, una per riga; l'ordine di inserimento e' l'ordine di domanda.</summary>
    public Task<Guid> AddAttendanceQuestionAsync(Guid actorId, Guid eventId, string text, string optionsText,
        bool confirmed, CancellationToken ct = default) => operations.ChangeAsync(actorId, eventId, async token =>
    {
        Confirm(confirmed);
        var options = (optionsText ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(SafeQuestionText).Where(x => x.Length > 0).ToArray();
        if (options.Length is < 2 or > AttendanceQuestion.MaxOptions)
            throw new LocalValidationException($"Indica da 2 a {AttendanceQuestion.MaxOptions} risposte possibili, una per riga.");
        if (options.Length != options.Select(x => x.ToLowerInvariant()).Distinct().Count())
            throw new LocalValidationException("Le risposte possibili devono essere diverse tra loro.");
        if (options.Any(x => x.Length > 60)) throw new LocalValidationException("Ogni risposta puo' avere al massimo 60 caratteri.");
        var clean = SafeQuestionText(text ?? "");
        if (clean.Length is 0 or > 200) throw new LocalValidationException("La domanda deve avere da 1 a 200 caratteri.");
        var existing = await store.ListAsync<AttendanceQuestion>(eventId, token);
        if (existing.Count >= MaxAttendanceQuestions)
            throw new LocalValidationException($"Sono ammesse al massimo {MaxAttendanceQuestions} domande per evento.");
        var question = new AttendanceQuestion(eventId, clean, existing.Count == 0 ? 0 : existing.Max(x => x.Position) + 1);
        store.Add(question);
        for (var i = 0; i < options.Length; i++) store.Add(new AttendanceQuestionOption(eventId, question.Id, options[i], i));
        operations.Audit(eventId, actorId, question.Id, "Attendance", "QuestionAdded");
        await store.SaveAsync(token);
        return question.Id;
    }, ct);

    public Task SetAttendanceQuestionActiveAsync(Guid actorId, Guid eventId, Guid questionId, bool active, int revision,
        bool confirmed, CancellationToken ct = default) => operations.ChangeAsync(actorId, eventId, async token =>
    {
        Confirm(confirmed);
        var question = await store.FindAsync<AttendanceQuestion>(eventId, questionId, token) ?? throw new LocalAccessException();
        LocalOperations.Revision(question, revision);
        if (question.SetActive(active))
        {
            operations.Audit(eventId, actorId, question.Id, "Attendance", active ? "QuestionActivated" : "QuestionDeactivated");
            await store.SaveAsync(token);
        }
        return true;
    }, ct);

    // Niente caratteri di controllo ne' di formattazione: il testo finisce in un messaggio WhatsApp.
    private static string SafeQuestionText(string text) => new string(text.Where(c => !char.IsControl(c)
        && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format && c is not '*' and not '_' and not '~' and not '`' and not '@')
        .ToArray()).Trim();

    private async Task<IReadOnlyList<AttendanceQuestionView>> AttendanceQuestionViewsAsync(Guid eventId, CancellationToken ct)
    {
        var questions = (await store.ListAsync<AttendanceQuestion>(eventId, ct)).OrderBy(x => x.Position).ToArray();
        if (questions.Length == 0) return [];
        var options = await store.ListAsync<AttendanceQuestionOption>(eventId, ct);
        var answers = await store.ListAsync<AttendanceAnswer>(eventId, ct);
        var confirmed = (await store.ParticipationsAsync(eventId, ct)).Where(x => x.Status == ParticipationStatus.Confirmed)
            .ToDictionary(x => x.PersonId, x => x.Name);
        return questions.Select(q => new AttendanceQuestionView(q.Id, q.Text, q.IsActive, q.Revision,
            options.Where(o => o.QuestionId == q.Id).OrderBy(o => o.Position).Select(o => new AttendanceQuestionOptionView(o.Id, o.Text,
                answers.Where(a => a.QuestionId == q.Id && a.OptionId == o.Id && confirmed.ContainsKey(a.PersonId))
                    .Select(a => confirmed[a.PersonId]).Order(StringComparer.CurrentCultureIgnoreCase).ToArray())).ToArray())).ToArray();
    }

    private async Task<AttendanceCollectionSettings?> AttendanceSettingsAsync(Guid eventId, CancellationToken ct) =>
        await store.FindAsync<AttendanceCollectionSettings>(eventId, eventId, ct);

    private async Task<(List<AttendanceQuestion> Active, List<AttendanceQuestionOption> Options)> ActiveAttendanceQuestionsAsync(Guid eventId, CancellationToken ct)
    {
        var active = (await store.ListAsync<AttendanceQuestion>(eventId, ct)).Where(x => x.IsActive).OrderBy(x => x.Position).ToList();
        return (active, active.Count == 0 ? [] : (await store.ListAsync<AttendanceQuestionOption>(eventId, ct)).ToList());
    }

    private static string QuestionPrompt(AttendanceQuestion question, IEnumerable<AttendanceQuestionOption> options) =>
        $"❓ {question.Text}\n" + string.Join('\n', options.Where(x => x.QuestionId == question.Id).OrderBy(x => x.Position)
            .Select((x, i) => $"{i + 1} = {x.Text}")) + "\n!annulla per saltare le altre domande.";

    /// <summary>Passa alla prima domanda attiva dopo <paramref name="after"/>; senza domande chiude il dialogo e restituisce solo il testo di chiusura.</summary>
    private async Task<string> AdvanceAttendanceQuestionAsync(Guid eventId, AttendanceDialogue dialogue, Guid? after, string closing, CancellationToken ct)
    {
        var (active, options) = await ActiveAttendanceQuestionsAsync(eventId, ct);
        var next = after is null ? active.FirstOrDefault()
            : active.SkipWhile(x => x.Id != after).Skip(1).FirstOrDefault()
              ?? (active.Any(x => x.Id == after) ? null : active.FirstOrDefault());
        if (next is null) { dialogue.Close(); return closing; }
        dialogue.AskQuestion(next.Id);
        return closing + "\n\n" + QuestionPrompt(next, options);
    }

    private async Task<string> AttendanceQuestionReplyAsync(WhatsAppConnection connection, EventParticipant participant,
        AttendanceDialogue dialogue, string text, DateTime now, CancellationToken ct)
    {
        var (active, options) = await ActiveAttendanceQuestionsAsync(connection.EventId, ct);
        var current = active.FirstOrDefault(x => x.Id == dialogue.QuestionId);
        if (current is null)
            return await AdvanceAttendanceQuestionAsync(connection.EventId, dialogue, dialogue.QuestionId, "ℹ️ Le domande sono cambiate.", ct);
        var choices = options.Where(x => x.QuestionId == current.Id).OrderBy(x => x.Position).ToArray();
        if (!int.TryParse(text, out var number) || number < 1 || number > choices.Length)
            return $"❓ Rispondi con un numero da 1 a {choices.Length}.\n" + QuestionPrompt(current, options);
        var chosen = choices[number - 1];
        var existing = await store.ListAsync<AttendanceAnswer>(connection.EventId, ct);
        foreach (var person in await AttendanceIndividualTargetsAsync(connection.EventId, participant.PersonId, ct))
        {
            var answer = await store.FindAsync<AttendanceAnswer>(connection.EventId,
                existing.FirstOrDefault(x => x.PersonId == person && x.QuestionId == current.Id)?.Id ?? Guid.Empty, ct);
            if (answer is null)
            {
                answer = new(connection.EventId, person, current.Id, chosen.Id, participant.PersonId, now);
                store.Add(answer);
            }
            else if (!answer.Change(chosen.Id, participant.PersonId, now)) continue;
            operations.Audit(connection.EventId, connection.ActorId!.Value, answer.Id, "Attendance", "QuestionAnswered");
        }
        return await AdvanceAttendanceQuestionAsync(connection.EventId, dialogue, current.Id, "🎉 Risposta salvata!", ct);
    }
}
