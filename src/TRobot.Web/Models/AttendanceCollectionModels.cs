using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace TRobot.Web.Models;

public sealed class AttendanceDeadlineInput
{
    [BindRequired] public Guid EventId { get; set; }
    public DateTime? DeadlineLocal { get; set; }
    [BindRequired] public int Revision { get; set; }
    public bool Confirmed { get; set; }
}

public sealed class AttendanceStayModeInput
{
    [BindRequired] public Guid EventId { get; set; }
    /// <summary>Vero = i partecipanti indicano arrivo e durata; falso = la presenza vale per la data dell'evento.</summary>
    public bool AskStayDates { get; set; }
    [System.ComponentModel.DataAnnotations.Range(1, 31)] public int EventDays { get; set; } = 1;
    [BindRequired] public int Revision { get; set; }
    public bool Confirmed { get; set; }
}

public sealed class AttendanceQuestionInput
{
    [BindRequired] public Guid EventId { get; set; }
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(200)] public string Text { get; set; } = "";
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(600)] public string Options { get; set; } = "";
    public bool Confirmed { get; set; }
}

public sealed class AttendanceQuestionActiveInput
{
    [BindRequired] public Guid EventId { get; set; }
    [BindRequired] public Guid QuestionId { get; set; }
    public bool Active { get; set; }
    [BindRequired] public int Revision { get; set; }
    public bool Confirmed { get; set; }
}

public sealed class AttendanceReminderInput
{
    [BindRequired] public Guid EventId { get; set; }
    [BindRequired] public Guid OperationId { get; set; }
}
