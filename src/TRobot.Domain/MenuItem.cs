namespace TRobot.Domain;

public enum MenuCategory { Food, Drink, Other }
public enum MenuAvailability { Planned, Assigned, Ready, Cancelled }

public sealed class MenuItem : ILocalRecord
{
    private MenuItem() { }
    public MenuItem(Guid id, Guid eventId, string name, MenuCategory category, decimal quantity, string unit,
        Guid? responsiblePersonId = null, MenuAvailability availability = MenuAvailability.Planned, string? notes = null)
    {
        Id = Guard.Id(id, nameof(id)); EventId = Guard.Id(eventId, nameof(eventId));
        Apply(name, category, quantity, unit, responsiblePersonId, availability, notes);
    }
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public int Revision { get; private set; }
    public string Name { get; private set; } = null!;
    public MenuCategory Category { get; private set; }
    public decimal Quantity { get; private set; }
    public string Unit { get; private set; } = null!;
    public Guid? ResponsiblePersonId { get; private set; }
    public MenuAvailability Availability { get; private set; }
    public string? Notes { get; private set; }

    public bool Update(string name, MenuCategory category, decimal quantity, string unit, Guid? responsiblePersonId,
        MenuAvailability availability, string? notes)
    {
        var candidate = new MenuItem(Id, EventId, name, category, quantity, unit, responsiblePersonId, availability, notes);
        if (SameContent(candidate)) return false;
        var next = checked(Revision + 1);
        Apply(candidate.Name, category, quantity, candidate.Unit, responsiblePersonId, availability, candidate.Notes);
        Revision = next; return true;
    }
    public bool SameContent(MenuItem other) => Name == other.Name && Category == other.Category && Quantity == other.Quantity
        && Unit == other.Unit && ResponsiblePersonId == other.ResponsiblePersonId && Availability == other.Availability && Notes == other.Notes;

    private void Apply(string name, MenuCategory category, decimal quantity, string unit, Guid? personId, MenuAvailability availability, string? notes)
    {
        name = Text(name, 150, "Il nome"); unit = Text(unit, 30, "L'unità");
        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (notes?.Length > 2000) throw new ArgumentException("Le note possono contenere al massimo 2000 caratteri.");
        if (!Enum.IsDefined(category) || !Enum.IsDefined(availability)) throw new ArgumentException("Categoria o disponibilità non valida.");
        if (quantity <= 0 || quantity > 1000000m || decimal.Round(quantity, 3) != quantity)
            throw new ArgumentException("La quantità deve essere positiva, fino a 1000000 e con massimo tre decimali.");
        if (personId == Guid.Empty) throw new ArgumentException("Responsabile non valido.");
        if (availability == MenuAvailability.Assigned && personId is null) throw new ArgumentException("Per lo stato assegnato serve un responsabile.");
        Name = name; Category = category; Quantity = quantity; Unit = unit; ResponsiblePersonId = personId; Availability = availability; Notes = notes;
    }
    private static string Text(string? text, int max, string field)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > max) throw new ArgumentException($"{field} è obbligatorio e può contenere al massimo {max} caratteri.");
        return text.Trim();
    }
}
