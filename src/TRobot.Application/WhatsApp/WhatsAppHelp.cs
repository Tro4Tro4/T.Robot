using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.WhatsApp;

public sealed partial class WhatsAppService
{
    public const string PresenceHelp = "🤖 *T.Robot · Ci sei a Capodanno?*\n\n"
        + "🎉 *Ci sono!*\n!presenza si\n"
        + "Ti faccio una domanda alla volta, tu rispondi qui nel gruppo:\n\n"
        + "📅 *Quando arrivi?*\nScrivi la data, per esempio 30/12 oppure 30/12/2026.\n\n"
        + "🧳 *Quanti giorni resti?*\nScrivi solo il numero, da 1 a 31. Contiamo anche il giorno di arrivo!\n"
        + "Esempio: arrivo 30/12 e 4 giorni = fino al 02/01, per 3 notti.\n\n"
        + "🏡 *Tutta la famiglia o missione in solitaria?*\n"
        + "Se sei un adulto del nucleo, ti chiedo se confermare le stesse date per tutti:\n"
        + "• sì = tutta la famiglia;\n• no = tu e i bambini collegati a te, con le tue date.\n"
        + "Se compili per te, basta confermare il tuo riepilogo con sì. I dettagli si salvano alla conferma finale.\n\n"
        + "🤔 *Devo ancora decidere*\n!presenza forse\n\n"
        + "😢 *Non riesco a venire*\n!presenza no\n"
        + "Forse e no cambiano subito la tua presenza. Se sei un adulto con altri partecipanti nel nucleo, ti chiedo se vale per tutti:\n"
        + "• sì = tutta la famiglia;\n• no = tu e i bambini collegati a te;\n• !annulla = solo tu.\n\n"
        + "✏️ *Vuoi correggere le date?*\n!presenza modifica (serve la presenza confermata)\nSe le date sono già registrate scegli 1 per l'arrivo, 2 per la durata, 3 per entrambi; altrimenti ti chiedo l'arrivo. Hai 15 minuti per completare le risposte.\n\n"
        + "Tu prepara la valigia: al conto dei giorni ci penso io. 😎";

    private const string HelpTitle = "🤖 *T.Robot · Aiuto*\nScrivi !aiuto seguito dalla voce:\n\n";
    private const string HelpNotFound = "🤖 Voce non trovata o non attiva.\n\n";
    private const string SummaryHelp = "📊 *Riepilogo presenze*\n!riepilogo [pagina]\nMostra quanti hanno confermato, adulti e bambini, indecisi, assenti e senza risposta, con i conteggi per nucleo.\n\n"
        + "!riepilogo gg/mm[/aaaa] [pagina]\nStesso riepilogo per un giorno preciso, per esempio !riepilogo 31/12. Conta i confermati il cui soggiorno comprende quel giorno. Indecisi, assenti e senza risposta restano complessivi.";
    private const string MealsHelpProposalsOpen = "🍝 *Pasti*\n\n*Proporre un piatto*\n!proponi\nTi scrivo in privato: scegli giorno, pasto e piatto e dici se ti offri di cucinarlo. "
        + "Serve la presenza confermata con le date del soggiorno e puoi proporre solo nei giorni in cui ci sei. "
        + "Hai cinque minuti dall'avvio; !annulla interrompe. La raccolta resta aperta finché gli organizzatori non la chiudono.\n\n*Votare i piatti*\n";
    private const string MealsHelpProposalsClosed = "🍝 *Pasti*\n\n*Proporre un piatto*\nLa raccolta delle proposte è chiusa.\n\n*Votare i piatti*\n";
    private const string MealsHelpVoteOpen = "!preferenze\nTi scrivo in privato: scegli per chi (te o un bambino collegato), poi giorno e pasto, poi i piatti. "
        + "Si vota nei giorni del soggiorno confermato di chi riceve il voto. Puoi sceglierne più di uno; controlli il riepilogo e salvi con !conferma. Hai cinque minuti dall'avvio; !annulla scarta le modifiche.";
    private const string MealsHelpVoteClosed = "!preferenze\nIl voto è chiuso: ti scrivo in privato i risultati finali, scegliendo persona, giorno e pasto.";
    private const string MealsHelpVoteNotYet = "!preferenze sarà disponibile quando gli organizzatori apriranno il voto.";

    private readonly record struct HelpState(bool Menu, bool Todo, MealVotingState? Vote, bool ProposalsOpen)
    {
        public bool Lists => Menu || Todo;
        public string VoteHelp => Vote switch { MealVotingState.Open => MealsHelpVoteOpen, MealVotingState.Closed => MealsHelpVoteClosed, _ => MealsHelpVoteNotYet };
    }

    private async Task<string> HelpReplyAsync(WhatsAppConnection connection, string[] parts, CancellationToken ct)
    {
        var state = await HelpStateAsync(connection, ct);
        var topic = parts.Length == 2 ? parts[1].ToLowerInvariant() : null;
        var text = topic switch
        {
            null when parts.Length == 1 => await HelpIndexWithDeadlineAsync(connection.EventId, state, ct),
            "presenza" or "presenze" => await WithAttendanceDeadlineAsync(connection.EventId, PresenceHelp, ct),
            "riepilogo" or "riepiloghi" => SummaryHelp,
            "pasti" or "pasto" or "piatti" or "proponi" or "preferenze" => (state.ProposalsOpen ? MealsHelpProposalsOpen : MealsHelpProposalsClosed) + state.VoteHelp,
            "elenchi" or "elenco" or "menu" or "todo" or "impegni" or "porto" or "prendo" or "fatto" when state.Lists => ListsHelp(state),
            _ => null
        };
        return text ?? HelpNotFound + await HelpIndexWithDeadlineAsync(connection.EventId, state, ct);
    }

    private async Task<string> HelpIndexWithDeadlineAsync(Guid eventId, HelpState state, CancellationToken ct) =>
        await WithAttendanceDeadlineAsync(eventId, HelpIndex(state), ct);

    private static string HelpIndex(HelpState state) => HelpTitle
        + "!aiuto presenza - conferme, date e famiglia\n"
        + "!aiuto riepilogo - chi c'è e quando\n"
        + "!aiuto pasti - proposte e preferenze sui piatti"
        + (state.Lists ? "\n!aiuto elenchi - menu e incarichi da prendere" : "");

    private static string ListsHelp(HelpState state)
    {
        var text = "📋 *Elenchi*\nServe la presenza confermata; i bambini non possono usare i comandi che modificano.";
        if (state.Menu) text += "\n\n🍽️ *Menu*\n!menu [pagina] - mostra il menu\n!porto <rif> - ti proponi di portare una voce libera o già tua";
        if (state.Todo) text += "\n\n✅ *Cose da fare*\n!todo [pagina] - mostra le attività\n!prendo <rif> - ti proponi per un'attività\n!fatto <rif> - segni come completata un'attività tua";
        var verbs = state.Menu && state.Todo ? "!porto, !prendo e !fatto" : state.Menu ? "!porto" : "!prendo e !fatto";
        return text + $"\n\n🙋 Per {verbs} ti rispondo con un codice: invia !conferma <codice> entro cinque minuti e solo allora la modifica viene salvata.";
    }

    private async Task<HelpState> HelpStateAsync(WhatsAppConnection connection, CancellationToken ct)
    {
        var cycles = await store.ListAsync<MealVotingCycle>(connection.EventId, ct);
        // At most one cycle is not closed; with only closed cycles the voting is closed for good.
        MealVotingState? vote = cycles.FirstOrDefault(x => x.State != MealVotingState.Closed)?.State ?? (cycles.Count > 0 ? MealVotingState.Closed : null);
        var proposalsOpen = cycles.Count == 0 || cycles.Any(x => x.State == MealVotingState.Collection);
        return new(await ListApprovedAsync(connection, WhatsAppListKind.Menu, ct), await ListApprovedAsync(connection, WhatsAppListKind.Todo, ct), vote, proposalsOpen);
    }

    // Same check as !menu/!todo; an unreadable (e.g. too long) list counts as not active.
    private async Task<bool> ListApprovedAsync(WhatsAppConnection connection, WhatsAppListKind kind, CancellationToken ct)
    {
        try
        {
            var pages = await ListPagesAsync(connection.EventId, kind, ct);
            return await IsApprovedAsync(connection, kind, ListFingerprint(connection, kind, pages), ct);
        }
        catch (LocalValidationException) { return false; }
    }
}
