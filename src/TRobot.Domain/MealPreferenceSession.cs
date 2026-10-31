namespace TRobot.Domain;

public enum MealPreferenceStep { Closed, Person, Day, Meal, Selection, Confirmation }

/// <summary>One temporary private preference dialogue per mapped sender and event.</summary>
public sealed class MealPreferenceSession : ILocalRecord
{
    private MealPreferenceSession() { }
    public MealPreferenceSession(Guid eventId, Guid senderId)
    { EventId = Guard.Id(eventId, nameof(eventId)); Id = Guard.Id(senderId, nameof(senderId)); }
    public Guid EventId { get; private set; }
    public Guid Id { get; private set; }
    public Guid ActorId { get; private set; }
    public string GroupId { get; private set; } = "";
    public int ConnectionRevision { get; private set; }
    public string IdentityFingerprint { get; private set; } = "";
    public string PersonOptions { get; private set; } = "";
    public Guid CycleId { get; private set; }
    public int CycleRevision { get; private set; }
    public Guid? PersonId { get; private set; }
    public DateOnly? Day { get; private set; }
    public MealKind? Meal { get; private set; }
    public string SelectedIds { get; private set; } = "";
    public int BallotRevision { get; private set; }
    public string EligibilityFingerprint { get; private set; } = "";
    public int Page { get; private set; } = 1;
    public string ReviewedPages { get; private set; } = "";
    public MealPreferenceStep Step { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public int Revision { get; private set; }

    public bool IsValid(Guid actor, string group, int connectionRevision, string identity, DateTime now) =>
        Step != MealPreferenceStep.Closed && ActorId == actor && GroupId == group
        && ConnectionRevision == connectionRevision && IdentityFingerprint == identity
        && now >= StartedAtUtc && now < ExpiresAtUtc;

    public void Start(Guid actor, string group, int connectionRevision, string identity, Guid cycleId,
        int cycleRevision, IEnumerable<Guid> people, DateTime now)
    {
        if (now.Kind != DateTimeKind.Utc || connectionRevision < 0 || cycleRevision < 0
            || !group.EndsWith("@g.us", StringComparison.Ordinal)) throw new ArgumentException("Dialogo preferenze non valido.");
        ActorId = Guard.Id(actor, nameof(actor)); GroupId = Guard.Text(group, 200, nameof(group));
        IdentityFingerprint = WhatsAppIdentityAlias.Fingerprint(identity); ConnectionRevision = connectionRevision;
        CycleId = Guard.Id(cycleId, nameof(cycleId)); CycleRevision = cycleRevision;
        PersonOptions = Encode(people); StartedAtUtc = now; ExpiresAtUtc = now.AddMinutes(5);
        ResetPerson();
    }
    public void ResetPerson()
    {
        PersonId = null; Day = null; Meal = null; SelectedIds = ""; EligibilityFingerprint = "";
        ReviewedPages = ""; Page = 1; Step = MealPreferenceStep.Person; Revision++;
    }
    public void ChoosePerson(Guid id)
    {
        if (!Decode(PersonOptions).Contains(id)) throw new ArgumentException("Persona non disponibile.");
        PersonId = id; Day = null; Meal = null; SelectedIds = ""; Page = 1;
        Step = MealPreferenceStep.Day; Revision++;
    }
    public void ChooseDay(DateOnly day)
    {
        if (Step != MealPreferenceStep.Day || day == default) throw new InvalidOperationException("Giorno non valido.");
        Day = day; Step = MealPreferenceStep.Meal; Page = 1; Revision++;
    }
    public void ChooseMeal(MealKind meal, IEnumerable<Guid> selected, int ballotRevision, string eligibility)
    {
        if (Step != MealPreferenceStep.Meal || !Enum.IsDefined(meal)) throw new InvalidOperationException("Pasto non valido.");
        Meal = meal; SelectedIds = Encode(selected); BallotRevision = ballotRevision;
        _ = WhatsAppIdentityAlias.Fingerprint(eligibility);
        EligibilityFingerprint = eligibility;
        ReviewedPages = ""; Page = 1; Step = MealPreferenceStep.Selection; Revision++;
    }
    public void SetSelection(IEnumerable<Guid> selected)
    {
        if (Step != MealPreferenceStep.Selection) throw new InvalidOperationException("Selezione non disponibile.");
        SelectedIds = Encode(selected); ReviewedPages = ""; Revision++;
    }
    public void Review()
    {
        if (Step != MealPreferenceStep.Selection) throw new InvalidOperationException("Riepilogo non disponibile.");
        Step = MealPreferenceStep.Confirmation; Page = 1; ReviewedPages = "1"; Revision++;
    }
    public void Edit()
    {
        if (Step != MealPreferenceStep.Confirmation) throw new InvalidOperationException("Modifica non disponibile.");
        Step = MealPreferenceStep.Selection; Page = 1; ReviewedPages = ""; Revision++;
    }
    public void ShowPage(int page, int count)
    {
        if (page < 1 || page > count) throw new ArgumentException("Pagina non disponibile.");
        Page = page;
        if (Step == MealPreferenceStep.Confirmation)
            ReviewedPages = string.Join(',', ReviewedPages.Split(',', StringSplitOptions.RemoveEmptyEntries).Append(page.ToString()).Distinct());
        Revision++;
    }
    public bool HasReviewed(int pages) => Enumerable.Range(1, pages)
        .All(x => ReviewedPages.Split(',').Contains(x.ToString()));
    public void Close()
    {
        Step = MealPreferenceStep.Closed; PersonOptions = ""; PersonId = null; Day = null; Meal = null;
        SelectedIds = ""; EligibilityFingerprint = ""; ReviewedPages = ""; Revision++;
    }
    public static string Encode(IEnumerable<Guid> ids) => string.Join(',', ids.Distinct().Select(x => Guard.Id(x, nameof(ids))).Order().Select(x => x.ToString("N")));
    public static Guid[] Decode(string ids) => ids.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => Guid.ParseExact(x, "N")).ToArray();
}
