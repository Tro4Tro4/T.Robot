using Microsoft.AspNetCore.Mvc;
using TRobot.Application.Accounting;
using TRobot.Web.Models;

namespace TRobot.Web.Controllers;

public sealed class AccountingController(AccountingService service) : LocalControllerBase
{
    [HttpGet] public Task<IActionResult> Index(Guid eventId, CancellationToken ct) => ExecuteAsync(async () =>
        View(await service.ListAsync(ActorId, eventId, ct)));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> DeclareExpense(ExpenseForm input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest("Controlla partecipante, descrizione e importo in EUR con massimo due decimali.");
        await service.DeclareExpenseAsync(ActorId, input.EventId, input.ToInput(), ct);
        return RedirectToAction(nameof(Index), new { eventId = input.EventId });
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> DeclarePayment(PaymentForm input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest("Controlla partecipanti e importo in EUR con massimo due decimali.");
        await service.DeclarePaymentAsync(ActorId, input.EventId, input.ToInput(), ct);
        return RedirectToAction(nameof(Index), new { eventId = input.EventId });
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> ExpenseTransition(ExpenseTransitionForm input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest("Operazione spesa non valida.");
        await service.ExpenseTransitionAsync(ActorId, input.EventId, input.Id!.Value, input.Revision!.Value, input.Target!.Value, input.Confirmed, ct);
        return RedirectToAction(nameof(Index), new { eventId = input.EventId });
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> PaymentTransition(PaymentTransitionForm input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest("Operazione pagamento non valida.");
        await service.PaymentTransitionAsync(ActorId, input.EventId, input.Id!.Value, input.Revision!.Value, input.Target!.Value, input.Confirmed, ct);
        return RedirectToAction(nameof(Index), new { eventId = input.EventId });
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Configure(AccountingSettingsForm input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest("Peso bambino non valido: usa punto o virgola, massimo tre decimali.");
        await service.ConfigureAsync(ActorId, input.EventId, AccountingInput.Number(input.ChildWeight), input.Revision!.Value, input.Confirmed, ct);
        return RedirectToAction(nameof(Index), new { eventId = input.EventId });
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Override(WeightForm input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest("Peso individuale non valido: usa punto o virgola, massimo tre decimali.");
        await service.OverrideAsync(ActorId, input.EventId, input.PersonId!.Value, input.OperationId!.Value,
            string.IsNullOrWhiteSpace(input.Weight) ? null : AccountingInput.Number(input.Weight), input.Revision!.Value, input.Confirmed, ct);
        return RedirectToAction(nameof(Index), new { eventId = input.EventId });
    });
}
