using System.Globalization;
using System.Text.Json;
using TRobot.Application.Local;
using TRobot.Application.Simulation;
using TRobot.Domain;

namespace TRobot.Application.WhatsApp;

public sealed partial class WhatsAppService
{
    public const string RealHelp = "Comandi attivi: !presenza si|forse|no; !menu [pagina]; !todo [pagina]; !porto <rif>; !prendo <rif>; !fatto <rif>; !conferma <codice>; !aiuto. Gli impegni richiedono conferma entro cinque minuti.";
    private const string UnapprovedList = "Elenco non disponibile: gli organizzatori devono approvare i contenuti aggiornati dal sito.";

    public async Task<WhatsAppListPreview> PreviewListAsync(Guid actorId, Guid eventId, WhatsAppListKind kind, CancellationToken ct = default)
    {
        await operations.AuthorizeAsync(actorId, eventId, ct);
        var connection = await RequireConnection(eventId, ct);
        var groupId = connection.GroupId!; var revision = connection.Revision;
        var health = await gateway.HealthAsync(ct);
        if (health.Status != "connected" || health.ActiveGroupId != groupId)
            throw new LocalValidationException("Collega il dispositivo e verifica il gruppo attivo prima dell'anteprima.");
        var matches = (await gateway.GroupsAsync(ct)).Where(x => x.Id == groupId).Take(2).ToArray();
        if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].Name) || matches[0].Name.Length > 200)
            throw new LocalValidationException("Nome del gruppo destinatario non verificabile.");
        return await operations.ChangeAsync(actorId, eventId, async token =>
        {
            var current = await RequireConnection(eventId, token);
            if (current.GroupId != groupId || current.Revision != revision) throw new LocalConflictException("Collegamento modificato: ricarica l'anteprima.");
            var pages = await ListPagesAsync(eventId, kind, token);
            var fingerprint = ListFingerprint(current, kind, pages);
            return new WhatsAppListPreview(eventId, kind, groupId, revision, matches[0].Name.Trim(), pages, fingerprint,
                await IsApprovedAsync(current, kind, fingerprint, token));
        }, ct);
    }

    public async Task ApproveListAsync(Guid actorId, Guid eventId, Guid operationId, WhatsAppListKind kind,
        string groupId, int connectionRevision, string fingerprint, bool confirmed, CancellationToken ct = default)
    {
        Confirm(confirmed);
        await DeviceGate.WaitAsync(ct);
        try
        {
            // Revalidate metadata as well as authorization; approval itself never sends.
            var preview = await PreviewListAsync(actorId, eventId, kind, ct);
            if (preview.GroupId != groupId || preview.ConnectionRevision != connectionRevision || preview.Fingerprint != fingerprint)
                throw new LocalConflictException("Contenuti o collegamento modificati: ricarica tutte le pagine prima di approvare.");
            await operations.ChangeAsync(actorId, eventId, async token =>
            {
                var connection = await RequireConnection(eventId, token);
                var pages = await ListPagesAsync(eventId, kind, token);
                if (connection.GroupId != groupId || connection.Revision != connectionRevision || ListFingerprint(connection, kind, pages) != fingerprint)
                    throw new LocalConflictException("Contenuti o collegamento modificati: ricarica l'anteprima.");
                var prior = await store.FindAsync<WhatsAppListApproval>(eventId, operationId, token);
                if (prior is not null)
                {
                    if (prior.ActorId == actorId && prior.Kind == kind && prior.Fingerprint == fingerprint && prior.GroupId == groupId && prior.ConnectionRevision == connectionRevision) return true;
                    throw new LocalConflictException("Identificativo approvazione già utilizzato.");
                }
                store.Add(new WhatsAppListApproval(eventId, operationId, actorId, kind, fingerprint, groupId, connectionRevision, clock.GetUtcNow().UtcDateTime));
                operations.Audit(eventId, actorId, operationId, "WhatsApp", "ListApproved"); await store.SaveAsync(token); return true;
            }, ct);
        }
        finally { DeviceGate.Release(); }
    }

    private async Task<string> InformationReplyAsync(WhatsAppConnection connection, string[] parts, CancellationToken ct)
    {
        var command = parts[0].ToLowerInvariant();
        if (command == "!aiuto") return parts.Length == 1 ? RealHelp : "Sintassi: !aiuto.";
        int page;
        try { page = SimulationReplies.ListPage(parts); }
        catch (LocalValidationException) { return $"Sintassi: {command} [pagina intera da 1 a 100000]."; }
        var kind = command == "!menu" ? WhatsAppListKind.Menu : WhatsAppListKind.Todo;
        var pages = await ListPagesAsync(connection.EventId, kind, ct);
        if (!await IsApprovedAsync(connection, kind, ListFingerprint(connection, kind, pages), ct)) return UnapprovedList;
        return page <= pages.Count ? pages[page - 1] : $"Pagina inesistente: disponibili {pages.Count} pagine. Usa {command} 1.";
    }

    private string ListFingerprint(WhatsAppConnection connection, WhatsAppListKind kind, IReadOnlyList<string> pages) =>
        options.Fingerprint("lists-v1", JsonSerializer.Serialize(new { connection.EventId, connection.GroupId, connection.Revision, Kind = kind, Pages = pages }));

    private async Task<bool> IsApprovedAsync(WhatsAppConnection connection, WhatsAppListKind kind, string fingerprint, CancellationToken ct) =>
        (await store.ListAsync<WhatsAppListApproval>(connection.EventId, ct)).Any(x => x.Kind == kind && x.GroupId == connection.GroupId
            && x.ConnectionRevision == connection.Revision && x.Fingerprint == fingerprint);

    private async Task<IReadOnlyList<string>> ListPagesAsync(Guid eventId, WhatsAppListKind kind, CancellationToken ct)
    {
        string heading, command; string[] lines;
        if (kind == WhatsAppListKind.Menu)
        {
            var items = (await store.ListAsync<MenuItem>(eventId, ct)).Where(x => x.Availability != MenuAvailability.Cancelled)
                .OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Id).ToArray();
            heading = $"Menu: {items.Length} voci attive; {items.Count(x => x.Availability == MenuAvailability.Ready)} pronte, {items.Count(x => x.Availability == MenuAvailability.Assigned)} assegnate, {items.Count(x => x.Availability == MenuAvailability.Planned)} da organizzare.";
            command = "!menu";
            lines = items.Select(x => $"{SimulationReferences.Short(x.Id)} {SimulationReplies.OneLine(x.Name)} — {x.Quantity.ToString("0.###", CultureInfo.InvariantCulture)} {SimulationReplies.OneLine(x.Unit)} — {x.Availability switch { MenuAvailability.Ready => "Pronto", MenuAvailability.Assigned => "Assegnato", _ => "Da organizzare" }}").ToArray();
        }
        else if (kind == WhatsAppListKind.Todo)
        {
            var tasks = await store.ListAsync<OrganizationTask>(eventId, ct);
            heading = $"Attività: {tasks.Count(x => x.Status == OrganizationTaskStatus.Todo)} da iniziare, {tasks.Count(x => x.Status == OrganizationTaskStatus.InProgress)} in corso, {tasks.Count(x => x.Status == OrganizationTaskStatus.Completed)} completate.";
            command = "!todo";
            lines = tasks.Where(x => x.Status is OrganizationTaskStatus.Todo or OrganizationTaskStatus.InProgress)
                .OrderBy(x => x.Title, StringComparer.Ordinal).ThenBy(x => x.Id)
                .Select(x => $"{SimulationReferences.Short(x.Id)} {SimulationReplies.OneLine(x.Title)} — {(x.Status == OrganizationTaskStatus.Todo ? "Da iniziare" : "In corso")}").ToArray();
        }
        else throw new LocalValidationException("Tipo elenco non valido.");
        var pages = Enumerable.Range(1, Math.Max(1, (lines.Length + 4) / 5))
            .Select(page => heading + SimulationReplies.PageText(lines, page, command, x => x)).ToArray();
        if (pages.Any(x => x.Length > 2000)) throw new LocalValidationException("Elenco troppo lungo per la risposta WhatsApp.");
        return pages;
    }
}
