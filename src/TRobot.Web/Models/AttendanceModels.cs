using System.ComponentModel.DataAnnotations;
using TRobot.Application.Attendance;

namespace TRobot.Web.Models;

public sealed class SimulationInput
{
    [Required] public Guid EventId { get; set; }
    [Required, StringLength(200)] public string SenderId { get; set; } = "";
    [Required, StringLength(100)] public string MessageId { get; set; } = Guid.NewGuid().ToString("N");
    [Required, StringLength(100)] public string Text { get; set; } = "!presenza si";
    public bool ConfirmVote { get; set; }
}

public sealed record DashboardViewModel(IReadOnlyList<EventOption> Events, AttendanceDashboard? Dashboard,
    SimulationInput Input, TRobot.Application.Local.LocalOverview? LocalOverview = null);

public sealed class LoginInput
{
    [Required, StringLength(100)] public string UserName { get; set; } = "";
    [Required, StringLength(128), DataType(DataType.Password)] public string Password { get; set; } = "";
}
