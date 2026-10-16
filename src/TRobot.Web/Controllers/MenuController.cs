using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Local;
using TRobot.Application.Menu;
using TRobot.Domain;
using TRobot.Web.Models;

namespace TRobot.Web.Controllers;

public sealed class MenuController(MenuService service) : LocalControllerBase
{
    [HttpGet]
    public Task<IActionResult> Index(Guid eventId, CancellationToken ct) => ExecuteAsync(async () =>
        View(new MenuViewModel(await service.ReadAsync(ActorId, eventId, ct), new MenuForm
        { Id = Guid.NewGuid(), Quantity = "1", Category = MenuCategory.Food, Availability = MenuAvailability.Planned }, false)));

    [HttpGet]
    public Task<IActionResult> Edit(Guid eventId, Guid id, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var page = await service.ReadAsync(ActorId, eventId, ct);
        var item = page.Items.SingleOrDefault(x => x.Id == id);
        if (item is null) return NotFound();
        return View("Index", new MenuViewModel(page, new MenuForm { Id = item.Id, Name = item.Name, Category = item.Category,
            Quantity = item.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture), Unit = item.Unit, ResponsiblePersonId = item.ResponsiblePersonId,
            Availability = item.Availability, Notes = item.Notes, Revision = item.Revision }, true));
    });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Create(Guid eventId, [Bind(Prefix = "Input")] MenuForm input, CancellationToken ct) => Save(eventId, input, true, ct);
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Update(Guid eventId, [Bind(Prefix = "Input")] MenuForm input, CancellationToken ct) => Save(eventId, input, false, ct);

    private Task<IActionResult> Save(Guid eventId, MenuForm input, bool create, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var page = await service.ReadAsync(ActorId, eventId, ct);
        if (!create && !input.Revision.HasValue) ModelState.AddModelError("Input.Revision", "Revisione obbligatoria.");
        if (!ModelState.IsValid) return View("Index", new MenuViewModel(page, input, !create));
        try { await service.SaveAsync(ActorId, eventId, input.ToInput(), create, ct); }
        catch (LocalValidationException ex)
        { ModelState.AddModelError("", ex.Message); return View("Index", new MenuViewModel(page, input, !create)); }
        return RedirectToAction(nameof(Index), new { eventId });
    });
}
