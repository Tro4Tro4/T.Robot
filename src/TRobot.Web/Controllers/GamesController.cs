using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Games;
using TRobot.Application.Local;
using TRobot.Web.Models;

namespace TRobot.Web.Controllers;

public sealed class GamesController(GameService service) : LocalControllerBase
{
    [HttpGet] public Task<IActionResult> Index(Guid eventId, CancellationToken ct) => ExecuteAsync(async () => View(await service.ListAsync(ActorId, eventId, ct)));
    [HttpGet] public Task<IActionResult> Details(Guid eventId, Guid id, CancellationToken ct) => ExecuteAsync(async () => View(await service.ReadAsync(ActorId, eventId, id, ct)));
    [HttpGet] public Task<IActionResult> Create(Guid eventId, CancellationToken ct) => ExecuteAsync(async () =>
    {
        var page = await service.ListAsync(ActorId, eventId, ct);
        return View(new QuizDraftPage(new QuizDraftInput { EventId = eventId, OperationId = Guid.NewGuid() }, page.Event.Name, page.Event.TimeZoneId));
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Create(QuizDraftInput input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest("Domanda, opzioni, posizione corretta e scadenza sono obbligatori.");
        var id = await service.CreateAsync(ActorId, input.EventId!.Value, input.ToDraft(), ct);
        return RedirectToAction(nameof(Details), new { eventId = input.EventId.Value, id });
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Membership(GameMembershipInput input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest("Evento, persona e revisione sono obbligatori.");
        await service.MembershipAsync(ActorId, input.EventId!.Value, input.PersonId!.Value, input.Revision!.Value, input.Active, input.Confirmed, ct);
        return RedirectToAction(nameof(Index), new { eventId = input.EventId.Value });
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Publish(GameActionInput input, CancellationToken ct) => Act(input,
        () => service.PublishAsync(ActorId, input.EventId!.Value, input.Id!.Value, input.Revision!.Value, input.Confirmed, ct));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Close(GameActionInput input, CancellationToken ct) => Act(input,
        () => service.CloseAsync(ActorId, input.EventId!.Value, input.Id!.Value, input.Revision!.Value, input.Confirmed, ct));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Cancel(GameActionInput input, CancellationToken ct) => Act(input,
        () => service.CancelAsync(ActorId, input.EventId!.Value, input.Id!.Value, input.Revision!.Value, input.Confirmed, ct));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Answer(GameActionInput input, CancellationToken ct) => Act(input, async () =>
    {
        if (!input.OperationId.HasValue || !input.PersonId.HasValue || !input.OptionId.HasValue) throw new LocalValidationException("Persona, opzione e identificativo sono obbligatori.");
        await service.AnswerAsync(ActorId, input.EventId!.Value, input.Id!.Value, new(input.OperationId.Value, input.PersonId.Value, input.OptionId.Value, input.Revision!.Value, input.Confirmed), ct);
    });
    private Task<IActionResult> Act(GameActionInput input, Func<Task> action) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest("Evento, quiz e revisione sono obbligatori.");
        await action(); return RedirectToAction(nameof(Details), new { eventId = input.EventId!.Value, id = input.Id!.Value });
    });
}
