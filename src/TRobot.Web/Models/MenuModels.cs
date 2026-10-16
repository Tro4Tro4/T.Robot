using System.ComponentModel.DataAnnotations;
using System.Globalization;
using TRobot.Application.Menu;
using TRobot.Domain;

namespace TRobot.Web.Models;

public sealed class MenuForm
{
    [Required] public Guid? Id { get; set; }
    [Required, StringLength(150), Display(Name = "Nome")] public string Name { get; set; } = "";
    [Required, Display(Name = "Categoria")] public MenuCategory? Category { get; set; }
    [Required, StringLength(20), RegularExpression(@"^[0-9]+([.,][0-9]{1,3})?$", ErrorMessage = "Usa una quantità positiva con massimo tre decimali; punto o virgola sono separatori decimali, senza separatori delle migliaia."), Display(Name = "Quantità")]
    public string Quantity { get; set; } = "";
    [Required, StringLength(30), Display(Name = "Unità")] public string Unit { get; set; } = "";
    [Display(Name = "Responsabile")] public Guid? ResponsiblePersonId { get; set; }
    [Required, Display(Name = "Disponibilità")] public MenuAvailability? Availability { get; set; }
    // The domain validates length after normalizing browser CRLF line endings to LF.
    [Display(Name = "Note")] public string? Notes { get; set; }
    public int? Revision { get; set; }
    [Display(Name = "Confermo l'annullamento della voce")] public bool ConfirmCancellation { get; set; }
    public MenuInput ToInput() => new(Id!.Value, Name, Category!.Value,
        decimal.Parse(Quantity.Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
        Unit, ResponsiblePersonId, Availability!.Value, Notes, Revision, ConfirmCancellation);
}

public sealed record MenuViewModel(MenuPage Page, MenuForm Input, bool Editing);
