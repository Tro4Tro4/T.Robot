using System.ComponentModel.DataAnnotations;
using TRobot.Application.Households;
using TRobot.Domain;

namespace TRobot.Web.Models;

public sealed class HouseholdInput
{
    public Guid EventId { get; set; }
    public Guid OperationId { get; set; }
    [Required, StringLength(150), Display(Name = "Nome nucleo")] public string Name { get; set; } = "";
}
public sealed class HouseholdMemberInput
{
    public Guid EventId { get; set; }
    public Guid HouseholdId { get; set; }
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    [Display(Name = "Persona")] public Guid PersonId { get; set; }
    [Display(Name = "Classificazione")] public HouseholdMemberKind Kind { get; set; }
    [Display(Name = "Accompagnatore di")] public Guid? CompanionOfMemberId { get; set; }
    public int HouseholdRevision { get; set; }
    public int Revision { get; set; }
    public bool IsActive { get; set; } = true;
    [Display(Name = "Confermo la modifica o rimozione dell'appartenenza")] public bool Confirm { get; set; }
    public HouseholdMemberEdit ToEdit() => new(PersonId, Kind, CompanionOfMemberId, HouseholdRevision, Revision, Confirm, OperationId);
}
public sealed record HouseholdMemberPage(HouseholdMemberInput Input, HouseholdOverview Overview, Household Household);
