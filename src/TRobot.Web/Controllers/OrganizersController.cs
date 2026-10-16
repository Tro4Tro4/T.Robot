using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Accounts;
using TRobot.Web.Models;

namespace TRobot.Web.Controllers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class OrganizersController(AccountService accounts) : Controller
{
    private Guid ActorId => Guid.TryParse(User.FindFirstValue(AccountClaims.PersonId), out var id) ? id : Guid.Empty;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try { return View(new AccountsViewModel(await accounts.GetDirectoryAsync(ActorId, ct), new())); }
        catch (AccountAccessException) { return Forbid(); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind(Prefix = "Input")] CreateOrganizerInput input, CancellationToken ct)
    {
        try
        {
            if (ModelState.IsValid)
            {
                await accounts.CreateAsync(ActorId, input.EventId, input.UserName, input.DisplayName, input.Password, ct);
                TempData["AccountNotice"] = "Account creato e organizzatore assegnato all'evento.";
                return RedirectToAction(nameof(Index));
            }
        }
        catch (AccountAccessException) { return Forbid(); }
        catch (AccountValidationException ex) { ModelState.AddModelError("", ex.Message); }
        input.Password = input.ConfirmPassword = "";
        ModelState.SetModelValue("Input.Password", null, null);
        ModelState.SetModelValue("Input.ConfirmPassword", null, null);
        try { return View("Index", new AccountsViewModel(await accounts.GetDirectoryAsync(ActorId, ct), input)); }
        catch (AccountAccessException) { return Forbid(); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> SetAssignment(AssignmentInput input, CancellationToken ct) => ApplyAsync(
        () => accounts.SetAssignmentAsync(ActorId, input.PersonId, input.EventId, input.Active!.Value, input.Confirmed, ct),
        "Assegnazione all'evento aggiornata.", ct);

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> SetEnabled(AccountEnabledInput input, CancellationToken ct) => ApplyAsync(
        () => accounts.SetEnabledAsync(ActorId, input.PersonId, input.Enabled!.Value, input.Confirmed, ct),
        "Accesso all'account aggiornato.", ct);

    private async Task<IActionResult> ApplyAsync(Func<Task<bool>> action, string message, CancellationToken ct)
    {
        try
        {
            if (ModelState.IsValid)
            {
                TempData["AccountNotice"] = await action() ? message : "Configurazione già impostata. Nessuna modifica.";
                return RedirectToAction(nameof(Index));
            }
        }
        catch (AccountAccessException) { return Forbid(); }
        catch (AccountValidationException ex) { ModelState.AddModelError("", ex.Message); }
        try { return View("Index", new AccountsViewModel(await accounts.GetDirectoryAsync(ActorId, ct), new())); }
        catch (AccountAccessException) { return Forbid(); }
    }
}
