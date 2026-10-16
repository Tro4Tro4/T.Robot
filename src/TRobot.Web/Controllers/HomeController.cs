using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Attendance;
using TRobot.Application.Accounts;
using TRobot.Application.Local;
using TRobot.Application.Simulation;
using TRobot.Web.Models;

namespace TRobot.Web.Controllers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class HomeController(AttendanceService attendance, LocalDashboardService localDashboard, SimulationCommandService simulator) : Controller
{
    private Guid ActorId => Guid.TryParse(User.FindFirstValue(AccountClaims.PersonId), out var id) ? id : Guid.Empty;

    [HttpGet]
    public async Task<IActionResult> Index(Guid? eventId, CancellationToken ct)
    {
        try { return View(await LoadAsync(eventId, null, ct)); }
        catch (AttendanceAccessException) { return Forbid(); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Simulate([Bind(Prefix = "Input")] SimulationInput input, CancellationToken ct)
    {
        try
        {
            if (ModelState.IsValid)
            {
                var result = await simulator.ExecuteAsync(ActorId, new(input.EventId, input.SenderId, input.MessageId, input.Text), input.ConfirmVote, ct);
                TempData["Notice"] = result.Reply;
                return RedirectToAction(nameof(Index), new { eventId = input.EventId });
            }
        }
        catch (AttendanceAccessException) { return Forbid(); }
        catch (AttendanceValidationException ex) { ModelState.AddModelError("", ex.Message); }
        catch (AttendanceConflictException ex) { ModelState.AddModelError("", ex.Message); }
        catch (LocalAccessException) { return Forbid(); }
        catch (LocalValidationException ex) { ModelState.AddModelError("", ex.Message); }
        catch (LocalConflictException ex) { ModelState.AddModelError("", ex.Message); }

        try { return View("Index", await LoadAsync(input.EventId, input, ct)); }
        catch (AttendanceAccessException) { return Forbid(); }
    }

    private async Task<DashboardViewModel> LoadAsync(Guid? eventId, SimulationInput? input, CancellationToken ct)
    {
        var events = await attendance.GetEventsAsync(ActorId, ct);
        var selected = eventId ?? events.FirstOrDefault()?.Id;
        var dashboard = selected.HasValue ? await attendance.GetDashboardAsync(ActorId, selected.Value, ct) : null;
        LocalOverview? overview = null;
        try { if (selected.HasValue) overview = await localDashboard.ReadAsync(ActorId, selected.Value, ct); }
        catch (LocalAccessException) { throw new AttendanceAccessException(); }
        return new(events, dashboard, input ?? new SimulationInput
        {
            EventId = selected ?? Guid.Empty, SenderId = dashboard?.Senders.FirstOrDefault()?.SenderId ?? ""
        }, overview);
    }

    [AllowAnonymous]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
