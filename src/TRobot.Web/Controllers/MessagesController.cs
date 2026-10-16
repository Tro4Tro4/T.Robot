using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Messaging;
using TRobot.Web.Models;

namespace TRobot.Web.Controllers;

public sealed class MessagesController(MessageService service) : LocalControllerBase
{
    [HttpGet] public Task<IActionResult> Index(Guid eventId, CancellationToken ct) => ExecuteAsync(async () =>
        View(await service.ListAsync(ActorId, eventId, ct)));
    [HttpGet] public Task<IActionResult> Create(Guid eventId, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var page = await service.ListAsync(ActorId, eventId, ct);
        return View("Edit", new MessagePage(new MessageInput { EventId = eventId, OperationId = Guid.NewGuid() }, page.Event.Name, page.Event.TimeZoneId));
    });
    [HttpGet] public Task<IActionResult> Edit(Guid eventId, Guid id, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var page = await service.ListAsync(ActorId, eventId, ct);
        var message = page.Messages.SingleOrDefault(x => x.Id == id);
        if (message is null) return NotFound();
        if (message.Status != Domain.OutboundMessageStatus.Draft) return Conflict("Solo bozze modificabili.");
        return View(new MessagePage(new MessageInput { EventId = eventId, Id = id, Body = message.Body, Kind = message.Kind, Revision = message.Revision,
            ScheduledLocal = TimeZoneInfo.ConvertTimeFromUtc(message.ScheduledAtUtc, TimeZoneInfo.FindSystemTimeZoneById(page.Event.TimeZoneId)) }, page.Event.Name, page.Event.TimeZoneId));
    });
    [HttpGet] public Task<IActionResult> Preview(Guid eventId, Guid id, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var page = await service.ListAsync(ActorId, eventId, ct);
        var message = page.Messages.SingleOrDefault(x => x.Id == id);
        if (message is null) return NotFound();
        ViewData["EventName"] = page.Event.Name;
        return View(message);
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Save(MessageInput input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        await service.ListAsync(ActorId, input.EventId, ct);
        if (!ModelState.IsValid) return BadRequest("Controlla testo, categoria e data.");
        var id = await service.SaveDraftAsync(ActorId, input.EventId, input.Id == Guid.Empty ? null : input.Id, input.ToEdit(), ct);
        return RedirectToAction(nameof(Preview), new { eventId = input.EventId, id });
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Transition(MessageTransitionInput input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        await service.ListAsync(ActorId, input.EventId, ct);
        if (!ModelState.IsValid) return BadRequest("Operazione non valida.");
        await service.TransitionAsync(ActorId, input.EventId, input.Id, input.Revision, input.Confirmed, input.Cancel, ct);
        return RedirectToAction(nameof(Index), new { eventId = input.EventId });
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Configure(MessageSettingsInput input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        await service.ListAsync(ActorId, input.EventId, ct);
        if (!ModelState.IsValid) return BadRequest("Configurazione non valida.");
        await service.ConfigureAsync(ActorId, input.EventId, input.ToEdit(), ct);
        return RedirectToAction(nameof(Index), new { eventId = input.EventId });
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Tick(Guid eventId, bool confirmed, CancellationToken ct) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest("Simulazione non valida.");
        var result = await service.TickAsync(ActorId, eventId, confirmed, ct);
        TempData["MessagesResult"] = $"Invii simulati: {result.Sent}. Nuovo riepilogo: {(result.SummaryCreated ? "sì" : "no")}.";
        return RedirectToAction(nameof(Index), new { eventId });
    });
}
