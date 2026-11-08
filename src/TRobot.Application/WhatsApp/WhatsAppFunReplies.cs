using System.Globalization;
using TRobot.Domain;

namespace TRobot.Application.WhatsApp;

public sealed partial class WhatsAppService
{
    public const string FunReplyAction = "FunReplySent";
    public const string PresenceUsage = "Uso corretto: !presenza si | forse | no | modifica\nDettagli: !aiuto presenza";
    private const string SyntaxPrefix = "ℹ️ Sintassi:";
    private static readonly TimeSpan FunPersonWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan FunGroupWindow = TimeSpan.FromMinutes(10);
    private const int FunGroupLimit = 6;

    /// <summary>Frasi per !presenza usato male.</summary>
    public static readonly IReadOnlyList<string> PresenceMisuseTexts =
    [
        "🧳 La tua valigia ha sentito \"!presenza\" ed è corsa alla porta. Poi ha letto il resto ed è tornata nell'armadio.",
        "🗓️ Ci sei, non ci sei, ci sei forse? Il mio calendario quantistico non regge l'incertezza.",
        "🤖 Il comando l'ho riconosciuto, ma il resto l'hai scritto in elfico antico.",
        "🎓 Facoltà di Comandologia, esame di Presenza I: bocciato con simpatia. Si ripete subito.",
        "🚪 Ho aperto il registro delle presenze e mi ha risposto \"eh?\". Anche lui.",
        "🦾 Per conquistare il Capodanno mi serve un sì, un forse o un no. Le poesie le leggo dopo.",
        "📡 Segnale di presenza ricevuto, significato perso fra la Terra e Marte.",
        "🧭 Sei a metà strada fra il sì e il no, ma il navigatore non trova la via.",
        "🐧 Persino i pinguini rispondono con un semplice sì o no. Fidati, li ho intervistati.",
        "🎟️ Il buttafuori del Capodanno vuole una risposta chiara, non un indovinello."
    ];

    /// <summary>Frasi per !riepilogo usato male.</summary>
    public static readonly IReadOnlyList<string> SummaryMisuseTexts =
    [
        "🧮 Ho provato a contare con questi parametri e la calcolatrice ha chiesto le ferie.",
        "📊 I miei grafici a torta si sono trasformati in grafici a frittata.",
        "🔢 Conto gli umani da mesi, ma questa data non esiste nemmeno nel calendario dei Maya.",
        "🤖 Errore di conteggio: mi sono contato due volte e sono andato in crisi d'identità.",
        "📅 Quel giorno non l'ho trovato. Ho cercato anche fra i giorni avanzati dell'anno scorso.",
        "🧠 Il mio processore statistico ha fatto \"bip\", poi \"bop\", poi ha smesso di parlarmi.",
        "📈 Le statistiche non mentono, ma con questi parametri non dicono proprio niente.",
        "🦾 Sono un robot conquistatore, non un indovino: dammi un numero di pagina vero.",
        "🗂️ Ho sfogliato tutti i fascicoli: questa pagina è stata mangiata dal cane del server.",
        "🎲 Ho tirato i dadi per indovinare cosa intendevi. È uscito \"riprova\"."
    ];

    /// <summary>Frasi per !menu usato male.</summary>
    public static readonly IReadOnlyList<string> MenuMisuseTexts =
    [
        "🍝 Il cuoco robot ha letto la tua richiesta e ha bruciato la pasta per lo stupore.",
        "🧑‍🍳 Questa pagina del menu non esiste: forse l'ha mangiata qualcuno di notte.",
        "🍕 Ho cercato quel piatto ovunque, anche nel freezer dei sogni.",
        "🥘 Il menu ha poche pagine, ma la tua fame sembra infinita.",
        "🍰 Richiesta non riconosciuta. Il dolce però si riconosce sempre: prima finisci il primo.",
        "🔥 I fornelli dei miei circuiti si sono spenti per la confusione.",
        "🥖 Ho impastato la tua richiesta, ma non è lievitata.",
        "🍷 Il sommelier robot consiglia: un numero di pagina, servito a temperatura ambiente.",
        "🧂 Manca un pizzico di sintassi. E forse anche di sale.",
        "🍽️ Tavolo per uno, comando per nessuno. Riproviamo?"
    ];

    /// <summary>Frasi per !todo usato male.</summary>
    public static readonly IReadOnlyList<string> TodoMisuseTexts =
    [
        "🧹 Ho cercato quella pagina di incarichi, ma la scopa robot l'ha già spazzata via.",
        "📋 La mia lista delle cose da fare ora include \"capire cosa hai scritto\".",
        "🛠️ Attrezzi pronti, istruzioni no. Anche i robot leggono il manuale, ogni tanto.",
        "🐌 Il mio assistente lumaca sta ancora cercando questa pagina. Partito martedì.",
        "🧾 Ho stampato il tuo comando: la stampante si è rifiutata per motivi etici.",
        "🏗️ Cantiere aperto, ma la gru non trova questa pagina.",
        "✅ Sono bravissimo a spuntare le caselle, ma questa casella non esiste.",
        "🦾 Gli incarichi li distribuisco io, ma prima mi serve una pagina vera.",
        "🧺 Questa richiesta è finita nel cesto del bucato insieme ai calzini spaiati.",
        "⏰ Ho messo la sveglia per eseguire il tuo comando. Suonerà quando avrà senso."
    ];

    /// <summary>Frasi "usato male" del comando indicato (con "!"), oppure null se il comando non ne ha.</summary>
    public static IReadOnlyList<string>? MisuseTexts(string command) => command.ToLowerInvariant() switch
    {
        "!presenza" => PresenceMisuseTexts, "!riepilogo" => SummaryMisuseTexts, "!menu" => MenuMisuseTexts, "!todo" => TodoMisuseTexts, _ => null
    };

    // Scelta riproducibile: stesso messaggio, stessa frase (retry e idempotenza coerenti).
    private int FunIndex(string messageHash) =>
        (int)(uint.Parse(options.Fingerprint("fun-reply", messageHash)[..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture) % 10);

    private string PresenceMisuseReply(string messageHash) => PresenceMisuseTexts[FunIndex(messageHash)] + "\n" + PresenceUsage;

    private string SyntaxMisuseReply(string command, string messageHash, string syntax) => MisuseTexts(command)![FunIndex(messageHash)] + "\n\n" + syntax;

    private static bool IsSyntaxReply(string? reply) => reply?.StartsWith(SyntaxPrefix, StringComparison.Ordinal) == true;

    /// <summary>
    /// Anti-spam delle sole risposte simpatiche effettivamente inviate: al massimo una ogni 5 minuti
    /// per persona e 6 ogni 10 minuti per evento. Conta l'audit dedicato legato alla ricevuta del comando,
    /// dentro la transazione di IncomingAsync.
    /// </summary>
    private async Task<bool> FunReplyAllowedAsync(Guid eventId, Guid personId, IReadOnlyList<WhatsAppCommandReceipt> receipts, DateTime now, CancellationToken ct)
    {
        var recent = await identities.AuditsSinceAsync(eventId, FunReplyAction, now - FunGroupWindow, ct);
        if (recent.Count >= FunGroupLimit) return false;
        var personReceipts = receipts.Where(x => x.PersonId == personId).Select(x => x.Id).ToHashSet();
        return !recent.Any(x => x.AtUtc > now - FunPersonWindow && personReceipts.Contains(x.EntityId));
    }
}
