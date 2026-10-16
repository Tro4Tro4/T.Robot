using System.Globalization;
using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.Messaging;

public sealed record MessageEdit(Guid OperationId, string Body, MessageKind Kind, DateTime? ScheduledLocal, int Revision);
public sealed record SettingsEdit(bool Paused, int QuietStart, int QuietEnd, int Cap, int Sarcasm, int Revision);
public sealed record MessageOverview(Event Event, IReadOnlyList<OutboundMessage> Messages, BotSettings Settings);
public sealed record TickResult(int Sent, bool SummaryCreated);

public sealed class MessageService(ILocalStore store, LocalOperations operations, TimeProvider clock)
{
    public async Task<MessageOverview> ListAsync(Guid actorId, Guid eventId, CancellationToken ct = default)
    {
        var messages = await operations.ReadAsync<OutboundMessage>(actorId, eventId, ct);
        return new(await store.EventAsync(eventId, ct) ?? throw new LocalAccessException(),
            messages.OrderByDescending(x => x.ScheduledAtUtc).ThenBy(x => x.Id).ToArray(),
            await store.FindAsync<BotSettings>(eventId, eventId, ct) ?? new BotSettings(eventId));
    }
    public Task<Guid> SaveDraftAsync(Guid actorId, Guid eventId, Guid? id, MessageEdit input, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            if (id.HasValue)
            {
                var existing = await store.FindAsync<OutboundMessage>(eventId, id.Value, token) ?? throw new LocalAccessException();
                LocalOperations.Revision(existing, input.Revision);
                var now = clock.GetUtcNow().UtcDateTime;
                var scheduled = await operations.ToUtcAsync(eventId, input.ScheduledLocal, token)
                    ?? (existing.ScheduledAtUtc > now ? now : existing.ScheduledAtUtc);
                // HTML datetime-local supports milliseconds; keep original residual ticks
                // when the organizer posts the same displayed instant.
                if (input.ScheduledLocal.HasValue && scheduled.Ticks ==
                    existing.ScheduledAtUtc.Ticks - existing.ScheduledAtUtc.Ticks % TimeSpan.TicksPerMillisecond)
                    scheduled = existing.ScheduledAtUtc;
                if (existing.Edit(input.Body, input.Kind, scheduled))
                { operations.Audit(eventId, actorId, existing.Id, "Messages", "DraftEdited"); await store.SaveAsync(token); }
                return existing.Id;
            }
            if (input.OperationId == Guid.Empty) throw new LocalValidationException("Identificativo operazione obbligatorio.");
            var candidate = new OutboundMessage(eventId, input.OperationId, input.Body, input.Kind,
                await operations.ToUtcAsync(eventId, input.ScheduledLocal, token) ?? clock.GetUtcNow().UtcDateTime);
            var replay = await store.FindAsync<OutboundMessage>(eventId, candidate.Id, token);
            if (replay is not null)
            {
                if (replay.Revision == 0 && replay.Body == candidate.Body && replay.Kind == candidate.Kind
                    && (!input.ScheduledLocal.HasValue || replay.ScheduledAtUtc == candidate.ScheduledAtUtc)) return replay.Id;
                throw new LocalConflictException("Identificativo già usato per un altro contenuto o messaggio modificato.");
            }
            store.Add(candidate); operations.Audit(eventId, actorId, candidate.Id, "Messages", "DraftCreated");
            await store.SaveAsync(token); return candidate.Id;
        }, ct);
    public Task<bool> TransitionAsync(Guid actorId, Guid eventId, Guid id, int revision, bool confirmed, bool cancel,
        CancellationToken ct = default) => operations.ChangeAsync(actorId, eventId, async token =>
    {
        var message = await store.FindAsync<OutboundMessage>(eventId, id, token) ?? throw new LocalAccessException();
        LocalOperations.Revision(message, revision);
        var changed = cancel ? message.Cancel(confirmed) : message.Queue(confirmed);
        if (changed) { operations.Audit(eventId, actorId, id, "Messages", cancel ? "Cancelled" : "Queued"); await store.SaveAsync(token); }
        return changed;
    }, ct);
    public Task<bool> ConfigureAsync(Guid actorId, Guid eventId, SettingsEdit input, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var settings = await store.FindAsync<BotSettings>(eventId, eventId, token);
            var fresh = settings is null; settings ??= new BotSettings(eventId);
            LocalOperations.Revision(settings, input.Revision);
            var changed = settings.Update(input.Paused, input.QuietStart, input.QuietEnd, input.Cap, input.Sarcasm);
            if (fresh || changed)
            {
                if (fresh) store.Add(settings);
                operations.Audit(eventId, actorId, eventId, "Messages", "SettingsChanged"); await store.SaveAsync(token);
            }
            return fresh || changed;
        }, ct);
    public Task<TickResult> TickAsync(Guid actorId, Guid eventId, bool confirmed, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            if (!confirmed) throw new LocalValidationException("Conferma la simulazione di invio.");
            var ev = await store.EventAsync(eventId, token) ?? throw new LocalAccessException();
            var settings = await store.FindAsync<BotSettings>(eventId, eventId, token);
            var fresh = settings is null; settings ??= new BotSettings(eventId);
            if (settings.Paused) return new TickResult(0, false);
            var now = clock.GetUtcNow().UtcDateTime;
            var zone = TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId);
            // Lists are read-only detached snapshots. Reload tracked rows before state transitions,
            // and share those same instances with the per-tick daily-cap calculation.
            var messages = new List<OutboundMessage>();
            foreach (var row in await store.ListAsync<OutboundMessage>(eventId, token))
                messages.Add(await store.FindAsync<OutboundMessage>(eventId, row.Id, token) ?? throw new LocalConflictException("Messaggio non più disponibile."));
            var interval = SchedulePolicy.SummaryIntervalDays(now, ev);
            var created = false;
            // Generate deterministic aggregate text only. No names, balances, free-form event details or AI.
            if (interval.HasValue && (!settings.LastSummaryAtUtc.HasValue || now >= settings.LastSummaryAtUtc.Value.AddDays(interval.Value))
                && !messages.Any(x => x.Kind == MessageKind.Summary && x.Status == OutboundMessageStatus.Queued))
            {
                var snapshot = await store.SnapshotAsync(eventId, token);
                var fingerprint = string.Create(CultureInfo.InvariantCulture,
                    $"{snapshot.Confirmed}/{snapshot.Undecided}/{snapshot.Absent}/{snapshot.NoResponse}/{snapshot.OpenTasks}/{snapshot.OpenPolls}/{snapshot.ReadyMenu}");
                if (fingerprint != settings.LastSummaryFingerprint)
                {
                    var body = string.Create(CultureInfo.InvariantCulture,
                        $"Riepilogo evento: confermati {snapshot.Confirmed}, indecisi {snapshot.Undecided}, assenti {snapshot.Absent}, senza risposta {snapshot.NoResponse}. Attività aperte: {snapshot.OpenTasks}. Sondaggi aperti: {snapshot.OpenPolls}. Voci menu pronte: {snapshot.ReadyMenu}.");
                    var summary = new OutboundMessage(eventId, Guid.NewGuid(), body, MessageKind.Summary, now);
                    summary.SetSummaryFingerprint(fingerprint); summary.Queue(true);
                    messages.Add(summary); store.Add(summary); operations.Audit(eventId, actorId, summary.Id, "Messages", "SummaryQueued"); created = true;
                }
            }
            var sent = 0;
            foreach (var message in messages.OrderBy(x => x.ScheduledAtUtc).ThenBy(x => x.Id))
            {
                // The summary is skipped on event day even when queued during an earlier tick.
                if (message.Kind == MessageKind.Summary && !interval.HasValue) continue;
                if (!SchedulePolicy.CanSend(message, settings, now, zone, messages)) continue;
                message.MarkSent(now); sent++;
                if (message.SummaryFingerprint is not null) settings.RecordSummary(now, message.SummaryFingerprint);
                operations.Audit(eventId, actorId, message.Id, "Messages", "SentSimulated");
            }
            if (created || sent > 0)
            {
                if (fresh) store.Add(settings);
                await store.SaveAsync(token);
            }
            return new TickResult(sent, created);
        }, ct);
}
