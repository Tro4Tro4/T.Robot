using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Local;
using TRobot.Application.Simulation;
using TRobot.Application.WhatsApp;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed partial class WhatsAppIntegrationTests
{
    private static string ConfirmationCode(WhatsAppIncomingResult result) =>
        Regex.Match(result.ReplyText ?? "", @"!conferma ([0-9a-f]{32})").Groups[1].Value;

    [Theory]
    [InlineData("!porto")]
    [InlineData("!prendo")]
    [InlineData("!fatto")]
    public async Task OperationsOnlyMutateOnOwnerConfirmationAndNeverReplayClearCodes(string command)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service);
        await service.IncomingAsync(Incoming()); var db = p.GetRequiredService<TRobotDbContext>();
        ILocalRecord target = command == "!porto" ? new MenuItem(Guid.NewGuid(), f.EventId, "TITOLO RISERVATO", MenuCategory.Food, 1, "pezzi")
            : new OrganizationTask(f.EventId, "TITOLO RISERVATO", null, TaskPriority.Normal, command == "!fatto" ? person : null, null);
        db.Add(target); await db.SaveChangesAsync();
        var request = Incoming($"{command} {SimulationReferences.Short(target.Id)}", "request");
        var pending = await service.IncomingAsync(request); var code = ConfirmationCode(pending);
        Assert.Equal(32, code.Length); Assert.DoesNotContain("TITOLO RISERVATO", pending.ReplyText); Assert.Null(pending.Reaction);
        Assert.Contains("!conferma " + code, pending.ReplyText!.Split('\n'));
        Assert.Contains("Nessuna modifica prima della conferma", pending.ReplyText);
        Assert.Contains("Solo tu", pending.ReplyText); Assert.Contains("cinque minuti", pending.ReplyText);
        Assert.Contains("Rif. " + SimulationReferences.Short(target.Id), pending.ReplyText);
        Assert.Equal(0, target.Revision); Assert.Equal(new WhatsAppIncomingResult(true, true), await service.IncomingAsync(request));
        var proposal = await db.Set<WhatsAppOperationalProposal>().SingleAsync();
        Assert.NotEqual(code, proposal.CodeHash); Assert.Equal(64, proposal.CodeHash.Length); Assert.Null(proposal.ConsumedAtUtc);
        Assert.Equal(Now.AddMinutes(5), proposal.ExpiresAtUtc);
        var other = new Person("Altra persona sintetica"); var otherParticipant = new EventParticipant(f.EventId, other.Id);
        otherParticipant.ChangeStatus(ParticipationStatus.Confirmed, Now);
        db.AddRange(other, otherParticipant, new WhatsAppIdentityAlias(f.EventId, Options().Fingerprint("sender", "other@lid"), other.Id)); await db.SaveChangesAsync();
        Assert.Contains("non disponibile", (await service.IncomingAsync(Incoming("!conferma " + code, "stolen") with { SenderIds = ["other@lid"] })).ReplyText);
        Assert.Null((await db.Set<WhatsAppOperationalProposal>().SingleAsync()).ConsumedAtUtc);
        var confirmation = Incoming("!conferma " + code, "confirm");
        Assert.Equal("✅", (await service.IncomingAsync(confirmation)).Reaction);
        Assert.Equal(new WhatsAppIncomingResult(true, true), await service.IncomingAsync(confirmation));
        Assert.Contains("non disponibile", (await service.IncomingAsync(Incoming("!conferma " + code, "reuse"))).ReplyText);
        Assert.Equal(Now, (await db.Set<WhatsAppOperationalProposal>().SingleAsync()).ConsumedAtUtc);
        if (command == "!porto") { var menu = await db.Set<MenuItem>().SingleAsync(); Assert.Equal(person, menu.ResponsiblePersonId); Assert.Equal(MenuAvailability.Assigned, menu.Availability); }
        else { var task = await db.Set<OrganizationTask>().SingleAsync(); Assert.Equal(person, task.AssigneeId); Assert.Equal(command == "!fatto" ? OrganizationTaskStatus.Completed : OrganizationTaskStatus.InProgress, task.Status); }
        Assert.Empty(await db.Set<SimulationActionReceipt>().ToListAsync());
        await Assert.ThrowsAsync<LocalConflictException>(() => service.IncomingAsync(Incoming("!aiuto", "request")));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.IncomingAsync(Incoming("!presenza no", "confirm")));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.IncomingAsync(Incoming("!prendo abcdef12", "synthetic-message")));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("target")]
    [InlineData("occupied")]
    [InlineData("repin")]
    [InlineData("absent")]
    [InlineData("child")]
    [InlineData("revoked")]
    [InlineData("alias")]
    public async Task OperationsRevalidateExpiryBindingEligibilityAuthorizationAndTarget(string change)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock); var person = await Activate(p, f, service); await service.IncomingAsync(Incoming());
        var db = p.GetRequiredService<TRobotDbContext>(); var task = new OrganizationTask(f.EventId, "NON PUBBLICARE", null, TaskPriority.Normal, null, null); db.Add(task); await db.SaveChangesAsync();
        var code = ConfirmationCode(await service.IncomingAsync(Incoming("!prendo " + task.Id, "request")));
        task = await db.Set<OrganizationTask>().SingleAsync();
        if (change == "expired") clock.Utc = Now.AddMinutes(5);
        if (change == "target") task.Update("Titolo cambiato", null, task.Priority, task.Status, null, null);
        if (change == "occupied") task.Update(task.Title, null, task.Priority, task.Status, f.OrganizerPersonId, null);
        if (change == "repin") await service.SelectGroupAsync(f.OrganizerPersonId, f.EventId, Group, 1, true);
        if (change == "absent") (await db.EventParticipants.SingleAsync()).ChangeStatus(ParticipationStatus.Absent, Now);
        if (change == "child") { var household = new Household(f.EventId, "Nucleo sintetico", Guid.NewGuid()); db.AddRange(household, new HouseholdMember(f.EventId, household.Id, person, HouseholdMemberKind.Child, null, Guid.NewGuid())); }
        if (change == "revoked") (await db.EventOrganizers.SingleAsync()).SetActive(false);
        if (change == "alias") foreach (var alias in await db.Set<WhatsAppIdentityAlias>().ToListAsync()) alias.SetActive(false);
        await db.SaveChangesAsync();
        var input = Incoming("!conferma " + code, "confirm") with { TimestampUnixSeconds = new DateTimeOffset(clock.Utc).ToUnixTimeSeconds() };
        if (change == "revoked") await Assert.ThrowsAsync<LocalAccessException>(() => service.IncomingAsync(input));
        else { var result = await service.IncomingAsync(input); Assert.Null(result.Reaction); if (change != "alias") Assert.Contains("non disponibile", result.ReplyText); }
        Assert.Null((await db.Set<WhatsAppOperationalProposal>().SingleAsync()).ConsumedAtUtc);
        Assert.Equal(OrganizationTaskStatus.Todo, (await db.Set<OrganizationTask>().SingleAsync()).Status);
        Assert.Equal(0, await db.Set<LocalAudit>().CountAsync(x => x.Action == "ClaimTask"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OperationProposalOrConfirmationRollsBackWithReceiptAndAudit(bool confirmation)
    {
        var failure = new FailAfterSave(); await using var f = new LocalWebFactory { SaveInterceptor = failure }; using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); await service.IncomingAsync(Incoming());
        var db = p.GetRequiredService<TRobotDbContext>(); var task = new OrganizationTask(f.EventId, "Attività sintetica", null, TaskPriority.Normal, null, null); db.Add(task); await db.SaveChangesAsync();
        var text = "!prendo " + task.Id;
        if (confirmation) text = "!conferma " + ConfirmationCode(await service.IncomingAsync(Incoming(text, "request")));
        failure.Armed = true; await Assert.ThrowsAsync<ApplicationException>(() => service.IncomingAsync(Incoming(text, "fail"))); failure.Armed = false;
        Assert.Equal(OrganizationTaskStatus.Todo, (await db.Set<OrganizationTask>().SingleAsync()).Status);
        Assert.Equal(confirmation ? 1 : 0, await db.Set<WhatsAppCommandReceipt>().CountAsync());
        var proposals = await db.Set<WhatsAppOperationalProposal>().ToListAsync(); Assert.Equal(confirmation ? 1 : 0, proposals.Count); Assert.All(proposals, x => Assert.Null(x.ConsumedAtUtc));
        Assert.Equal(0, await db.Set<LocalAudit>().CountAsync(x => x.Action == "ClaimTask"));
    }

    [Theory]
    [InlineData("menu-ready")]
    [InlineData("menu-cancelled")]
    [InlineData("menu-other")]
    [InlineData("task-completed")]
    [InlineData("task-other")]
    [InlineData("complete-unassigned")]
    public async Task OperationProposalRejectsUnavailableOrOtherAssignedTargets(string kind)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); await service.IncomingAsync(Incoming());
        var db = p.GetRequiredService<TRobotDbContext>(); ILocalRecord target;
        if (kind.StartsWith("menu-")) target = new MenuItem(Guid.NewGuid(), f.EventId, "NON PUBBLICARE", MenuCategory.Food, 1, "pezzi",
            kind == "menu-other" ? f.OrganizerPersonId : null, kind == "menu-ready" ? MenuAvailability.Ready : kind == "menu-cancelled" ? MenuAvailability.Cancelled : MenuAvailability.Assigned);
        else
        {
            var task = new OrganizationTask(f.EventId, "NON PUBBLICARE", null, TaskPriority.Normal, kind == "task-other" ? f.OrganizerPersonId : null, null);
            if (kind == "task-completed") task.Update(task.Title, null, task.Priority, OrganizationTaskStatus.Completed, null, null);
            target = task;
        }
        db.Add(target); await db.SaveChangesAsync();
        var command = kind.StartsWith("menu-") ? "!porto" : kind == "complete-unassigned" ? "!fatto" : "!prendo";
        var result = await service.IncomingAsync(Incoming(command + " " + target.Id, "blocked"));
        Assert.Contains("non disponibile", result.ReplyText); Assert.DoesNotContain("NON PUBBLICARE", result.ReplyText); Assert.Empty(await db.Set<WhatsAppOperationalProposal>().ToListAsync());
    }

    [Fact]
    public async Task OperationProposalLimitReferencesAndListApprovalRemainScoped()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        var db = p.GetRequiredService<TRobotDbContext>(); var a = new OrganizationTask(f.EventId, "Titolo approvabile", null, TaskPriority.Normal, null, null, Guid.Parse("abcdef12-0000-0000-0000-000000000001"));
        var b = new OrganizationTask(f.EventId, "Seconda attività", null, TaskPriority.Normal, null, null, Guid.Parse("abcdef12-0000-0000-0000-000000000002")); db.AddRange(a, b); await db.SaveChangesAsync();
        Assert.Contains("non disponibile", (await service.IncomingAsync(Incoming("!prendo " + a.Id, "unconfirmed"))).ReplyText);
        await service.IncomingAsync(Incoming());
        Assert.Contains("non disponibile", (await service.IncomingAsync(Incoming("!prendo abcdef12", "ambiguous"))).ReplyText);
        Assert.Contains("non disponibile", (await service.IncomingAsync(Incoming("!porto " + a.Id, "wrong-kind"))).ReplyText);
        for (var i = 0; i < 3; i++) Assert.Equal(32, ConfirmationCode(await service.IncomingAsync(Incoming("!prendo " + a.Id, "request-" + i))).Length);
        Assert.Contains("tre richieste", (await service.IncomingAsync(Incoming("!prendo " + a.Id, "limit"))).ReplyText);
        Assert.Equal(3, await db.Set<WhatsAppOperationalProposal>().CountAsync());
        var preview = await service.PreviewListAsync(f.OrganizerPersonId, f.EventId, WhatsAppListKind.Todo); Assert.Contains("abcdef12", preview.Pages[0]); await ApproveInformation(service, f, preview);
        var code = ConfirmationCode(await service.IncomingAsync(Incoming("!prendo " + a.Id, "request-0"))); Assert.Empty(code);
        a = await db.Set<OrganizationTask>().SingleAsync(x => x.Id == a.Id);
        a.Update(a.Title, null, a.Priority, OrganizationTaskStatus.InProgress, null, null); await db.SaveChangesAsync();
        Assert.False((await service.PreviewListAsync(f.OrganizerPersonId, f.EventId, WhatsAppListKind.Todo)).Approved);
    }
}
