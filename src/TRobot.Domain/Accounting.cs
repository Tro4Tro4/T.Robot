namespace TRobot.Domain;

public enum ExpenseStatus { Pending, Approved, Rejected, Cancelled }
public enum PaymentStatus { Declared, Confirmed, Cancelled }

public static class AccountingRules
{
    public static decimal Amount(decimal value)
    {
        if (value is <= 0 or > 1000000 || decimal.Round(value, 2) != value)
            throw new ArgumentException("Importo positivo fino a 1.000.000 EUR, con centesimi esatti.");
        return value;
    }
    public static decimal Weight(decimal value)
    {
        if (value is < 0 or > 10 || decimal.Round(value, 3) != value)
            throw new ArgumentException("Peso da 0 a 10, con massimo tre decimali.");
        return value;
    }
    public static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value : throw new ArgumentException("La data deve essere UTC.");
}

public sealed class Expense : ILocalRecord
{
    private Expense() { }
    public Expense(Guid eventId, Guid id, Guid advancedByPersonId, Guid declaredByActorId, string description, decimal amount, DateTime utc)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); Id = Guard.Id(id, nameof(id));
        AdvancedByPersonId = Guard.Id(advancedByPersonId, nameof(advancedByPersonId));
        DeclaredByActorId = Guard.Id(declaredByActorId, nameof(declaredByActorId));
        Description = Guard.Text(description, 150, nameof(description)); Amount = AccountingRules.Amount(amount);
        CreatedAtUtc = ChangedAtUtc = AccountingRules.Utc(utc);
    }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid AdvancedByPersonId { get; private set; }
    public Guid DeclaredByActorId { get; private set; }
    public string Description { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public ExpenseStatus Status { get; private set; }
    public int Revision { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ChangedAtUtc { get; private set; }
    public bool Transition(ExpenseStatus target, bool confirmed, DateTime utc)
    {
        if (!confirmed) throw new ArgumentException("Conferma la modifica della spesa.");
        if (!Enum.IsDefined(target)) throw new ArgumentException("Stato spesa non valido.");
        AccountingRules.Utc(utc);
        if (Status == target) return false;
        if (!(Status == ExpenseStatus.Pending && target is ExpenseStatus.Approved or ExpenseStatus.Rejected or ExpenseStatus.Cancelled)
            && !(Status == ExpenseStatus.Approved && target == ExpenseStatus.Cancelled))
            throw new InvalidOperationException("Transizione spesa non consentita; lo storico non viene riaperto.");
        var revision = checked(Revision + 1); Status = target; ChangedAtUtc = utc; Revision = revision; return true;
    }
}

public sealed class Payment : ILocalRecord
{
    private Payment() { }
    public Payment(Guid eventId, Guid id, Guid fromPersonId, Guid toPersonId, Guid declaredByActorId, decimal amount, DateTime utc)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); Id = Guard.Id(id, nameof(id));
        FromPersonId = Guard.Id(fromPersonId, nameof(fromPersonId)); ToPersonId = Guard.Id(toPersonId, nameof(toPersonId));
        if (FromPersonId == ToPersonId) throw new ArgumentException("Mittente e destinatario devono essere distinti.");
        DeclaredByActorId = Guard.Id(declaredByActorId, nameof(declaredByActorId));
        Amount = AccountingRules.Amount(amount); CreatedAtUtc = ChangedAtUtc = AccountingRules.Utc(utc);
    }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid FromPersonId { get; private set; }
    public Guid ToPersonId { get; private set; }
    public Guid DeclaredByActorId { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentStatus Status { get; private set; }
    public int Revision { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ChangedAtUtc { get; private set; }
    public bool Transition(PaymentStatus target, bool confirmed, DateTime utc)
    {
        if (!confirmed) throw new ArgumentException("Conferma la registrazione manuale del pagamento.");
        if (!Enum.IsDefined(target)) throw new ArgumentException("Stato pagamento non valido.");
        AccountingRules.Utc(utc);
        if (Status == target) return false;
        if (!(Status == PaymentStatus.Declared && target is PaymentStatus.Confirmed or PaymentStatus.Cancelled)
            && !(Status == PaymentStatus.Confirmed && target == PaymentStatus.Cancelled))
            throw new InvalidOperationException("Transizione pagamento non consentita; lo storico non viene riaperto.");
        var revision = checked(Revision + 1); Status = target; ChangedAtUtc = utc; Revision = revision; return true;
    }
}

public sealed class AccountingSettings : ILocalRecord
{
    private AccountingSettings() { }
    public AccountingSettings(Guid eventId) { Id = EventId = Guard.Id(eventId, nameof(eventId)); }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public int Revision { get; private set; }
    public decimal ChildWeight { get; private set; } = 0.5m;
    public bool Update(decimal childWeight)
    {
        AccountingRules.Weight(childWeight);
        if (childWeight == ChildWeight) return false;
        var revision = checked(Revision + 1); ChildWeight = childWeight; Revision = revision; return true;
    }
}

public sealed class ParticipantWeight : ILocalRecord
{
    private ParticipantWeight() { }
    public ParticipantWeight(Guid eventId, Guid id, Guid personId, decimal? weight)
    {
        Id = Guard.Id(id, nameof(id)); EventId = Guard.Id(eventId, nameof(eventId)); PersonId = Guard.Id(personId, nameof(personId));
        if (weight.HasValue) AccountingRules.Weight(weight.Value); Weight = weight;
    }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid PersonId { get; private set; }
    public decimal? Weight { get; private set; }
    public int Revision { get; private set; }
    public bool Update(decimal? weight)
    {
        if (weight.HasValue) AccountingRules.Weight(weight.Value);
        if (Weight == weight) return false;
        var revision = checked(Revision + 1); Weight = weight; Revision = revision; return true;
    }
}
