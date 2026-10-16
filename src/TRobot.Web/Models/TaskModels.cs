using System.ComponentModel.DataAnnotations;
using TRobot.Application.Local;
using TRobot.Application.Tasks;
using TRobot.Domain;

namespace TRobot.Web.Models;

public sealed class TaskInput
{
    public Guid EventId { get; set; }
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    [Required, StringLength(160), Display(Name = "Titolo")] public string Title { get; set; } = "";
    [StringLength(2000), Display(Name = "Descrizione")] public string? Description { get; set; }
    [Display(Name = "Priorità")] public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    [Display(Name = "Stato")] public OrganizationTaskStatus Status { get; set; }
    [Display(Name = "Responsabile")] public Guid? AssigneeId { get; set; }
    [Display(Name = "Scadenza locale")] public DateTime? DueLocal { get; set; }
    public int Revision { get; set; }
    [Display(Name = "Confermo l'annullamento")] public bool ConfirmCancellation { get; set; }
    public TaskEdit ToEdit() => new(Title, Description, Priority, Status, AssigneeId, DueLocal, Revision, ConfirmCancellation, OperationId);
}
public sealed record TaskPage(TaskInput Input, string EventName, string TimeZoneId, IReadOnlyList<LocalPerson> People);
public static class TaskLabels
{
    public static string Status(OrganizationTaskStatus value) => value switch
    { OrganizationTaskStatus.Todo => "Da iniziare", OrganizationTaskStatus.InProgress => "In corso", OrganizationTaskStatus.Completed => "Completata", OrganizationTaskStatus.Cancelled => "Annullata", _ => "Non valido" };
    public static string Priority(TaskPriority value) => value switch
    { TaskPriority.Low => "Bassa", TaskPriority.Normal => "Normale", TaskPriority.High => "Alta", _ => "Non valida" };
}
