using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TRobot.Web.Models;

public class WhatsAppActionInput
{
    [BindRequired] public Guid EventId { get; set; }
    public bool Confirmed { get; set; }
}
public sealed class WhatsAppGroupInput : WhatsAppActionInput
{
    [Required, StringLength(200)] public string GroupId { get; set; } = "";
    [BindRequired] public int Revision { get; set; }
}
public sealed class WhatsAppGroupDeactivateInput : WhatsAppActionInput
{
    [BindRequired] public int Revision { get; set; }
}
public sealed class WhatsAppFeatureInput : WhatsAppActionInput
{
    [BindRequired] public int FeaturesRevision { get; set; }
    public bool Attendance { get; set; }
    public bool Information { get; set; }
    public bool Operations { get; set; }
    public bool MealProposals { get; set; }
    public bool MealPreferences { get; set; }
    public bool FunReplies { get; set; }
}
public sealed class WhatsAppMapInput : WhatsAppActionInput
{
    [Required, StringLength(200)] public string MemberId { get; set; } = "";
    [BindRequired] public Guid PersonId { get; set; }
    [BindRequired] public int Revision { get; set; }
}
public sealed class WhatsAppRubricaInput : WhatsAppActionInput
{
    [BindRequired] public int Revision { get; set; }
}
public sealed class WhatsAppDraftInput
{
    [BindRequired] public Guid EventId { get; set; }
    [BindRequired] public Guid OperationId { get; set; }
    [Required] public string Body { get; set; } = "";
}
public sealed class WhatsAppSendInput : WhatsAppActionInput
{
    [BindRequired] public Guid Id { get; set; }
    [BindRequired] public int Revision { get; set; }
}
public sealed class WhatsAppListApprovalInput : WhatsAppActionInput
{
    [BindRequired] public Guid OperationId { get; set; }
    [BindRequired] public TRobot.Domain.WhatsAppListKind Kind { get; set; }
    [Required, StringLength(200)] public string GroupId { get; set; } = "";
    [BindRequired] public int ConnectionRevision { get; set; }
    [Required, StringLength(64, MinimumLength = 64)] public string Fingerprint { get; set; } = "";
}
