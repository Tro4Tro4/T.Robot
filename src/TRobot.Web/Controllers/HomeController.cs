using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Attendance;
using TRobot.Application.Accounts;
using TRobot.Domain;
using TRobot.Web.Models;

namespace TRobot.Web.Controllers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class HomeController(AttendanceService attendance) : Controller
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
                var result = await attendance.SimulateAsync(ActorId, new(input.EventId, input.SenderId, input.MessageId, input.Text), ct);
                var label = result.Status switch
                {
                    ParticipationStatus.Confirmed => "Confermato",
                    ParticipationStatus.Undecided => "Indeciso",
                    ParticipationStatus.Absent => "Assente",
                    _ => "Senza risposta"
                };
                TempData["Notice"] = result.Duplicate ? "Messaggio già elaborato: nessun duplicato."
                    : result.Changed ? $"Stato aggiornato: {label}. Conteggi aggiornati."
                    : $"Stato già impostato: {label}. Conteggi invariati.";
                return RedirectToAction(nameof(Index), new { eventId = input.EventId });
            }
        }
        catch (AttendanceAccessException) { return Forbid(); }
        catch (AttendanceValidationException ex) { ModelState.AddModelError("", ex.Message); }
        catch (AttendanceConflictException ex) { ModelState.AddModelError("", ex.Message); }

        try { return View("Index", await LoadAsync(input.EventId, input, ct)); }
        catch (AttendanceAccessException) { return Forbid(); }
    }

    private async Task<DashboardViewModel> LoadAsync(Guid? eventId, SimulationInput? input, CancellationToken ct)
    {
        var events = await attendance.GetEventsAsync(ActorId, ct);
        var selected = eventId ?? events.FirstOrDefault()?.Id;
        var dashboard = selected.HasValue ? await attendance.GetDashboardAsync(ActorId, selected.Value, ct) : null;
        return new(events, dashboard, input ?? new SimulationInput
        {
            EventId = selected ?? Guid.Empty, SenderId = dashboard?.Senders.FirstOrDefault()?.SenderId ?? ""
        });
    }

    [AllowAnonymous]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
