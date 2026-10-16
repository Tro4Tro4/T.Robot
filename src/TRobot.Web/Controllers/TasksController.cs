using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Tasks;

namespace TRobot.Web.Controllers;

public sealed class TasksController(TaskService service) : LocalControllerBase
{
    [HttpGet] public Task<IActionResult> Index(Guid eventId, CancellationToken ct) => ExecuteAsync(async () =>
        View(await service.ListAsync(ActorId, eventId, ct)));

    [HttpGet] public Task<IActionResult> Create(Guid eventId, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var page = await service.ListAsync(ActorId, eventId, ct);
        return View("Edit", new Models.TaskPage(new Models.TaskInput { EventId = eventId, OperationId = Guid.NewGuid() }, page.Event.Name, page.Event.TimeZoneId, page.People));
    });
    [HttpGet] public Task<IActionResult> Edit(Guid eventId, Guid id, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var page = await service.ListAsync(ActorId, eventId, ct);
        var item = page.Tasks.SingleOrDefault(x => x.Id == id);
        if (item is null) return NotFound();
        var local = item.DueAtUtc.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(item.DueAtUtc.Value, TimeZoneInfo.FindSystemTimeZoneById(page.Event.TimeZoneId)) : (DateTime?)null;
        return View(new Models.TaskPage(new Models.TaskInput { EventId = eventId, Id = id, Title = item.Title, Description = item.Description,
            Priority = item.Priority, Status = item.Status, AssigneeId = item.AssigneeId, DueLocal = local, Revision = item.Revision }, page.Event.Name, page.Event.TimeZoneId, page.People));
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Save(Models.TaskInput input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        // Authorize before exposing model validation details for an event.
        await service.ListAsync(ActorId, input.EventId, ct);
        if (!ModelState.IsValid) return BadRequest("Controlla titolo, descrizione e valori inseriti.");
        if (input.Id == Guid.Empty) await service.CreateAsync(ActorId, input.EventId, input.ToEdit(), ct);
        else await service.UpdateAsync(ActorId, input.EventId, input.Id, input.ToEdit(), ct);
        return RedirectToAction(nameof(Index), new { eventId = input.EventId });
    });
}
