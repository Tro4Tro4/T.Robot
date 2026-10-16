using System.ComponentModel.DataAnnotations;
using TRobot.Application.Polls;

namespace TRobot.Web.Models;

public sealed class PollDraftInput
{
    [Required] public Guid? EventId { get; set; }
    public Guid? Id { get; set; }
    [Required] public Guid? OperationId { get; set; }
    [Required, StringLength(200), Display(Name = "Titolo per il gruppo")] public string Title { get; set; } = "";
    [Required, Display(Name = "Scadenza locale")] public DateTime? DeadlineLocal { get; set; }
    [Display(Name = "Opzioni: una per riga, da 2 a 10")] public string? Options { get; set; }
    [Required] public int? Revision { get; set; }
    public PollEdit ToEdit() => new(OperationId!.Value, Title, DeadlineLocal!.Value,
        (Options ?? "").ReplaceLineEndings("\n").Split('\n'), Revision!.Value);
}
public sealed record PollDraftPage(PollDraftInput Input, string EventName, string TimeZoneId);
public sealed class PollActionInput
{
    [Required] public Guid? EventId { get; set; }
    [Required] public Guid? Id { get; set; }
    [Required] public int? Revision { get; set; }
    public bool Confirmed { get; set; }
    public Guid? OperationId { get; set; }
    public Guid? PersonId { get; set; }
    public Guid? OptionId { get; set; }
    public Guid? DecisionOptionId { get; set; }
}
