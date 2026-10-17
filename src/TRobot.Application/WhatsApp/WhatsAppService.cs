using TRobot.Application.Attendance;
using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.WhatsApp;

public sealed class WhatsAppService(ILocalStore store, IWhatsAppStore identities, LocalOperations operations,
    IWhatsAppGateway gateway, WhatsAppGatewayOptions options, TimeProvider clock)
{
    private static readonly SemaphoreSlim DeviceGate = new(1, 1);

    public async Task<WhatsAppPage> ReadAsync(Guid actorId, Guid eventId, CancellationToken ct = default)
    {
        await operations.AuthorizeAsync(actorId, eventId, ct);
        return new(await store.EventAsync(eventId, ct) ?? throw new LocalAccessException(),
            await store.FindAsync<WhatsAppConnection>(eventId, eventId, ct), await store.ParticipantsAsync(eventId, ct),
            await store.ListAsync<WhatsAppIdentityAlias>(eventId, ct),
            (await store.ListAsync<WhatsAppLiveMessage>(eventId, ct)).OrderByDescending(x => x.CreatedAtUtc).Take(30).ToArray(),
            await identities.IsAdministratorAsync(actorId, ct), options.Enabled);
    }

    public async Task<GatewayHealth> HealthAsync(Guid actorId, Guid eventId, CancellationToken ct = default)
    { await RequireAdmin(actorId, eventId, ct); return await gateway.HealthAsync(ct); }
    public async Task<GatewayHealth> ConnectAsync(Guid actorId, Guid eventId, bool confirmed, CancellationToken ct = default)
    {
        await RequireAdmin(actorId, eventId, ct); Confirm(confirmed);
        await DeviceGate.WaitAsync(ct);
        try { await RequireAdmin(actorId, eventId, ct); return await gateway.ConnectAsync(ct); }
        finally { DeviceGate.Release(); }
    }
    public async Task DisconnectAsync(Guid actorId, Guid eventId, bool confirmed, CancellationToken ct = default)
    {
        await RequireAdmin(actorId, eventId, ct); Confirm(confirmed); await DeviceGate.WaitAsync(ct);
        try
        {
            await operations.ChangeAsync(actorId, eventId, async token =>
            {
                await RequireAdmin(actorId, eventId, token);
                var active = await identities.ActiveConnectionAsync(token);
                if (active is not null && active.EventId != eventId) throw new LocalConflictException("Il gateway è attivo per un altro evento. Disattivalo dalla relativa pagina.");
                if (active?.Disable() == true)
                { operations.Audit(eventId, actorId, eventId, "WhatsApp", "Disconnected"); await store.SaveAsync(token); }
                return true;
            }, ct);
            await gateway.DisconnectAsync(ct);
        }
        finally { DeviceGate.Release(); }
    }

    public async Task<IReadOnlyList<GatewayGroup>> GroupsAsync(Guid actorId, Guid eventId, CancellationToken ct = default)
    { await RequireAdmin(actorId, eventId, ct); return await gateway.GroupsAsync(ct); }
    public async Task<IReadOnlyList<GatewayMember>> MembersAsync(Guid actorId, Guid eventId, CancellationToken ct = default)
    {
        await RequireAdmin(actorId, eventId, ct);
        var connection = await RequireConnection(eventId, ct);
        return await gateway.MembersAsync(connection.GroupId!, ct);
    }

    public async Task SelectGroupAsync(Guid actorId, Guid eventId, string groupId, int revision, bool confirmed, CancellationToken ct = default)
    {
        await RequireAdmin(actorId, eventId, ct); Confirm(confirmed);
        if (string.IsNullOrWhiteSpace(groupId) || groupId.Length > 200 || !groupId.EndsWith("@g.us", StringComparison.Ordinal))
            throw new LocalValidationException("Seleziona un gruppo valido.");
        await DeviceGate.WaitAsync(ct);
        try
        {
            await RequireAdmin(actorId, eventId, ct);
            if (!(await gateway.GroupsAsync(ct)).Any(x => x.Id == groupId)) throw new LocalValidationException("Gruppo non disponibile per questo dispositivo.");
            var claimedRevision = await operations.ChangeAsync(actorId, eventId, async token =>
            {
                await RequireAdmin(actorId, eventId, token);
                var active = await identities.ActiveConnectionAsync(token);
                if (active is not null && active.EventId != eventId) throw new LocalConflictException("Un altro evento ha già un gruppo attivo; disattivalo prima.");
                var connection = await store.FindAsync<WhatsAppConnection>(eventId, eventId, token);
                var fresh = connection is null; connection ??= new(eventId); LocalOperations.Revision(connection, revision);
                var disabled = connection.Disable();
                if (fresh) store.Add(connection);
                if (fresh || disabled) { operations.Audit(eventId, actorId, eventId, "WhatsApp", "GroupChangeStarted"); await store.SaveAsync(token); }
                return connection.Revision;
            }, ct);
            await gateway.SetGroupAsync(groupId, ct);
            await operations.ChangeAsync(actorId, eventId, async token =>
            {
                await RequireAdmin(actorId, eventId, token);
                var connection = await store.FindAsync<WhatsAppConnection>(eventId, eventId, token) ?? throw new LocalConflictException("Collegamento modificato.");
                LocalOperations.Revision(connection, claimedRevision);
                if (connection.Enabled) throw new LocalConflictException("Collegamento già attivato.");
                var utc = clock.GetUtcNow().UtcDateTime;
                connection.Activate(groupId, actorId, new DateTime(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc));
                operations.Audit(eventId, actorId, eventId, "WhatsApp", "GroupActivated"); await store.SaveAsync(token); return true;
            }, ct);
        }
        finally { DeviceGate.Release(); }
    }

    public async Task MapAsync(Guid actorId, Guid eventId, string memberId, Guid personId, int connectionRevision, bool confirmed, CancellationToken ct = default)
    {
        await RequireAdmin(actorId, eventId, ct); Confirm(confirmed);
        var connection = await RequireConnection(eventId, ct);
        LocalOperations.Revision(connection, connectionRevision);
        var members = await gateway.MembersAsync(connection.GroupId!, ct);
        var member = members.SingleOrDefault(x => x.Id == memberId) ?? throw new LocalValidationException("Membro non presente nel gruppo attivo.");
        var hashes = HashAliases(new[] { member.Id }.Concat(member.AlternateIds).ToArray());
        await operations.ChangeAsync(actorId, eventId, async token =>
        {
            await RequireAdmin(actorId, eventId, token);
            LocalOperations.Revision(await RequireConnection(eventId, token), connectionRevision);
            if (await identities.ParticipantAsync(eventId, personId, token) is null) throw new LocalValidationException("Scegli un partecipante già esistente in questo evento.");
            var aliases = await store.ListAsync<WhatsAppIdentityAlias>(eventId, token); var changed = false;
            foreach (var hash in hashes)
            {
                var existing = aliases.SingleOrDefault(x => x.SenderHash == hash);
                if (existing is not null && existing.PersonId != personId) throw new LocalConflictException("Identità già associata a un'altra persona: associazione ambigua rifiutata.");
                if (existing is null) { store.Add(new WhatsAppIdentityAlias(eventId, hash, personId)); changed = true; }
                else if (!existing.IsActive) changed |= (await store.FindAsync<WhatsAppIdentityAlias>(eventId, existing.Id, token))!.SetActive(true);
            }
            if (changed) { operations.Audit(eventId, actorId, personId, "WhatsApp", "IdentityMapped"); await store.SaveAsync(token); }
            return true;
        }, ct);
    }

    public async Task<WhatsAppIncomingResult> IncomingAsync(WhatsAppIncoming input, CancellationToken ct = default)
    {
        options.RequireEnabled();
        if (string.IsNullOrWhiteSpace(input.GroupId) || input.GroupId.Length > 200 || !input.GroupId.EndsWith("@g.us", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(input.MessageId) || input.MessageId.Length > 200 || string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > 100)
            return new(false);
        ParticipationStatus status;
        try { status = AttendanceService.Parse(input.Text); }
        catch (AttendanceValidationException) { return new(false); }
        string[] hashes;
        try { hashes = HashAliases(input.SenderIds); } catch (LocalValidationException) { return new(false); }
        DateTime timestamp;
        try { timestamp = DateTimeOffset.FromUnixTimeSeconds(input.TimestampUnixSeconds).UtcDateTime; }
        catch (ArgumentOutOfRangeException) { return new(false); }
        // Discovery is not authorization: repeat every check inside the mutating transaction.
        var active = await identities.ActiveConnectionAsync(ct);
        if (active?.ActorId is not { } actor || active.GroupId != input.GroupId) return new(false);
        return await operations.ChangeAsync(actor, active.EventId, async token =>
        {
            await RequireAdmin(actor, active.EventId, token);
            var connection = await identities.ActiveConnectionAsync(token);
            var now = clock.GetUtcNow().UtcDateTime;
            if (connection is null || connection.EventId != active.EventId || connection.ActorId != actor || connection.GroupId != input.GroupId
                || !connection.ActivatedAtUtc.HasValue || timestamp < connection.ActivatedAtUtc.Value || timestamp < now.AddSeconds(-120) || timestamp > now.AddSeconds(30)) return new WhatsAppIncomingResult(false);
            var matches = (await store.ListAsync<WhatsAppIdentityAlias>(connection.EventId, token)).Where(x => x.IsActive && hashes.Contains(x.SenderHash)).Select(x => x.PersonId).Distinct().ToArray();
            if (matches.Length != 1) return new WhatsAppIncomingResult(false);
            var participant = await identities.ParticipantAsync(connection.EventId, matches[0], token);
            if (participant is null) return new WhatsAppIncomingResult(false);
            var messageHash = options.Fingerprint("message", input.GroupId + ":" + input.MessageId);
            var receipt = await identities.ReceiptAsync(connection.EventId, messageHash, token);
            if (receipt is not null)
            {
                if (receipt.PersonId != participant.PersonId || receipt.Status != status || receipt.ActorId != actor)
                    throw new LocalConflictException("Identificativo messaggio già elaborato con dati diversi.");
                return new WhatsAppIncomingResult(true, true);
            }
            var previous = participant.Status; participant.ChangeStatus(status, now);
            var record = new WhatsAppIncomingReceipt(connection.EventId, messageHash, participant.PersonId, actor, previous, status, now);
            store.Add(record); operations.Audit(connection.EventId, actor, record.Id, "WhatsApp", "PresenceReceived"); await store.SaveAsync(token);
            return new WhatsAppIncomingResult(true, false, "✅");
        }, ct);
    }

    public Task<Guid> DraftAsync(Guid actorId, Guid eventId, Guid operationId, string body, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            options.RequireEnabled(); var connection = await RequireConnection(eventId, token);
            var candidate = new WhatsAppLiveMessage(eventId, operationId, actorId, connection.GroupId!, connection.Revision, body, clock.GetUtcNow().UtcDateTime);
            var previous = await store.FindAsync<WhatsAppLiveMessage>(eventId, operationId, token);
            if (previous is not null)
            {
                if (previous.ActorId == actorId && previous.Body == candidate.Body && previous.GroupId == candidate.GroupId && previous.ConnectionRevision == candidate.ConnectionRevision) return previous.Id;
                throw new LocalConflictException("Identificativo bozza già utilizzato.");
            }
            store.Add(candidate); operations.Audit(eventId, actorId, operationId, "WhatsApp", "LiveDraftCreated"); await store.SaveAsync(token); return candidate.Id;
        }, ct);

    public async Task<WhatsAppLiveMessageStatus> SendAsync(Guid actorId, Guid eventId, Guid id, int revision, bool confirmed, CancellationToken ct = default)
    {
        options.RequireEnabled(); Confirm(confirmed); await DeviceGate.WaitAsync(ct);
        try { return await SendCoreAsync(actorId, eventId, id, revision, confirmed, ct); }
        finally { DeviceGate.Release(); }
    }

    private async Task<WhatsAppLiveMessageStatus> SendCoreAsync(Guid actorId, Guid eventId, Guid id, int revision, bool confirmed, CancellationToken ct)
    {
        options.RequireEnabled(); Confirm(confirmed);
        var claim = await operations.ChangeAsync(actorId, eventId, async token =>
        {
            var message = await store.FindAsync<WhatsAppLiveMessage>(eventId, id, token) ?? throw new LocalAccessException();
            if (message.Status != WhatsAppLiveMessageStatus.Draft) return (Send: false, Message: message);
            LocalOperations.Revision(message, revision); var connection = await RequireConnection(eventId, token);
            if (message.GroupId != connection.GroupId || message.ConnectionRevision != connection.Revision)
                throw new LocalConflictException("Gruppo o collegamento cambiato dopo l'anteprima. Prepara una nuova bozza.");
            message.ClaimSending(); operations.Audit(eventId, actorId, id, "WhatsApp", "LiveSending"); await store.SaveAsync(token);
            return (Send: true, Message: message);
        }, ct);
        if (!claim.Send) return claim.Message.Status;
        var sent = false;
        try { sent = (await gateway.SendAsync(id, claim.Message.GroupId, claim.Message.Body, ct)).Status == "sent"; }
        catch (Exception) { /* Sending is persisted: never retry an ambiguous external operation. */ }
        // Record the transport outcome even if this HTTP request was canceled. Failure leaves
        // Sending visible and non-retriable; no background dispatcher picks it up.
        try
        {
            return await operations.ChangeAsync(actorId, eventId, async token =>
            {
                var message = await store.FindAsync<WhatsAppLiveMessage>(eventId, id, token) ?? throw new LocalAccessException();
                if (message.Status != WhatsAppLiveMessageStatus.Sending) return message.Status;
                message.Complete(sent, clock.GetUtcNow().UtcDateTime);
                operations.Audit(eventId, actorId, id, "WhatsApp", sent ? "LiveSent" : "LiveUncertain"); await store.SaveAsync(token); return message.Status;
            }, CancellationToken.None);
        }
        catch (LocalAccessException) { return WhatsAppLiveMessageStatus.Sending; }
    }

    private async Task RequireAdmin(Guid actorId, Guid eventId, CancellationToken ct)
    { options.RequireEnabled(); await operations.AuthorizeAsync(actorId, eventId, ct); if (!await identities.IsAdministratorAsync(actorId, ct)) throw new LocalAccessException(); }
    private async Task<WhatsAppConnection> RequireConnection(Guid eventId, CancellationToken ct)
    {
        options.RequireEnabled(); var connection = await store.FindAsync<WhatsAppConnection>(eventId, eventId, ct);
        if (connection is null || !connection.Enabled || connection.GroupId is null || connection.ActorId is not { } actor
            || !await store.CanManageAsync(actor, eventId, ct) || !await identities.IsAdministratorAsync(actor, ct))
            throw new LocalValidationException("Nessun gruppo attivo con attivatore autorizzato per questo evento.");
        return connection;
    }
    private string[] HashAliases(IReadOnlyList<string> aliases)
    {
        if (aliases is null || aliases.Count is < 1 or > 4 || aliases.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200
            || !(x.EndsWith("@s.whatsapp.net", StringComparison.Ordinal) || x.EndsWith("@lid", StringComparison.Ordinal))))
            throw new LocalValidationException("Identità WhatsApp non valida.");
        return aliases.Select(x => options.Fingerprint("sender", x)).Distinct().ToArray();
    }
    private static void Confirm(bool confirmed)
    { if (!confirmed) throw new LocalValidationException("Conferma esplicitamente l'operazione WhatsApp."); }
}
