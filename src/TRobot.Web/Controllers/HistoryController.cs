using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Accounts;
using TRobot.Application.History;

namespace TRobot.Web.Controllers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class HistoryController(EventHistoryService history) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(Guid eventId, HistoryCategory category = HistoryCategory.All, int page = 1, CancellationToken ct = default)
    {
        var actorId = Guid.TryParse(User.FindFirstValue(AccountClaims.PersonId), out var id) ? id : Guid.Empty;
        try
        {
            if (!ModelState.IsValid) return BadRequest("Filtro o pagina non validi.");
            return View(await history.GetAsync(actorId, eventId, category, page, ct));
        }
        catch (HistoryAccessException) { return Forbid(); }
        catch (HistoryValidationException ex) { return BadRequest(ex.Message); }
    }
}
