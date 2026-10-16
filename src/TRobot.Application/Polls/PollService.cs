using System.Globalization;
using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.Polls;

public sealed record PollEdit(Guid OperationId, string Title, DateTime DeadlineLocal, IReadOnlyList<string> Options, int Revision);
public sealed record PollBallot(Guid OperationId, Guid PersonId, Guid OptionId, int Revision, bool Confirmed);
public sealed record PollCount(PollOption Option, int Count);
public sealed record PollPage(Event Event, Poll Poll, IReadOnlyList<PollCount> Counts, IReadOnlyList<LocalParticipation> Eligible,
    IReadOnlyList<PollVote> Votes, OutboundMessage? Publication, OutboundMessage? Result, string PublicationPreview, string ResultPreview, bool Open);
public sealed record PollOverview(Event Event, IReadOnlyList<Poll> Polls);

public sealed class PollService(ILocalStore store, LocalOperations operations, TimeProvider clock)
{
    public async Task<PollOverview> ListAsync(Guid actorId, Guid eventId, CancellationToken ct = default) =>
        new(await AuthorizedEvent(actorId, eventId, ct), (await store.ListAsync<Poll>(eventId, ct)).OrderBy(x => x.DeadlineUtc).ToArray());
    public async Task<PollPage> ReadAsync(Guid actorId, Guid eventId, Guid pollId, CancellationToken ct = default)
    {
        var ev = await AuthorizedEvent(actorId, eventId, ct);
        var poll = await Get(eventId, pollId, ct);
        var options = await Options(eventId, pollId, ct);
        var votes = (await store.ListAsync<PollVote>(eventId, ct)).Where(x => x.PollId == pollId).ToArray();
        var counts = options.Select(x => new PollCount(x, votes.Count(v => v.OptionId == x.Id))).ToArray();
        var publication = poll.PublicationMessageId.HasValue ? await store.FindAsync<OutboundMessage>(eventId, poll.PublicationMessageId.Value, ct) : null;
        var result = poll.ResultMessageId.HasValue ? await store.FindAsync<OutboundMessage>(eventId, poll.ResultMessageId.Value, ct) : null;
        return new(ev, poll, counts, await Eligible(eventId, ct), votes, publication, result, PublicationText(ev, poll, options),
            ResultText(poll, counts), poll.Status == PollStatus.PublicationQueued && publication?.Status == OutboundMessageStatus.Sent && clock.GetUtcNow().UtcDateTime < poll.DeadlineUtc);
    }
    public Task<Guid> SaveAsync(Guid actorId, Guid eventId, Guid? id, PollEdit input, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var deadline = await operations.ToUtcAsync(eventId, input.DeadlineLocal, token) ?? throw new LocalValidationException("Scadenza obbligatoria.");
            if (deadline <= clock.GetUtcNow().UtcDateTime) throw new LocalValidationException("La scadenza deve essere futura.");
            if (id.HasValue)
            {
                var current = await Get(eventId, id.Value, token); LocalOperations.Revision(current, input.Revision);
                // Browser datetime-local fields preserve milliseconds; retain any stored finer ticks on an unchanged value.
                if (deadline.Ticks / TimeSpan.TicksPerMillisecond == current.DeadlineUtc.Ticks / TimeSpan.TicksPerMillisecond)
                    deadline = current.DeadlineUtc;
                if (current.Edit(input.Title, deadline))
                { operations.Audit(eventId, actorId, current.Id, "Polls", "DraftEdited"); await store.SaveAsync(token); }
                return current.Id;
            }
            var candidate = new Poll(eventId, input.OperationId, input.Title, deadline);
            var texts = input.Options.Select(x => x.Trim()).ToArray();
            if (texts.Length is < 2 or > 10 || texts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != texts.Length)
                throw new LocalValidationException("Servono da 2 a 10 opzioni distinte.");
            var options = texts.Select((text, i) => new PollOption(eventId, candidate.Id, i + 1, text)).ToArray();
            var existing = await store.FindAsync<Poll>(eventId, candidate.Id, token);
            if (existing is not null)
            {
                if (existing.Revision == 0 && existing.Title == candidate.Title && existing.DeadlineUtc == deadline
                    && (await Options(eventId, existing.Id, token)).Select(x => x.Text).SequenceEqual(texts)) return existing.Id;
                throw new LocalConflictException("Identificativo già usato o bozza modificata.");
            }
            store.Add(candidate); foreach (var option in options) store.Add(option);
            operations.Audit(eventId, actorId, candidate.Id, "Polls", "DraftCreated"); await store.SaveAsync(token); return candidate.Id;
        }, ct);
    public Task PublishAsync(Guid actorId, Guid eventId, Guid pollId, int revision, bool confirmed, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            Confirm(confirmed); var poll = await Get(eventId, pollId, token); LocalOperations.Revision(poll, revision);
            if (poll.DeadlineUtc <= clock.GetUtcNow().UtcDateTime) throw new LocalValidationException("La scadenza è trascorsa.");
            var ev = await store.EventAsync(eventId, token) ?? throw new LocalAccessException();
            var message = new OutboundMessage(eventId, Guid.NewGuid(), PublicationText(ev, poll, await Options(eventId, pollId, token)), MessageKind.Operational, clock.GetUtcNow().UtcDateTime);
            message.Queue(true); poll.Publish(message.Id); store.Add(message);
            operations.Audit(eventId, actorId, pollId, "Polls", "PublicationQueued"); await store.SaveAsync(token); return true;
        }, ct);
    public Task VoteAsync(Guid actorId, Guid eventId, Guid pollId, PollBallot input, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var poll = await Get(eventId, pollId, token);
            var receipt = await store.FindAsync<PollVoteReceipt>(eventId, input.OperationId, token);
            if (receipt is not null)
            {
                if (receipt.PollId == pollId && receipt.ActorId == actorId && receipt.PersonId == input.PersonId && receipt.OptionId == input.OptionId) return true;
                throw new LocalConflictException("Identificativo voto già utilizzato per altri dati.");
            }
            Confirm(input.Confirmed); LocalOperations.Revision(poll, input.Revision);
            await RequirePublished(poll, token);
            if (clock.GetUtcNow().UtcDateTime >= poll.DeadlineUtc) throw new LocalValidationException("La votazione è scaduta.");
            if (!(await Eligible(eventId, token)).Any(x => x.PersonId == input.PersonId)) throw new LocalValidationException("Può votare solo un adulto confermato di questo evento.");
            if (!(await Options(eventId, pollId, token)).Any(x => x.Id == input.OptionId)) throw new LocalValidationException("Opzione estranea al sondaggio.");
            var row = (await store.ListAsync<PollVote>(eventId, token)).SingleOrDefault(x => x.PollId == pollId && x.PersonId == input.PersonId);
            if (row is null) store.Add(new PollVote(eventId, pollId, input.PersonId, input.OptionId));
            else (await store.FindAsync<PollVote>(eventId, row.Id, token))!.Change(input.OptionId);
            store.Add(new PollVoteReceipt(eventId, pollId, input.OperationId, actorId, input.PersonId, input.OptionId, clock.GetUtcNow().UtcDateTime));
            poll.Touch(); operations.Audit(eventId, actorId, pollId, "Polls", "VoteSimulated"); await store.SaveAsync(token); return true;
        }, ct);
    public Task CloseAsync(Guid actorId, Guid eventId, Guid pollId, int revision, bool confirmed, Guid? decisionOptionId, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            Confirm(confirmed); var poll = await Get(eventId, pollId, token); LocalOperations.Revision(poll, revision); await RequirePublished(poll, token);
            if (decisionOptionId.HasValue && !(await Options(eventId, pollId, token)).Any(x => x.Id == decisionOptionId)) throw new LocalValidationException("Decisione estranea al sondaggio.");
            poll.Close(clock.GetUtcNow().UtcDateTime, decisionOptionId);
            operations.Audit(eventId, actorId, pollId, "Polls", "Closed"); await store.SaveAsync(token); return true;
        }, ct);
    public Task QueueResultAsync(Guid actorId, Guid eventId, Guid pollId, int revision, bool confirmed, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            Confirm(confirmed); var poll = await Get(eventId, pollId, token); LocalOperations.Revision(poll, revision);
            var options = await Options(eventId, pollId, token); var votes = await store.ListAsync<PollVote>(eventId, token);
            var text = ResultText(poll, options.Select(o => new PollCount(o, votes.Count(v => v.PollId == pollId && v.OptionId == o.Id))).ToArray());
            var message = new OutboundMessage(eventId, Guid.NewGuid(), text, MessageKind.Operational, clock.GetUtcNow().UtcDateTime);
            message.Queue(true); poll.QueueResult(message.Id); store.Add(message);
            operations.Audit(eventId, actorId, pollId, "Polls", "ResultQueued"); await store.SaveAsync(token); return true;
        }, ct);
    public Task CancelAsync(Guid actorId, Guid eventId, Guid pollId, int revision, bool confirmed, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            Confirm(confirmed); var poll = await Get(eventId, pollId, token); LocalOperations.Revision(poll, revision);
            if (poll.PublicationMessageId.HasValue)
            {
                var message = await store.FindAsync<OutboundMessage>(eventId, poll.PublicationMessageId.Value, token) ?? throw new LocalAccessException();
                if (message.Status == OutboundMessageStatus.Sent) throw new LocalValidationException("Sondaggio già pubblicato: usa la chiusura per conservare voti e risultati.");
                message.Cancel(true);
            }
            poll.Cancel(); operations.Audit(eventId, actorId, pollId, "Polls", "Cancelled"); await store.SaveAsync(token); return true;
        }, ct);
    private async Task RequirePublished(Poll poll, CancellationToken ct)
    {
        if (poll.Status != PollStatus.PublicationQueued || !poll.PublicationMessageId.HasValue
            || (await store.FindAsync<OutboundMessage>(poll.EventId, poll.PublicationMessageId.Value, ct))?.Status != OutboundMessageStatus.Sent)
            throw new LocalValidationException("Serve la pubblicazione con invio simulato concluso.");
    }
    private async Task<IReadOnlyList<LocalParticipation>> Eligible(Guid eventId, CancellationToken ct)
    {
        var children = (await store.ListAsync<HouseholdMember>(eventId, ct)).Where(x => x.IsActive && x.Kind == HouseholdMemberKind.Child).Select(x => x.PersonId).ToHashSet();
        return (await store.ParticipationsAsync(eventId, ct)).Where(x => x.Status == ParticipationStatus.Confirmed && !children.Contains(x.PersonId)).ToArray();
    }
    private async Task<Event> AuthorizedEvent(Guid actorId, Guid eventId, CancellationToken ct)
    { await operations.AuthorizeAsync(actorId, eventId, ct); return await store.EventAsync(eventId, ct) ?? throw new LocalAccessException(); }
    private async Task<Poll> Get(Guid eventId, Guid id, CancellationToken ct) => await store.FindAsync<Poll>(eventId, id, ct) ?? throw new LocalAccessException();
    private async Task<PollOption[]> Options(Guid eventId, Guid pollId, CancellationToken ct) =>
        (await store.ListAsync<PollOption>(eventId, ct)).Where(x => x.PollId == pollId).OrderBy(x => x.Position).ToArray();
    private static void Confirm(bool value) { if (!value) throw new LocalValidationException("Conferma esplicitamente l'operazione e l'anteprima mostrata."); }
    private static string PublicationText(Event ev, Poll poll, IReadOnlyList<PollOption> options)
    {
        var deadline = TimeZoneInfo.ConvertTimeFromUtc(poll.DeadlineUtc, TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId));
        return $"Sondaggio: {poll.Title}\n" + string.Join("\n", options.Select(x => $"{x.Position}. {x.Text}"))
            + "\nScadenza: " + deadline.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " (" + ev.TimeZoneId + "). Un voto per adulto confermato.";
    }
    private static string ResultText(Poll poll, IReadOnlyList<PollCount> counts) =>
        $"Risultati: {poll.Title}\n" + string.Join("\n", counts.Select(x => $"{x.Option.Position}. {x.Option.Text}: {x.Count}"))
        + $"\nVoti totali: {counts.Sum(x => x.Count)}."
        + (poll.DecisionOptionId.HasValue ? "\nDecisione organizzativa: " + counts.Single(x => x.Option.Id == poll.DecisionOptionId).Option.Text : "\nNessuna decisione ufficiale registrata.");
}
