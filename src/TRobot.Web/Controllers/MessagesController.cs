using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Messaging;
using TRobot.Web.Models;
using TRobot.Web.Services;

namespace TRobot.Web.Controllers;

public sealed class MessagesController(MessageService service, IConfiguration configuration) : LocalControllerBase
{
    [HttpGet] public Task<IActionResult> Index(Guid eventId, CancellationToken ct) => ExecuteAsync(async () =>
    {
        ViewData["LocalSchedulerEnabled"] = configuration.GetValue("LocalScheduler:Enabled", true);
        ViewData["DispatcherEnabled"] = MessageDispatchWorker.IsEnabled(configuration);
        return View(await service.ListAsync(ActorId, eventId, ct));
    });
    [HttpGet] public Task<IActionResult> Status(Guid eventId, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var status = await service.DeliveryStatusAsync(ActorId, eventId, ct);
        // Only safe diagnostics: no QR data, no group name and no gateway error payload.
        return Json(new
        {
            gatewayState = status.GatewayState, connectionActive = status.ActiveGroupId is not null,
            group = MessageLabels.Group(status.ActiveGroupId), groupMatches = status.GroupMatches
        });
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Automation(MessageAutomationInput input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        await service.ListAsync(ActorId, input.EventId, ct);
        if (!ModelState.IsValid) return BadRequest("Configurazione automatica non valida.");
        var changed = await service.ConfigureAutomaticAsync(ActorId, input.EventId, input.Enabled, input.Revision, input.Confirmed, ct);
        TempData["MessagesResult"] = changed
            ? input.Enabled ? "Scheduler automatico locale abilitato per questo evento." : "Scheduler automatico locale disattivato per questo evento."
            : "Configurazione automatica già impostata: nessuna modifica.";
        return RedirectToAction(nameof(Index), new { eventId = input.EventId });
    });
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
        ViewData["TimeZoneId"] = page.Event.TimeZoneId;
        ViewData["GatewayEnabled"] = page.GatewayEnabled;
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
        return RedirectToAction(input.Cancel ? nameof(Index) : nameof(Preview), new { eventId = input.EventId, id = input.Id });
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Dispatch(Guid eventId, bool confirmed, CancellationToken ct) => ExecuteAsync(async () =>
    {
        if (!confirmed) return BadRequest("Conferma l'elaborazione dei messaggi già confermati.");
        var outcome = await service.DispatchAsync(ActorId, eventId, ct);
        TempData["MessagesResult"] = $"Elaborazione: {outcome.Sent} inviati, {outcome.Failed} falliti, {outcome.Uncertain} incerti, {outcome.Skipped} in attesa, {outcome.Recovered} recuperati.";
        return RedirectToAction(nameof(Index), new { eventId });
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Resolve(MessageResolveInput input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        await service.ListAsync(ActorId, input.EventId, ct);
        if (!ModelState.IsValid) return BadRequest("Risoluzione non valida: indica come hai verificato l'esito.");
        await service.ResolveAsync(ActorId, input.EventId, input.Id, input.Revision, input.AsSent, input.Note, input.Confirmed, ct);
        TempData["MessagesResult"] = "Esito registrato manualmente.";
        return RedirectToAction(nameof(Preview), new { eventId = input.EventId, id = input.Id });
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
