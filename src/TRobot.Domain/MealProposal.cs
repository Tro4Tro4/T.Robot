namespace TRobot.Domain;

public enum MealKind { Breakfast, Lunch, Dinner }
public enum MealProposalStep { Closed, Day, Meal, Dish, Availability, Confirmation }

public sealed class MealProposal : ILocalRecord
{
    private MealProposal() { }
    public MealProposal(Guid eventId, Guid personId, DateOnly day, MealKind meal, string dishName, bool offersToCook, DateTime utc)
    {
        EventId = Guard.Id(eventId, nameof(eventId)); PersonId = Guard.Id(personId, nameof(personId));
        if (day == default || !Enum.IsDefined(meal) || utc.Kind != DateTimeKind.Utc) throw new ArgumentException("Proposta pasto non valida.");
        Day = day; Meal = meal; DishName = ValidateDish(dishName); OffersToCook = offersToCook; CreatedAtUtc = utc;
    }
    internal static string ValidateDish(string text)
    {
        var value = Guard.Text(text, 200, nameof(text));
        if (value.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format))
            throw new ArgumentException("Il piatto deve essere un nome su una sola riga.");
        return value;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid EventId { get; private set; }
    public Guid PersonId { get; private set; }
    public DateOnly Day { get; private set; }
    public MealKind Meal { get; private set; }
    public string DishName { get; private set; } = null!;
    public bool OffersToCook { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? WithdrawnAtUtc { get; private set; }
    public int Revision { get; private set; }
}

public sealed class MealProposalSession : ILocalRecord
{
    private MealProposalSession() { }
    public MealProposalSession(Guid eventId, Guid personId)
    { EventId = Guard.Id(eventId, nameof(eventId)); Id = Guard.Id(personId, nameof(personId)); }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public Guid ActorId { get; private set; }
    public string GroupId { get; private set; } = "";
    public int ConnectionRevision { get; private set; }
    public string IdentityFingerprint { get; private set; } = "";
    public DateTime StartedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public MealProposalStep Step { get; private set; }
    public DateOnly? Day { get; private set; }
    public MealKind? Meal { get; private set; }
    public string? DishName { get; private set; }
    public bool? OffersToCook { get; private set; }
    public int Revision { get; private set; }
    public void Start(Guid actorId, string groupId, int connectionRevision, string identityFingerprint, DateTime utc)
    {
        actorId = Guard.Id(actorId, nameof(actorId)); groupId = Guard.Text(groupId, 200, nameof(groupId));
        identityFingerprint = WhatsAppIdentityAlias.Fingerprint(identityFingerprint);
        if (!groupId.EndsWith("@g.us", StringComparison.Ordinal) || connectionRevision < 0 || utc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Sessione non valida.");
        ActorId = actorId; GroupId = groupId; ConnectionRevision = connectionRevision; IdentityFingerprint = identityFingerprint;
        StartedAtUtc = utc; ExpiresAtUtc = utc.AddMinutes(5); Step = MealProposalStep.Day;
        Day = null; Meal = null; DishName = null; OffersToCook = null; Revision++;
    }
    public void SetDay(DateOnly day)
    { Require(MealProposalStep.Day); if (day == default) throw new ArgumentException("Giorno non valido."); Day = day; Step = MealProposalStep.Meal; Revision++; }
    public void SetMeal(MealKind meal)
    { Require(MealProposalStep.Meal); if (!Enum.IsDefined(meal)) throw new ArgumentException("Pasto non valido."); Meal = meal; Step = MealProposalStep.Dish; Revision++; }
    public void SetDish(string dish)
    { Require(MealProposalStep.Dish); DishName = MealProposal.ValidateDish(dish); Step = MealProposalStep.Availability; Revision++; }
    public void SetAvailability(bool offersToCook)
    { Require(MealProposalStep.Availability); OffersToCook = offersToCook; Step = MealProposalStep.Confirmation; Revision++; }
    public MealProposal Confirm(DateTime utc)
    {
        Require(MealProposalStep.Confirmation);
        if (utc.Kind != DateTimeKind.Utc || utc < StartedAtUtc || utc >= ExpiresAtUtc) throw new InvalidOperationException("Sessione scaduta.");
        var proposal = new MealProposal(EventId, Id, Day!.Value, Meal!.Value, DishName!, OffersToCook!.Value, utc);
        Close(); return proposal;
    }
    public void Close()
    { Step = MealProposalStep.Closed; Day = null; Meal = null; DishName = null; OffersToCook = null; Revision++; }
    private void Require(MealProposalStep step)
    { if (Step != step) throw new InvalidOperationException("Passaggio del dialogo non valido."); }
}
