using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Accounts;
using TRobot.Application.Attendance;
using TRobot.Application.Events;
using TRobot.Web.Models;

namespace TRobot.Web.Controllers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class EventsController(EventManagementService events, AttendanceService attendance, ParticipantNameService names) : Controller
{
    private Guid ActorId => Guid.TryParse(User.FindFirstValue(AccountClaims.PersonId), out var id) ? id : Guid.Empty;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(new EventsViewModel(await attendance.GetEventsAsync(ActorId, ct), await events.CanCreateAsync(ActorId, ct)));

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct) =>
        await events.CanCreateAsync(ActorId, ct) ? View(new EventEditorViewModel(new() { Revision = 0 })) : Forbid();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] EventFormInput input, CancellationToken ct)
    {
        if (!await events.CanCreateAsync(ActorId, ct)) return Forbid();
        try
        {
            if (ModelState.IsValid)
            {
                var id = await events.CreateAsync(ActorId, input.ToCommand(), ct);
                TempData["EventNotice"] = "Evento creato. Sei stato assegnato come organizzatore.";
                return RedirectToAction(nameof(Details), new { id });
            }
        }
        catch (EventAccessException) { return Forbid(); }
        catch (EventValidationException ex) { ModelState.AddModelError("", ex.Message); }
        catch (EventConflictException ex) { ModelState.AddModelError("", ex.Message); }
        return View(new EventEditorViewModel(input));
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        try { return View(await LoadAsync(id, null, null, ct)); }
        catch (EventAccessException) { return Forbid(); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(Guid id, [Bind(Prefix = "Input")] EventFormInput input, CancellationToken ct)
    {
        try
        {
            if (ModelState.IsValid)
            {
                var changed = await events.UpdateAsync(ActorId, id, input.Revision!.Value, input.ToCommand(), ct);
                TempData["EventNotice"] = changed ? "Evento aggiornato." : "Dati già impostati. Nessuna modifica.";
                return RedirectToAction(nameof(Details), new { id });
            }
        }
        catch (EventAccessException) { return Forbid(); }
        catch (EventValidationException ex) { ModelState.AddModelError("", ex.Message); }
        catch (EventConflictException ex) { ModelState.AddModelError("", ex.Message); }
        try { return View("Details", await LoadAsync(id, input, null, ct)); }
        catch (EventAccessException) { return Forbid(); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AddParticipant(Guid id, [Bind(Prefix = "Participant")] AddParticipantInput input, CancellationToken ct)
    {
        try
        {
            if (ModelState.IsValid)
            {
                await events.AddParticipantAsync(ActorId, id, input.OperationId, input.DisplayName, input.EnableSimulation, ct);
                TempData["EventNotice"] = "Partecipante registrato con stato iniziale Senza risposta.";
                return RedirectToAction(nameof(Details), new { id });
            }
        }
        catch (EventAccessException) { return Forbid(); }
        catch (EventValidationException ex) { ModelState.AddModelError("", ex.Message); }
        catch (EventConflictException ex) { ModelState.AddModelError("", ex.Message); }
        try { return View("Details", await LoadAsync(id, null, input, ct)); }
        catch (EventAccessException) { return Forbid(); }
    }

    [HttpGet]
    public async Task<IActionResult> EditParticipant(Guid id, Guid personId, CancellationToken ct)
    {
        try { return View(await LoadNameAsync(id, personId, null, ct)); }
        catch (EventAccessException) { return Forbid(); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateParticipant(Guid id, [Bind(Prefix = "Input")] ParticipantNameInput input, CancellationToken ct)
    {
        try
        {
            if (ModelState.IsValid)
            {
                var changed = await names.RenameAsync(ActorId, id, input.PersonId, input.Revision!.Value,
                    input.DisplayName, input.ConfirmedShared, ct);
                TempData["EventNotice"] = changed ? "Nome corretto. Identità, presenze e storico conservati." : "Nome già impostato. Nessuna modifica.";
                return RedirectToAction(nameof(Details), new { id });
            }
        }
        catch (EventAccessException) { return Forbid(); }
        catch (EventValidationException ex) { ModelState.AddModelError("", ex.Message); }
        catch (EventConflictException ex) { ModelState.AddModelError("", ex.Message); }
        try { return View("EditParticipant", await LoadNameAsync(id, input.PersonId, input, ct)); }
        catch (EventAccessException) { return Forbid(); }
    }

    private async Task<ParticipantNameViewModel> LoadNameAsync(Guid eventId, Guid personId, ParticipantNameInput? input, CancellationToken ct)
    {
        var editor = await names.GetAsync(ActorId, eventId, personId, ct);
        return new(editor, input ?? new() { PersonId = editor.PersonId, DisplayName = editor.DisplayName, Revision = editor.Revision });
    }

    private async Task<EventDetailsViewModel> LoadAsync(Guid id, EventFormInput? edit, AddParticipantInput? participant, CancellationToken ct)
    {
        var details = await events.GetAsync(ActorId, id, ct);
        return new(details, edit ?? EventFormInput.From(details.Event), participant ?? new() { OperationId = Guid.NewGuid() });
    }
}
