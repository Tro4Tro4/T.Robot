using TRobot.Application.Local;
using TRobot.Application.WhatsApp;
using TRobot.Domain;

namespace TRobot.Application.Messaging;

/// <summary>Hint for discovery. Every send re-checks settings, access and the active group inside its own transaction.</summary>
public interface IOutboxDispatchStore
{
    Task<IReadOnlyList<Guid>> DispatchCandidatesAsync(DateTime nowUtc, DateTime staleBeforeUtc, CancellationToken ct = default);
}

public sealed record DispatchOutcome(int Sent, int Failed, int Uncertain, int Skipped, int Recovered)
{
    public static DispatchOutcome Empty { get; } = new(0, 0, 0, 0, 0);
    public static DispatchOutcome operator +(DispatchOutcome left, DispatchOutcome right) => new(
        left.Sent + right.Sent, left.Failed + right.Failed, left.Uncertain + right.Uncertain,
        left.Skipped + right.Skipped, left.Recovered + right.Recovered);
    public int Attempted => Sent + Failed + Uncertain;
}

/// <summary>Live panel diagnostics. It never exposes QR data or group metadata.</summary>
public sealed record DeliveryStatus(bool GatewayEnabled, bool GatewayReachable, string GatewayState,
    string? ActiveGroupId, bool GroupMatches)
{
    public bool Ready => GatewayEnabled && GatewayReachable && GatewayState == "connected" && GroupMatches;
}

/// <summary>
/// Real WhatsApp delivery for the organizer outbox. It deliberately has no actor parameter:
/// it runs from a background worker and re-validates the organizer who confirmed each message.
/// </summary>
public sealed class MessageDispatchService(
    ILocalStore store,
    IWhatsAppGateway gateway,
    WhatsAppGatewayOptions options,
    IWhatsAppStore whatsApp,
    LocalOperations operations,
    TimeProvider clock)
{
    // A claim older than this is considered interrupted. It is marked uncertain, never retried.
    public static readonly TimeSpan StaleClaim = TimeSpan.FromMinutes(5);
    // Bounded batch per event and cycle: protects against accidental mass confirmation.
    private const int MaxPerPass = 20;
    private const string StaleReason = "Esito non registrato: processo interrotto durante l'invio. Verificare nel gruppo prima di decidere.";
    private const string AmbiguousReason = "Esito non determinabile: timeout o risposta mancante dal gateway. Verificare nel gruppo.";
    private const string RejectedReason = "Il gateway ha rifiutato la richiesta prima dell'invio.";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public bool GatewayEnabled => options.Enabled;

    public async Task<string?> ActiveGroupAsync(Guid eventId, CancellationToken ct = default)
    {
        var connection = await store.FindAsync<WhatsAppConnection>(eventId, eventId, ct);
        if (connection is null || !connection.Enabled || connection.GroupId is null || connection.ActorId is not { } activator)
            return null;
        if (!await store.CanManageAsync(activator, eventId, ct) || !await whatsApp.IsAdministratorAsync(activator, ct))
            return null;
        return connection.GroupId;
    }

    public async Task<string> RequireActiveGroupAsync(Guid eventId, CancellationToken ct = default)
    {
        options.RequireEnabled();
        return await ActiveGroupAsync(eventId, ct)
            ?? throw new LocalValidationException("Nessun gruppo WhatsApp attivo con attivatore autorizzato per questo evento.");
    }

    public async Task<DispatchOutcome> DispatchEventAsync(Guid eventId, CancellationToken ct = default)
    {
        if (!GatewayReady) return DispatchOutcome.Empty;
        var claims = new List<Claim>();
        int skipped = 0, recovered = 0;
        await Gate.WaitAsync(ct);
        try
        {
            (claims, skipped, recovered) = await ClaimAsync(eventId, ct);
            if (claims.Count == 0) return new DispatchOutcome(0, 0, 0, skipped, recovered);
            var results = await SendAsync(claims);
            var outcome = await RecordAsync(eventId, results);
            return outcome + new DispatchOutcome(0, 0, 0, skipped, recovered);
        }
        finally { Gate.Release(); }
    }

    private bool GatewayReady
    {
        get
        {
            if (!options.Enabled) return false;
            try { options.RequireEnabled(); return true; }
            catch (LocalValidationException) { return false; }
        }
    }

    /// <summary>Read-only live check. It never changes connection state and caps the wait to 3 seconds.</summary>
    public async Task<DeliveryStatus> StatusAsync(Guid eventId, CancellationToken ct = default)
    {
        var group = await ActiveGroupAsync(eventId, ct);
        if (!options.Enabled) return new(false, false, "disabled", group, false);
        try { options.RequireEnabled(); }
        catch (LocalValidationException) { return new(true, false, "invalid", group, false); }
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            var health = await gateway.HealthAsync(linked.Token);
            var state = health.Status is "connected" or "qr" or "connecting" or "disconnected" ? health.Status : "unknown";
            return new(true, true, state, group, state == "connected" && group is not null && health.ActiveGroupId == group);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return new(true, false, "unreachable", group, false); }
    }

    private sealed record Claim(Guid Id, string GroupId, string Body);
    private enum ResultKind { Sent, Failed, Uncertain }
    private sealed record Result(Guid Id, ResultKind Kind, string Reason);

    private async Task<(List<Claim> Claims, int Skipped, int Recovered)> ClaimAsync(Guid eventId, CancellationToken ct) =>
        await store.TransactionAsync(async token =>
        {
            var ev = await store.EventAsync(eventId, token);
            if (ev is null) return (new List<Claim>(), 0, 0);
            var settings = await store.FindAsync<BotSettings>(eventId, eventId, token) ?? new BotSettings(eventId);
            // Detached lists cannot be saved: reload the tracked instances before any transition.
            var messages = new List<OutboundMessage>();
            foreach (var row in await store.ListAsync<OutboundMessage>(eventId, token))
                messages.Add(await store.FindAsync<OutboundMessage>(eventId, row.Id, token)
                    ?? throw new LocalConflictException("Messaggio non più disponibile."));
            var now = clock.GetUtcNow().UtcDateTime;
            var recovered = 0;
            foreach (var stale in messages.Where(x => x.DeliveryMode == MessageDeliveryMode.WhatsApp
                && x.Status == OutboundMessageStatus.Sending && x.LastAttemptAtUtc <= now - StaleClaim))
            {
                stale.ConfirmUncertain(StaleReason, now);
                operations.Audit(eventId, stale.ConfirmedByActorId!.Value, stale.Id, "Messages", "UncertainRecovered");
                recovered++;
            }
            if (settings.Paused)
            {
                if (recovered > 0) await store.SaveAsync(token);
                return (new List<Claim>(), 0, recovered);
            }
            var zone = TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId);
            var group = await ActiveGroupAsync(eventId, token);
            var claims = new List<Claim>();
            var skipped = 0;
            foreach (var message in messages.Where(x => x.DeliveryMode == MessageDeliveryMode.WhatsApp
                && x.Status == OutboundMessageStatus.Queued)
                .Where(x => x.ScheduledAtUtc <= now).OrderBy(x => x.ScheduledAtUtc).ThenBy(x => x.Id))
            {
                if (claims.Count >= MaxPerPass) { skipped++; continue; }
                if (group is null || message.TargetGroupId != group
                    || message.ConfirmedByActorId is not { } confirmer
                    || !await store.CanManageAsync(confirmer, eventId, token)
                    || !SchedulePolicy.AllowsDispatch(message, settings, now, zone, messages))
                { skipped++; continue; }
                message.ClaimSending(now);
                operations.Audit(eventId, confirmer, message.Id, "Messages", "Sending");
                claims.Add(new Claim(message.Id, group, message.Body));
            }
            if (claims.Count > 0 || recovered > 0) await store.SaveAsync(token);
            return (claims, skipped, recovered);
        }, ct);

    private async Task<List<Result>> SendAsync(List<Claim> claims)
    {
        var results = new List<Result>(claims.Count);
        foreach (var claim in claims)
        {
            // Deliberately not tied to the worker's stopping token: an interrupted HTTP call would make
            // the outcome ambiguous, so the request is allowed to finish within the client timeout.
            try
            {
                var response = await gateway.SendAsync(claim.Id, claim.GroupId, claim.Body, CancellationToken.None);
                results.Add(new(claim.Id, response.Status == "sent" ? ResultKind.Sent : ResultKind.Uncertain, AmbiguousReason));
            }
            catch (WhatsAppGatewayRejectedException rejection)
            {
                results.Add(new(claim.Id, ResultKind.Failed, rejection.Code is { } code ? $"{RejectedReason} Codice: {code}." : RejectedReason));
            }
            catch (Exception)
            {
                // Network errors, timeouts and 5xx are ambiguous: the gateway may have received the request.
                results.Add(new(claim.Id, ResultKind.Uncertain, AmbiguousReason));
            }
        }
        return results;
    }

    private async Task<DispatchOutcome> RecordAsync(Guid eventId, List<Result> results)
    {
        if (results.Count == 0) return DispatchOutcome.Empty;
        // Outcome persistence must survive request aborts; a crash instead leaves a stale claim for recovery.
        return await store.TransactionAsync(async token =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            int sent = 0, failed = 0, uncertain = 0;
            foreach (var result in results)
            {
                var message = await store.FindAsync<OutboundMessage>(eventId, result.Id, token);
                if (message is null || message.Status != OutboundMessageStatus.Sending) continue;
                var actor = message.ConfirmedByActorId!.Value;
                switch (result.Kind)
                {
                    case ResultKind.Sent:
                        message.ConfirmSent(now); operations.Audit(eventId, actor, message.Id, "Messages", "Sent"); sent++; break;
                    case ResultKind.Failed:
                        message.Fail(result.Reason, now); operations.Audit(eventId, actor, message.Id, "Messages", "Failed"); failed++; break;
                    default:
                        message.ConfirmUncertain(result.Reason, now); operations.Audit(eventId, actor, message.Id, "Messages", "Uncertain"); uncertain++; break;
                }
            }
            if (sent + failed + uncertain > 0) await store.SaveAsync(token);
            return new DispatchOutcome(sent, failed, uncertain, 0, 0);
        }, CancellationToken.None);
    }
}
