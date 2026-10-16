namespace TRobot.Web.Models;

public static class LocalHistoryLabels
{
    public static string Module(string? value) => value switch
    { "Tasks" => "Attività", "Households" => "Nuclei", "Menu" => "Menu", "Messages" => "Messaggi", "Polls" => "Sondaggi", "Accounting" => "Contabilità", "Games" => "Giochi", _ => "Modulo locale" };
    public static string Action(string? value) => value switch
    {
        "Created" => "Creato", "Updated" => "Modificato", "MemberAdded" => "Membro aggiunto", "MemberUpdated" => "Appartenenza modificata", "MemberRemoved" => "Appartenenza rimossa",
        "DraftCreated" => "Bozza creata", "DraftEdited" => "Bozza modificata", "Cancelled" => "Annullato", "Queued" => "Accodato", "PublicationQueued" => "Pubblicazione accodata",
        "ResultQueued" => "Risultato accodato", "SettingsChanged" => "Configurazione modificata", "SummaryQueued" => "Riepilogo accodato", "SentSimulated" => "Invio simulato concluso",
        "VoteSimulated" => "Voto simulato registrato", "Closed" => "Chiuso", "ExpenseDeclared" => "Spesa dichiarata", "ExpenseApproved" => "Spesa approvata", "ExpenseRejected" => "Spesa rifiutata",
        "ExpenseCancelled" => "Spesa annullata", "PaymentDeclared" => "Pagamento dichiarato", "PaymentConfirmed" => "Pagamento confermato manualmente", "PaymentCancelled" => "Pagamento annullato",
        "WeightsConfigured" => "Pesi configurati", "WeightOverridden" => "Peso individuale aggiornato", "Joined" => "Adesione registrata", "Withdrawn" => "Adesione ritirata", "AnswerSimulated" => "Risposta simulata registrata",
        _ => "Operazione registrata"
    };
}
