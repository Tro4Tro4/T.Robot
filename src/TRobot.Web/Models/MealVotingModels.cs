using System.ComponentModel.DataAnnotations;
using TRobot.Application.Menu;

namespace TRobot.Web.Models;

public sealed class MealVotingInput
{
    public Guid EventId { get; set; }
    public Guid CycleId { get; set; }
    public Guid Id { get; set; }
    [Range(0, int.MaxValue)] public int Revision { get; set; }
    [Range(0, int.MaxValue)] public int? EntryRevision { get; set; }
    [StringLength(200)] public string? Name { get; set; }
    public Guid[] ProposalIds { get; set; } = [];
    public bool IsExcluded { get; set; }
    public bool Confirmed { get; set; }
}

public sealed record MealVotingOrganizerPage(MealVotingPage Voting, MealProposalPage Archive,
    IReadOnlyDictionary<Guid, string> PersonNames, MealPreferenceHistoryPage History);
