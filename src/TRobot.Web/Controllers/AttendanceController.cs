using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Accounts;
using TRobot.Application.Attendance;
using TRobot.Domain;
using TRobot.Web.Models;

namespace TRobot.Web.Controllers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AttendanceController(OrganizerAttendanceService attendance) : Controller
{
    private Guid ActorId => Guid.TryParse(User.FindFirstValue(AccountClaims.PersonId), out var id) ? id : Guid.Empty;

    [HttpGet]
    public async Task<IActionResult> Edit(Guid eventId, Guid personId, CancellationToken ct)
    {
        try { return View(await LoadAsync(eventId, personId, null, ct)); }
        catch (AttendanceAccessException) { return Forbid(); }
        catch (AttendanceValidationException) { return NotFound(); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Set(Guid eventId, [Bind(Prefix = "Input")] OrganizerAttendanceInput input, CancellationToken ct)
    {
        try
        {
            if (ModelState.IsValid)
            {
                var result = await attendance.SetAsync(ActorId, new(eventId, input.PersonId, input.OperationId,
                    input.Status!.Value, input.ExpectedVersion, input.Confirmed), ct);
                TempData["EventNotice"] = result.Duplicate ? "Operazione già registrata: nessun duplicato."
                    : result.Changed ? "Presenza registrata dall'organizzatore. Conteggi aggiornati."
                    : "Stato già impostato. Operazione registrata, conteggi invariati.";
                return RedirectToAction("Details", "Events", new { id = eventId });
            }
        }
        catch (AttendanceAccessException) { return Forbid(); }
        catch (AttendanceValidationException ex) { ModelState.AddModelError("", ex.Message); }
        catch (AttendanceConflictException ex) { ModelState.AddModelError("", ex.Message); }
        try { return View("Edit", await LoadAsync(eventId, input.PersonId, input, ct)); }
        catch (AttendanceAccessException) { return Forbid(); }
        catch (AttendanceValidationException) { return NotFound(); }
    }

    private async Task<OrganizerAttendanceViewModel> LoadAsync(Guid eventId, Guid personId, OrganizerAttendanceInput? input, CancellationToken ct)
    {
        var participant = await attendance.GetAsync(ActorId, eventId, personId, ct);
        return new(participant, input ?? new()
        {
            PersonId = personId, OperationId = Guid.NewGuid(), ExpectedVersion = participant.Version,
            Status = participant.Status == ParticipationStatus.NoResponse ? null : participant.Status
        });
    }
}
