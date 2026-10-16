using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Accounts;
using TRobot.Application.Local;

namespace TRobot.Web.Controllers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public abstract class LocalControllerBase : Controller
{
    protected Guid ActorId => Guid.TryParse(User.FindFirstValue(AccountClaims.PersonId), out var id) ? id : Guid.Empty;
    protected async Task<IActionResult> ExecuteAsync(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (LocalAccessException) { return Forbid(); }
        catch (LocalValidationException ex) { return BadRequest(ex.Message); }
        catch (LocalConflictException ex) { return Conflict(ex.Message); }
    }
}
