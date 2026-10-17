using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Accounts;
using TRobot.Application.Local;
using TRobot.Application.WhatsApp;

namespace TRobot.Web.Controllers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class StaysController(WhatsAppService service) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(Guid eventId, CancellationToken ct)
    {
        var actor = Guid.TryParse(User.FindFirstValue(AccountClaims.PersonId), out var id) ? id : Guid.Empty;
        try { return View(await service.AttendanceOverviewAsync(actor, eventId, ct)); }
        catch (LocalAccessException) { return Forbid(); }
    }
}
