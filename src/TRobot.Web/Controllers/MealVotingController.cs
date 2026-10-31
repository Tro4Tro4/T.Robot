using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Local;
using TRobot.Application.Menu;
using TRobot.Web.Models;

namespace TRobot.Web.Controllers;

[Authorize]
public sealed class MealVotingController(MealVotingService service, MealProposalService proposals, ILocalStore store) : LocalControllerBase
{
    [HttpGet]
    public Task<IActionResult> Index(Guid eventId, CancellationToken ct, int historyPage = 1) => ExecuteAsync(async () =>
    {
        var page = await service.ReadAsync(ActorId, eventId, ct);
        return View(new MealVotingOrganizerPage(page, await proposals.ReadAsync(ActorId, eventId, ct),
            (await store.ParticipantsAsync(eventId, ct)).ToDictionary(x => x.Id, x => x.Name), await service.ReadHistoryAsync(ActorId, eventId, historyPage, ct)));
    });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Start(MealVotingInput input, CancellationToken ct) => Save(input, async () =>
        { await service.StartCycleAsync(ActorId, input.EventId, input.CycleId, ct); });
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Prepare(MealVotingInput input, CancellationToken ct) => Save(input, async () =>
        { await service.CloseCollectionAsync(ActorId, input.EventId, input.CycleId, input.Revision, ct); });
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Entry(MealVotingInput input, CancellationToken ct) => Save(input, async () =>
        { await service.SaveEntryAsync(ActorId, input.EventId, new(input.Id, input.CycleId, input.Name ?? "", input.ProposalIds,
            input.IsExcluded, input.Revision, input.EntryRevision), ct); });
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Open(MealVotingInput input, CancellationToken ct) => Save(input, async () =>
        { await service.OpenAsync(ActorId, input.EventId, input.CycleId, input.Revision, ct); });
    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Close(MealVotingInput input, CancellationToken ct) => Save(input, async () =>
        { await service.CloseAsync(ActorId, input.EventId, input.CycleId, input.Revision, ct); });

    private Task<IActionResult> Save(MealVotingInput input, Func<Task> action) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest("Controlla i campi e ricarica la pagina.");
        if (!input.Confirmed) throw new LocalValidationException("Conferma esplicitamente l'operazione.");
        try { await action(); }
        catch (LocalValidationException ex) { TempData["MealVotingError"] = ex.Message; }
        catch (LocalConflictException ex) { TempData["MealVotingError"] = ex.Message; }
        return RedirectToAction(nameof(Index), new { input.EventId });
    });
}
