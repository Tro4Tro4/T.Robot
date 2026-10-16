using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Households;
using TRobot.Web.Models;

namespace TRobot.Web.Controllers;

public sealed class HouseholdsController(HouseholdService service) : LocalControllerBase
{
    [HttpGet] public Task<IActionResult> Index(Guid eventId, CancellationToken ct) => ExecuteAsync(async () =>
        View(await service.ListAsync(ActorId, eventId, ct)));
    [HttpGet] public Task<IActionResult> Create(Guid eventId, CancellationToken ct) => ExecuteAsync(async () =>
    {
        await service.ListAsync(ActorId, eventId, ct);
        return View(new HouseholdInput { EventId = eventId, OperationId = Guid.NewGuid() });
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Create(HouseholdInput input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        await service.ListAsync(ActorId, input.EventId, ct);
        if (!ModelState.IsValid) return View(input);
        await service.CreateAsync(ActorId, input.EventId, input.Name, input.OperationId, ct);
        return RedirectToAction(nameof(Index), new { eventId = input.EventId });
    });
    [HttpGet] public Task<IActionResult> Add(Guid eventId, Guid householdId, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var page = await service.ListAsync(ActorId, eventId, ct);
        var household = page.Households.SingleOrDefault(x => x.Id == householdId);
        if (household is null) return NotFound();
        return View("Edit", new HouseholdMemberPage(new HouseholdMemberInput { EventId = eventId, HouseholdId = householdId,
            HouseholdRevision = household.Revision, OperationId = Guid.NewGuid() }, page, household));
    });
    [HttpGet] public Task<IActionResult> Edit(Guid eventId, Guid id, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var page = await service.ListAsync(ActorId, eventId, ct);
        var member = page.Members.SingleOrDefault(x => x.Id == id);
        if (member is null) return NotFound();
        var household = page.Households.Single(x => x.Id == member.HouseholdId);
        return View(new HouseholdMemberPage(new HouseholdMemberInput { EventId = eventId, HouseholdId = household.Id,
            Id = id, PersonId = member.PersonId, Kind = member.Kind, CompanionOfMemberId = member.CompanionOfMemberId,
            Revision = member.Revision, HouseholdRevision = household.Revision, IsActive = member.IsActive }, page, household));
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Save(HouseholdMemberInput input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        await service.ListAsync(ActorId, input.EventId, ct);
        if (!ModelState.IsValid) return BadRequest("Controlla i dati dell'appartenenza.");
        if (input.Id == Guid.Empty) await service.AddAsync(ActorId, input.EventId, input.HouseholdId, input.ToEdit(), ct);
        else await service.UpdateAsync(ActorId, input.EventId, input.HouseholdId, input.Id, input.ToEdit(), input.IsActive, ct);
        return RedirectToAction(nameof(Index), new { eventId = input.EventId });
    });
}
