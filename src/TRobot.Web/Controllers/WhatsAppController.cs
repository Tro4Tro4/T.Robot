using Microsoft.AspNetCore.Mvc;
using TRobot.Application.WhatsApp;
using TRobot.Domain;
using TRobot.Web.Models;

namespace TRobot.Web.Controllers;

public sealed class WhatsAppController(WhatsAppService service) : LocalControllerBase
{
    [HttpGet] public Task<IActionResult> ListPreview(Guid eventId, TRobot.Domain.WhatsAppListKind kind, CancellationToken ct) => ExecuteAsync(async () =>
        View(await service.PreviewListAsync(ActorId, eventId, kind, ct)));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> ApproveList(WhatsAppListApprovalInput input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest();
        await service.ApproveListAsync(ActorId, input.EventId, input.OperationId, input.Kind, input.GroupId, input.ConnectionRevision, input.Fingerprint, input.Confirmed, ct);
        return RedirectToAction(nameof(ListPreview), new { input.EventId, input.Kind });
    });
    [HttpGet] public Task<IActionResult> Index(Guid eventId, CancellationToken ct) => ExecuteAsync(async () => View(await service.ReadAsync(ActorId, eventId, ct)));
    [HttpGet] public Task<IActionResult> Status(Guid eventId, CancellationToken ct) => ExecuteAsync(async () => Json(await service.HealthAsync(ActorId, eventId, ct)));
    [HttpGet] public Task<IActionResult> Groups(Guid eventId, CancellationToken ct) => ExecuteAsync(async () => Json(await service.GroupsAsync(ActorId, eventId, ct)));
    [HttpGet] public Task<IActionResult> Members(Guid eventId, CancellationToken ct) => ExecuteAsync(async () => Json(await service.MembersAsync(ActorId, eventId, ct)));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Connect(WhatsAppActionInput input, CancellationToken ct) => ExecuteAsync(async () =>
    { if (!ModelState.IsValid) return BadRequest(); await service.ConnectAsync(ActorId, input.EventId, input.Confirmed, ct); return RedirectToAction(nameof(Index), new { input.EventId }); });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Disconnect(WhatsAppActionInput input, CancellationToken ct) => ExecuteAsync(async () =>
    { if (!ModelState.IsValid) return BadRequest(); await service.DisconnectAsync(ActorId, input.EventId, input.Confirmed, ct); return RedirectToAction(nameof(Index), new { input.EventId }); });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> SelectGroup(WhatsAppGroupInput input, CancellationToken ct) => ExecuteAsync(async () =>
    { if (!ModelState.IsValid) return BadRequest(); await service.SelectGroupAsync(ActorId, input.EventId, input.GroupId, input.Revision, input.Confirmed, ct); return RedirectToAction(nameof(Index), new { input.EventId }); });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> DeactivateGroup(WhatsAppGroupDeactivateInput input, CancellationToken ct) => ExecuteAsync(async () =>
    { if (!ModelState.IsValid) return BadRequest(); await service.DeactivateGroupAsync(ActorId, input.EventId, input.Revision, input.Confirmed, ct); return RedirectToAction(nameof(Index), new { input.EventId }); });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> UpdateFeatures(WhatsAppFeatureInput input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest();
        var features = WhatsAppFeature.None;
        if (input.Attendance) features |= WhatsAppFeature.Attendance;
        if (input.Information) features |= WhatsAppFeature.Information;
        if (input.Operations) features |= WhatsAppFeature.Operations;
        if (input.MealProposals) features |= WhatsAppFeature.MealProposals;
        if (input.MealPreferences) features |= WhatsAppFeature.MealPreferences;
        if (input.FunReplies) features |= WhatsAppFeature.FunReplies;
        await service.UpdateFeaturesAsync(ActorId, input.EventId, new WhatsAppFeatureSet(features), input.FeaturesRevision, input.Confirmed, ct);
        return RedirectToAction(nameof(Index), new { input.EventId });
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Map(WhatsAppMapInput input, CancellationToken ct) => ExecuteAsync(async () =>
    { if (!ModelState.IsValid) return BadRequest(); await service.MapAsync(ActorId, input.EventId, input.MemberId, input.PersonId, input.Revision, input.Confirmed, ct); return RedirectToAction(nameof(Index), new { input.EventId }); });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> AssociateFromRubrica(WhatsAppRubricaInput input, CancellationToken ct) => ExecuteAsync(async () =>
    {
        if (!ModelState.IsValid) return BadRequest();
        var result = await service.AssociateFromRubricaAsync(ActorId, input.EventId, input.Revision, input.Confirmed, ct);
        TempData["WhatsAppResult"] = result.Associated > 0
            ? $"Rubrica: {result.Associated} membri associati, {result.AlreadyAssociated} già associati, {result.NotRecognized} non riconosciuti, "
                + $"{result.Ambiguous} ambigui, {result.NotParticipant} senza partecipante, {result.Conflicts} in conflitto."
            : $"Rubrica: nessuna nuova associazione. {result.AlreadyAssociated} già associati, {result.NotRecognized} non riconosciuti, "
                + $"{result.Ambiguous} ambigui, {result.NotParticipant} senza partecipante, {result.Conflicts} in conflitto.";
        return RedirectToAction(nameof(Index), new { input.EventId });
    });
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Draft(WhatsAppDraftInput input, CancellationToken ct) => ExecuteAsync(async () =>
    { if (!ModelState.IsValid) return BadRequest(); var id = await service.DraftAsync(ActorId, input.EventId, input.OperationId, input.Body, ct); return RedirectToAction(nameof(Preview), new { input.EventId, id }); });
    [HttpGet] public Task<IActionResult> Preview(Guid eventId, Guid id, CancellationToken ct) => ExecuteAsync(async () =>
        View(await service.PreviewAsync(ActorId, eventId, id, ct)));
    [HttpPost, ValidateAntiForgeryToken] public Task<IActionResult> Send(WhatsAppSendInput input, CancellationToken ct) => ExecuteAsync(async () =>
    { if (!ModelState.IsValid) return BadRequest(); await service.SendAsync(ActorId, input.EventId, input.Id, input.Revision, input.Confirmed, ct); return RedirectToAction(nameof(Preview), new { input.EventId, id = input.Id }); });
}
