using Microsoft.AspNetCore.Mvc;
using TRobot.Application.WhatsApp;
using TRobot.Web.Models;

namespace TRobot.Web.Controllers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class StaysController(WhatsAppService service) : LocalControllerBase
{
    [HttpGet]
    public Task<IActionResult> Index(Guid eventId, CancellationToken ct) => ExecuteAsync(async () =>
        View(await service.AttendanceOverviewAsync(ActorId, eventId, ct)));

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Deadline(AttendanceDeadlineInput input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest();
        await service.SetAttendanceDeadlineAsync(ActorId, input.EventId, input.DeadlineLocal, input.Revision, input.Confirmed, ct);
        return RedirectToAction(nameof(Index), new { input.EventId });
    });

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Reminder(AttendanceReminderInput input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest();
        var id = await service.DraftAttendanceReminderAsync(ActorId, input.EventId, input.OperationId, ct);
        return RedirectToAction("Preview", "WhatsApp", new { input.EventId, id });
    });
}
