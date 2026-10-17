using System.Text.Json;
using TRobot.Application.Local;
using TRobot.Application.Simulation;
using TRobot.Domain;

namespace TRobot.Application.WhatsApp;

public sealed partial class WhatsAppService
{
    public const string RealHelp = "🤖 *T.Robot · Ci sei a Capodanno?*\n\n"
        + "🎉 *Ci sono!*\n!presenza si\n"
        + "Ti faccio una domanda alla volta, tu rispondi qui nel gruppo:\n\n"
        + "📅 *Quando arrivi?*\nScrivi la data, per esempio 30/12 oppure 30/12/2026.\n\n"
        + "🧳 *Quanti giorni resti?*\nScrivi solo il numero, da 1 a 31. Contiamo anche il giorno di arrivo!\n"
        + "Esempio: arrivo 30/12 e 4 giorni = fino al 02/01, per 3 notti.\n\n"
        + "🏡 *Tutta la famiglia o missione in solitaria?*\n"
        + "Se sei un adulto del nucleo, ti chiedo se confermare le stesse date per tutti:\n"
        + "• sì = tutta la famiglia;\n• no = soltanto tu.\n"
        + "Se compili per te, basta confermare il tuo riepilogo con sì. I dettagli si salvano alla conferma finale.\n\n"
        + "🤔 *Devo ancora decidere*\n!presenza forse\n\n"
        + "😢 *Non riesco a venire*\n!presenza no\n"
        + "Forse e no aggiornano soltanto la tua presenza.\n\n"
        + "✏️ Per correggere le date, riparti da !presenza si. Hai 15 minuti per completare le risposte.\n\n"
        + "Tu prepara la valigia: al conto dei giorni ci penso io. 😎";
    private const string UnapprovedList = "⏳ *Elenco in attesa*\nGli organizzatori devono approvare i contenuti aggiornati dal sito. Riprova dopo l'approvazione.";

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
        if (command == "!aiuto") return parts.Length == 1 ? RealHelp : "ℹ️ Sintassi:\n!aiuto";
        int page;
        try { page = SimulationReplies.ListPage(parts); }
        catch (LocalValidationException) { return $"ℹ️ Sintassi:\n{command} [pagina]\nIndica una pagina intera da 1 a 100000."; }
        if (command == "!riepilogo") return await AttendanceSummaryReplyAsync(connection.EventId, page, ct);
        var kind = command == "!menu" ? WhatsAppListKind.Menu : WhatsAppListKind.Todo;
        var pages = await ListPagesAsync(connection.EventId, kind, ct);
        if (!await IsApprovedAsync(connection, kind, ListFingerprint(connection, kind, pages), ct)) return UnapprovedList;
        return page <= pages.Count ? pages[page - 1] : $"ℹ️ Pagina inesistente.\nDisponibili {pages.Count} pagine. Riparti da:\n{command} 1";
    }

    private string ListFingerprint(WhatsAppConnection connection, WhatsAppListKind kind, IReadOnlyList<string> pages) =>
        options.Fingerprint("lists-v1", JsonSerializer.Serialize(new { connection.EventId, connection.GroupId, connection.Revision, Kind = kind, Pages = pages }));

    private async Task<bool> IsApprovedAsync(WhatsAppConnection connection, WhatsAppListKind kind, string fingerprint, CancellationToken ct) =>
        (await store.ListAsync<WhatsAppListApproval>(connection.EventId, ct)).Any(x => x.Kind == kind && x.GroupId == connection.GroupId
            && x.ConnectionRevision == connection.Revision && x.Fingerprint == fingerprint);

    private async Task<IReadOnlyList<string>> ListPagesAsync(Guid eventId, WhatsAppListKind kind, CancellationToken ct)
    {
        IReadOnlyList<string> pages = kind switch
        {
            WhatsAppListKind.Menu => OperationalListText.Menu(await store.ListAsync<MenuItem>(eventId, ct)),
            WhatsAppListKind.Todo => OperationalListText.Todo(await store.ListAsync<OrganizationTask>(eventId, ct)),
            _ => throw new LocalValidationException("Tipo elenco non valido.")
        };
        if (pages.Any(x => x.Length > 2000)) throw new LocalValidationException("Elenco troppo lungo per la risposta WhatsApp.");
        return pages;
    }
}
