using System.ComponentModel.DataAnnotations;
using TRobot.Application.Lodging;

namespace TRobot.Web.Models;

public sealed class LodgingInput
{
    public Guid EventId { get; set; }
    [Range(0, int.MaxValue)] public int Revision { get; set; }
    [Required, StringLength(64, MinimumLength = 64)] public string Fingerprint { get; set; } = "";
    public Guid Id { get; set; }
    public Guid ApartmentId { get; set; }
    public Guid PersonId { get; set; }
    public Guid HouseholdId { get; set; }
    public Guid? RoomId { get; set; }
    [StringLength(150)] public string? Name { get; set; }
    [StringLength(1000)] public string? Notes { get; set; }
    public int Capacity { get; set; }
    public bool Confirmed { get; set; }
    public LodgingVersion Version => new(Revision, Fingerprint);
}

public sealed record LodgingPrintPage(LodgingPage Plan, LodgingHousehold Household);
