using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Lodging;
using TRobot.Application.Local;
using TRobot.Web.Models;

namespace TRobot.Web.Controllers;

[Authorize]
public sealed class LodgingController(LodgingService service) : LocalControllerBase
{
    [HttpGet]
    public Task<IActionResult> Index(Guid eventId, CancellationToken ct) => ExecuteAsync(async () =>
        View(await service.ReadAsync(ActorId, eventId, ct)));

    [HttpGet]
    public Task<IActionResult> Print(Guid eventId, Guid? householdId, Guid? personId, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var page = await service.ReadAsync(ActorId, eventId, ct);
        var household = page.Households.SingleOrDefault(x => x.Id == householdId);
        if (household is null) return NotFound();
        if (!householdId.HasValue)
        {
            var person = household.People.SingleOrDefault(x => x.PersonId == personId);
            if (person is null) return NotFound();
            household = new(null, person.Name, [person]);
        }
        return View(new LodgingPrintPage(page, household));
    });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Apartment(LodgingInput input, CancellationToken ct) => Save(input, async () =>
    { await service.SaveApartmentAsync(ActorId, input.EventId, input.Version, input.Id, input.Name ?? "", input.Notes, ct); });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Room(LodgingInput input, CancellationToken ct) => Save(input, async () =>
    { await service.SaveRoomAsync(ActorId, input.EventId, input.Version, input.Id, input.ApartmentId, input.Name ?? "", input.Capacity, input.Notes, ct); });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Person(LodgingInput input, CancellationToken ct) => Save(input, async () =>
    { await service.AssignPersonAsync(ActorId, input.EventId, input.Version, input.PersonId, input.RoomId, ct); });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Household(LodgingInput input, CancellationToken ct) => Save(input, async () =>
    { await service.AssignHouseholdAsync(ActorId, input.EventId, input.Version, input.HouseholdId,
            input.RoomId ?? throw new LocalValidationException("Seleziona una camera."), ct); });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> FinalizePlan(LodgingInput input, CancellationToken ct) => Save(input, async () =>
    {
        if (!input.Confirmed) throw new LocalValidationException("Conferma esplicitamente il piano definitivo.");
        await service.FinalizeAsync(ActorId, input.EventId, input.Version, ct);
    });

    private Task<IActionResult> Save(LodgingInput input, Func<Task> action) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest("Controlla i dati e ricarica il piano.");
        try { await action(); }
        catch (LocalValidationException ex) { TempData["LodgingError"] = ex.Message; }
        catch (LocalConflictException ex) { TempData["LodgingError"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { input.EventId });
    });
}
