using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Menu;

namespace TRobot.Web.Controllers;

[Authorize]
public sealed class MealProposalsController(MealProposalService service) : LocalControllerBase
{
    [HttpGet]
    public Task<IActionResult> Index(Guid eventId, CancellationToken ct) => ExecuteAsync(async () =>
        View(await service.ReadAsync(ActorId, eventId, ct)));
}
