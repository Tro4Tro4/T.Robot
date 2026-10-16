using System.ComponentModel.DataAnnotations;
using TRobot.Application.Attendance;
using TRobot.Application.Events;

namespace TRobot.Web.Models;

public sealed class EventFormInput
{
    [Required, StringLength(200)] public string Name { get; set; } = "";
    [Required, DataType(DataType.DateTime)] public DateTime? LocalStart { get; set; }
    [Required, StringLength(100)] public string TimeZoneId { get; set; } = "Europe/Rome";
    [Required, Range(0, int.MaxValue)] public int? Revision { get; set; }
    [StringLength(300)] public string? Location { get; set; }
    // The service validates the length after normalizing line endings submitted by browsers.
    public string? Program { get; set; }
    public EventInput ToCommand() => new(Name, LocalStart!.Value, TimeZoneId, Location, Program);

    public static EventFormInput From(TRobot.Domain.Event ev) => new()
    {
        Name = ev.Name, TimeZoneId = ev.TimeZoneId, Revision = ev.Revision,
        Location = ev.Location, Program = ev.Program,
        LocalStart = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(ev.StartsAtUtc,
            TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId)), DateTimeKind.Unspecified)
    };
}

public sealed class AddParticipantInput
{
    [Required] public Guid OperationId { get; set; }
    [Required, StringLength(150)] public string DisplayName { get; set; } = "";
    public bool EnableSimulation { get; set; } = true;
}

public sealed record EventsViewModel(IReadOnlyList<EventOption> Events, bool CanCreate);
public sealed record EventEditorViewModel(EventFormInput Input);
public sealed record EventDetailsViewModel(EventDetails Details, EventFormInput Input, AddParticipantInput Participant);

public sealed class ParticipantNameInput
{
    [Required] public Guid PersonId { get; set; }
    [Required, Range(0, int.MaxValue)] public int? Revision { get; set; }
    [Required, StringLength(150)] public string DisplayName { get; set; } = "";
    public bool ConfirmedShared { get; set; }
}

public sealed record ParticipantNameViewModel(ParticipantNameEditor Editor, ParticipantNameInput Input);
