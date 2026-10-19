using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TRobot.Web.Models;

public sealed class AttendanceDeadlineInput
{
    [BindRequired] public Guid EventId { get; set; }
    public DateTime? DeadlineLocal { get; set; }
    [BindRequired] public int Revision { get; set; }
    public bool Confirmed { get; set; }
}

public sealed class AttendanceReminderInput
{
    [BindRequired] public Guid EventId { get; set; }
    [BindRequired] public Guid OperationId { get; set; }
}
