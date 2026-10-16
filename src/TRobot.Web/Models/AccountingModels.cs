using System.ComponentModel.DataAnnotations;
using System.Globalization;
using TRobot.Application.Accounting;
using TRobot.Domain;

namespace TRobot.Web.Models;

public static class AccountingInput
{
    public const string AmountPattern = @"^[0-9]+([.,][0-9]{1,2})?$";
    public const string WeightPattern = @"^[0-9]+([.,][0-9]{1,3})?$";
    public static decimal Number(string value) => decimal.Parse(value.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    public static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    public static string Weight(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    public static string Participation(ParticipationStatus status) => status switch
    { ParticipationStatus.Confirmed => "Confermato", ParticipationStatus.Undecided => "Indeciso", ParticipationStatus.Absent => "Assente", _ => "Senza risposta" };
    public static string Expense(ExpenseStatus status) => status switch
    { ExpenseStatus.Pending => "Da approvare", ExpenseStatus.Approved => "Approvata", ExpenseStatus.Rejected => "Rifiutata", ExpenseStatus.Cancelled => "Annullata", _ => "Non valido" };
    public static string Payment(PaymentStatus status) => status switch
    { PaymentStatus.Declared => "Dichiarato", PaymentStatus.Confirmed => "Confermato manualmente", PaymentStatus.Cancelled => "Annullato", _ => "Non valido" };
}
public sealed class ExpenseForm
{
    public Guid EventId { get; set; }
    [Required] public Guid? OperationId { get; set; }
    [Required] public Guid? AdvancedByPersonId { get; set; }
    [Required, StringLength(150)] public string Description { get; set; } = "";
    [Required, StringLength(20), RegularExpression(AccountingInput.AmountPattern)] public string Amount { get; set; } = "";
    public bool Confirmed { get; set; }
    public ExpenseDeclaration ToInput() => new(OperationId!.Value, AdvancedByPersonId!.Value, Description, AccountingInput.Number(Amount), Confirmed);
}
public sealed class PaymentForm
{
    public Guid EventId { get; set; }
    [Required] public Guid? OperationId { get; set; }
    [Required] public Guid? FromPersonId { get; set; }
    [Required] public Guid? ToPersonId { get; set; }
    [Required, StringLength(20), RegularExpression(AccountingInput.AmountPattern)] public string Amount { get; set; } = "";
    public bool Confirmed { get; set; }
    public PaymentDeclaration ToInput() => new(OperationId!.Value, FromPersonId!.Value, ToPersonId!.Value, AccountingInput.Number(Amount), Confirmed);
}
public sealed class ExpenseTransitionForm
{
    public Guid EventId { get; set; }
    [Required] public Guid? Id { get; set; }
    [Required] public int? Revision { get; set; }
    [Required] public ExpenseStatus? Target { get; set; }
    public bool Confirmed { get; set; }
}
public sealed class PaymentTransitionForm
{
    public Guid EventId { get; set; }
    [Required] public Guid? Id { get; set; }
    [Required] public int? Revision { get; set; }
    [Required] public PaymentStatus? Target { get; set; }
    public bool Confirmed { get; set; }
}
public sealed class AccountingSettingsForm
{
    public Guid EventId { get; set; }
    [Required] public int? Revision { get; set; }
    [Required, StringLength(20), RegularExpression(AccountingInput.WeightPattern)] public string ChildWeight { get; set; } = "";
    public bool Confirmed { get; set; }
}
public sealed class WeightForm
{
    public Guid EventId { get; set; }
    [Required] public Guid? PersonId { get; set; }
    [Required] public Guid? OperationId { get; set; }
    [Required] public int? Revision { get; set; }
    [StringLength(20), RegularExpression(AccountingInput.WeightPattern)] public string? Weight { get; set; }
    public bool Confirmed { get; set; }
}
