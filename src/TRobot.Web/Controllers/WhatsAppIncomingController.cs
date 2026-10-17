using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TRobot.Application.Local;
using TRobot.Application.WhatsApp;

namespace TRobot.Web.Controllers;

[ApiController, AllowAnonymous, Route("internal/whatsapp/incoming")]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class WhatsAppIncomingController(WhatsAppService service, WhatsAppGatewayOptions options) : ControllerBase
{
    [HttpPost, RequestSizeLimit(8192), EnableRateLimiting("whatsapp-incoming")]
    public async Task<IActionResult> Incoming(WhatsAppIncoming input, CancellationToken ct)
    {
        if (!options.Enabled) return NotFound();
        var ip = HttpContext.Connection.RemoteIpAddress;
        if (ip is null || !IPAddress.IsLoopback(ip)) return Unauthorized();
        var header = Request.Headers.Authorization.ToString();
        var expected = "Bearer " + options.Token;
        if (options.Token.Length != 64 || header.Length != expected.Length
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header), Encoding.UTF8.GetBytes(expected))) return Unauthorized();
        try { return Ok(await service.IncomingAsync(input, ct)); }
        catch (LocalAccessException) { return StatusCode(403); }
        catch (LocalConflictException) { return Conflict(new { handled = false, duplicate = false, reaction = (string?)null }); }
        catch (LocalValidationException) { return BadRequest(new { handled = false, duplicate = false, reaction = (string?)null }); }
    }
}
