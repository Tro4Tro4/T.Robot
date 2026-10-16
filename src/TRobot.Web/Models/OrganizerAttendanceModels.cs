using System.ComponentModel.DataAnnotations;
using TRobot.Application.Attendance;
using TRobot.Domain;

namespace TRobot.Web.Models;

public sealed class OrganizerAttendanceInput
{
    [Required] public Guid PersonId { get; set; }
    [Required] public Guid OperationId { get; set; }
    [Required, StringLength(100)] public string ExpectedVersion { get; set; } = "";
    [Required] public ParticipationStatus? Status { get; set; }
    public bool Confirmed { get; set; }
}
public sealed record OrganizerAttendanceViewModel(ParticipantAttendance Participant, OrganizerAttendanceInput Input);
