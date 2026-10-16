using System.ComponentModel.DataAnnotations;
using TRobot.Application.Games;

namespace TRobot.Web.Models;

public sealed class QuizDraftInput
{
    [Required] public Guid? EventId { get; set; }
    [Required] public Guid? OperationId { get; set; }
    [Required, StringLength(200)] public string Question { get; set; } = "";
    [Required] public DateTime? DeadlineLocal { get; set; }
    [Required] public string Options { get; set; } = "";
    [Required, Range(1,10)] public int? CorrectPosition { get; set; }
    public QuizDraft ToDraft() => new(OperationId!.Value, Question, DeadlineLocal!.Value, Options.ReplaceLineEndings("\n").Split('\n'), CorrectPosition!.Value);
}
public sealed record QuizDraftPage(QuizDraftInput Input, string EventName, string TimeZoneId);
public sealed class GameActionInput
{
    [Required] public Guid? EventId { get; set; }
    [Required] public Guid? Id { get; set; }
    [Required] public int? Revision { get; set; }
    public bool Confirmed { get; set; }
    public Guid? OperationId { get; set; }
    public Guid? PersonId { get; set; }
    public Guid? OptionId { get; set; }
}
public sealed class GameMembershipInput
{
    [Required] public Guid? EventId { get; set; }
    [Required] public Guid? PersonId { get; set; }
    [Required] public int? Revision { get; set; }
    public bool Active { get; set; }
    public bool Confirmed { get; set; }
}
