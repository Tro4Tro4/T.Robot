using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Polls;
using TRobot.Domain;
using TRobot.Web.Models;

namespace TRobot.Web.Controllers;

public sealed class PollsController(PollService service) : LocalControllerBase
{
    [HttpGet] public Task<IActionResult> Index(Guid eventId, CancellationToken ct) => ExecuteAsync(async () => View(await service.ListAsync(ActorId, eventId, ct)));
    [HttpGet] public Task<IActionResult> Details(Guid eventId, Guid id, CancellationToken ct) => ExecuteAsync(async () => View(await service.ReadAsync(ActorId, eventId, id, ct)));
    [HttpGet] public Task<IActionResult> Create(Guid eventId, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var page = await service.ListAsync(ActorId, eventId, ct);
        return View("Edit", new PollDraftPage(new PollDraftInput { EventId = eventId, OperationId = Guid.NewGuid(), Revision = 0 }, page.Event.Name, page.Event.TimeZoneId));
    });
    [HttpGet] public Task<IActionResult> Edit(Guid eventId, Guid id, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var page = await service.ReadAsync(ActorId, eventId, id, ct);
        if (page.Poll.Status != PollStatus.Draft) return Conflict("Solo le bozze sono modificabili.");
        return View(new PollDraftPage(new PollDraftInput { EventId = eventId, Id = id, OperationId = id, Title = page.Poll.Title,
            DeadlineLocal = DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(page.Poll.DeadlineUtc, TimeZoneInfo.FindSystemTimeZoneById(page.Event.TimeZoneId)), DateTimeKind.Unspecified),
            Revision = page.Poll.Revision }, page.Event.Name, page.Event.TimeZoneId));
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Save(PollDraftInput input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest("Titolo, scadenza, identificativo e revisione sono obbligatori.");
        var id = await service.SaveAsync(ActorId, input.EventId!.Value, input.Id, input.ToEdit(), ct);
        return RedirectToAction(nameof(Details), new { eventId = input.EventId.Value, id });
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Publish(PollActionInput input, CancellationToken ct) => Act(input,
        () => service.PublishAsync(ActorId, input.EventId!.Value, input.Id!.Value, input.Revision!.Value, input.Confirmed, ct));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Vote(PollActionInput input, CancellationToken ct) => Act(input, async () =>
    {
        if (!input.OperationId.HasValue || !input.PersonId.HasValue || !input.OptionId.HasValue) throw new Application.Local.LocalValidationException("Persona, opzione e identificativo voto sono obbligatori.");
        await service.VoteAsync(ActorId, input.EventId!.Value, input.Id!.Value, new PollBallot(input.OperationId.Value, input.PersonId.Value, input.OptionId.Value, input.Revision!.Value, input.Confirmed), ct);
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Close(PollActionInput input, CancellationToken ct) => Act(input,
        () => service.CloseAsync(ActorId, input.EventId!.Value, input.Id!.Value, input.Revision!.Value, input.Confirmed, input.DecisionOptionId, ct));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> QueueResult(PollActionInput input, CancellationToken ct) => Act(input,
        () => service.QueueResultAsync(ActorId, input.EventId!.Value, input.Id!.Value, input.Revision!.Value, input.Confirmed, ct));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Cancel(PollActionInput input, CancellationToken ct) => Act(input,
        () => service.CancelAsync(ActorId, input.EventId!.Value, input.Id!.Value, input.Revision!.Value, input.Confirmed, ct));
    private Task<IActionResult> Act(PollActionInput input, Func<Task> action) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest("Evento, sondaggio e revisione sono obbligatori.");
        await action(); return RedirectToAction(nameof(Details), new { eventId = input.EventId!.Value, id = input.Id!.Value });
    });
}
