using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TRobot.Application.Accounts;
using TRobot.Infrastructure.Identity;
using TRobot.Web.Models;

namespace TRobot.Web.Controllers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AccessController(SignInManager<OrganizerUser> signIn, UserManager<OrganizerUser> users, AccountService accounts) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Login() => View(new LoginInput());

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginInput input)
    {
        if (ModelState.IsValid)
        {
            var result = await signIn.PasswordSignInAsync(input.UserName.Trim(), input.Password, isPersistent: false, lockoutOnFailure: true);
            if (result.Succeeded) return RedirectToAction("Index", "Home");
        }
        ModelState.Clear();
        ModelState.AddModelError("", "Credenziali non valide o account temporaneamente bloccato.");
        return View(new LoginInput { UserName = input.UserName });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordInput());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordInput input, CancellationToken ct)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var actorId = Guid.TryParse(User.FindFirstValue(AccountClaims.PersonId), out var id) ? id : Guid.Empty;
                await accounts.ChangePasswordAsync(actorId, input.CurrentPassword, input.NewPassword, ct);
                var user = await users.GetUserAsync(User);
                if (user is null) return Forbid();
                await signIn.RefreshSignInAsync(user);
                TempData["Notice"] = "Password aggiornata. Le altre sessioni sono state invalidate.";
                return RedirectToAction("Index", "Home");
            }
            catch (AccountAccessException) { return Forbid(); }
            catch (AccountValidationException ex) { ModelState.AddModelError("", ex.Message); }
        }
        input.CurrentPassword = input.NewPassword = input.ConfirmPassword = "";
        ModelState.SetModelValue(nameof(input.CurrentPassword), null, null);
        ModelState.SetModelValue(nameof(input.NewPassword), null, null);
        ModelState.SetModelValue(nameof(input.ConfirmPassword), null, null);
        return View(input);
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Denied() => StatusCode(StatusCodes.Status403Forbidden);
}
