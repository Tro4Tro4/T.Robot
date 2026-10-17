using Microsoft.AspNetCore.Mvc;
using TRobot.Application.WhatsApp;
using TRobot.Web.Models;

namespace TRobot.Web.Controllers;

public sealed class WhatsAppController(WhatsAppService service) : LocalControllerBase
{
    [HttpGet] public Task<IActionResult> Index(Guid eventId, CancellationToken ct) => ExecuteAsync(async () => View(await service.ReadAsync(ActorId, eventId, ct)));
    [HttpGet] public Task<IActionResult> Status(Guid eventId, CancellationToken ct) => ExecuteAsync(async () => Json(await service.HealthAsync(ActorId, eventId, ct)));
    [HttpGet] public Task<IActionResult> Groups(Guid eventId, CancellationToken ct) => ExecuteAsync(async () => Json(await service.GroupsAsync(ActorId, eventId, ct)));
    [HttpGet] public Task<IActionResult> Members(Guid eventId, CancellationToken ct) => ExecuteAsync(async () => Json(await service.MembersAsync(ActorId, eventId, ct)));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Connect(WhatsAppActionInput input, CancellationToken ct) => ExecuteAsync(async () =>
    { if (!ModelState.IsValid) return BadRequest(); await service.ConnectAsync(ActorId, input.EventId, input.Confirmed, ct); return RedirectToAction(nameof(Index), new { input.EventId }); });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Disconnect(WhatsAppActionInput input, CancellationToken ct) => ExecuteAsync(async () =>
    { if (!ModelState.IsValid) return BadRequest(); await service.DisconnectAsync(ActorId, input.EventId, input.Confirmed, ct); return RedirectToAction(nameof(Index), new { input.EventId }); });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> SelectGroup(WhatsAppGroupInput input, CancellationToken ct) => ExecuteAsync(async () =>
    { if (!ModelState.IsValid) return BadRequest(); await service.SelectGroupAsync(ActorId, input.EventId, input.GroupId, input.Revision, input.Confirmed, ct); return RedirectToAction(nameof(Index), new { input.EventId }); });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Map(WhatsAppMapInput input, CancellationToken ct) => ExecuteAsync(async () =>
    { if (!ModelState.IsValid) return BadRequest(); await service.MapAsync(ActorId, input.EventId, input.MemberId, input.PersonId, input.Revision, input.Confirmed, ct); return RedirectToAction(nameof(Index), new { input.EventId }); });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Draft(WhatsAppDraftInput input, CancellationToken ct) => ExecuteAsync(async () =>
    { if (!ModelState.IsValid) return BadRequest(); var id = await service.DraftAsync(ActorId, input.EventId, input.OperationId, input.Body, ct); return RedirectToAction(nameof(Preview), new { input.EventId, id }); });
    [HttpGet] public Task<IActionResult> Preview(Guid eventId, Guid id, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var page = await service.ReadAsync(ActorId, eventId, ct);
        var message = page.Messages.SingleOrDefault(x => x.Id == id);
        return message is null ? NotFound() : View(new WhatsAppPreview(page.Event.Name, message));
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Send(WhatsAppSendInput input, CancellationToken ct) => ExecuteAsync(async () =>
    { if (!ModelState.IsValid) return BadRequest(); await service.SendAsync(ActorId, input.EventId, input.Id, input.Revision, input.Confirmed, ct); return RedirectToAction(nameof(Preview), new { input.EventId, id = input.Id }); });
}
