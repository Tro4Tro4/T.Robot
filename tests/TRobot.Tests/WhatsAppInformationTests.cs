using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TRobot.Application.Local;
using TRobot.Application.WhatsApp;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed partial class WhatsAppIntegrationTests
{
    [Fact]
    public async Task RealInformationRequiresApprovalAndStoresOnlyTechnicalReceiptsWithPersistentReplyOperation()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var gateway = new FakeGateway(); var service = Service(p, gateway, new Clock()); await Activate(p, f, service);
        var db = p.GetRequiredService<TRobotDbContext>();
        db.Add(new MenuItem(Guid.NewGuid(), f.EventId, "Titolo pubblico sintetico", MenuCategory.Food, 2, "porzioni", notes: "NOTA PRIVATA")); await db.SaveChangesAsync();
        var blocked = await service.IncomingAsync(Incoming("!menu", "unapproved"));
        Assert.True(blocked.Handled); Assert.Contains("approvare", blocked.ReplyText); Assert.DoesNotContain("Titolo pubblico", blocked.ReplyText);
        Assert.Null(blocked.Reaction); Assert.NotNull(blocked.ReplyOperationId);
        Assert.Equal(new WhatsAppIncomingResult(true, true), await service.IncomingAsync(Incoming("!menu", "unapproved")));
        var preview = await service.PreviewListAsync(f.OrganizerPersonId, f.EventId, WhatsAppListKind.Menu);
        Assert.False(preview.Approved); Assert.Equal("Gruppo sintetico", preview.GroupName); Assert.Single(preview.Pages);
        var operation = Guid.NewGuid();
        await Assert.ThrowsAsync<LocalValidationException>(() => service.ApproveListAsync(f.OrganizerPersonId, f.EventId, operation, preview.Kind, preview.GroupId, preview.ConnectionRevision, preview.Fingerprint, false));
        await ApproveInformation(service, f, preview, operation);
        await ApproveInformation(service, f, preview, operation);
        var reply = await service.IncomingAsync(Incoming("!menu", "approved"));
        Assert.Contains("Titolo pubblico sintetico", reply.ReplyText); Assert.DoesNotContain("NOTA PRIVATA", reply.ReplyText);
        Assert.Contains("2 porzioni", reply.ReplyText); Assert.Null(reply.Reaction); Assert.NotNull(reply.ReplyOperationId);
        Assert.True((await service.PreviewListAsync(f.OrganizerPersonId, f.EventId, preview.Kind)).Approved);
        Assert.Single(await db.Set<WhatsAppListApproval>().ToListAsync()); Assert.Equal(2, await db.Set<WhatsAppCommandReceipt>().CountAsync());
        Assert.Contains(await db.Set<WhatsAppCommandReceipt>().Select(x => x.Id).ToArrayAsync(), x => x == reply.ReplyOperationId);
        Assert.Equal(2, await db.Set<LocalAudit>().CountAsync(x => x.Action == "InformationReceived"));
        Assert.Equal(1, await db.Set<LocalAudit>().CountAsync(x => x.Action == "ListApproved"));
        Assert.Empty(await db.Set<WhatsAppLiveMessage>().ToListAsync()); Assert.Equal(0, gateway.Sends);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("quantity")]
    [InlineData("repin")]
    public async Task RealListChangedContentOrSameGroupRevisionInvalidatesApprovalAndStalePreview(string change)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); var db = p.GetRequiredService<TRobotDbContext>();
        db.Add(new MenuItem(Guid.NewGuid(), f.EventId, "Primo titolo", MenuCategory.Drink, 2, "litri")); await db.SaveChangesAsync();
        var preview = await service.PreviewListAsync(f.OrganizerPersonId, f.EventId, WhatsAppListKind.Menu); await ApproveInformation(service, f, preview);
        if (change == "repin") await service.SelectGroupAsync(f.OrganizerPersonId, f.EventId, Group, 1, true);
        else
        {
            var menu = await db.Set<MenuItem>().SingleAsync(); menu.Update(change == "content" ? "Nuovo titolo" : menu.Name, menu.Category, change == "quantity" ? 3 : 2, menu.Unit, null, menu.Availability, null); await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<LocalConflictException>(() => ApproveInformation(service, f, preview));
        var reply = await service.IncomingAsync(Incoming("!menu", "changed"));
        Assert.Contains("approvare", reply.ReplyText); Assert.DoesNotContain("titolo", reply.ReplyText);
        Assert.Single(await db.Set<WhatsAppListApproval>().ToListAsync());
    }

    [Fact]
    public async Task RealListPreviewIncludesAllFiveItemPagesAndHashesExactlyPublishedContent()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service);
        var db = p.GetRequiredService<TRobotDbContext>();
        for (var i = 0; i < 11; i++) db.Add(new OrganizationTask(f.EventId, $"Attività {i:D2}", "NOTA PRIVATA", TaskPriority.Normal, person, null));
        await db.SaveChangesAsync();
        var preview = await service.PreviewListAsync(f.OrganizerPersonId, f.EventId, WhatsAppListKind.Todo);
        Assert.Equal(3, preview.Pages.Count); Assert.All(preview.Pages, page => { Assert.True(page.Length <= 2000); Assert.DoesNotContain("NOTA PRIVATA", page); });
        Assert.Contains("Attività 04", preview.Pages[0]); Assert.DoesNotContain("Attività 05", preview.Pages[0]); Assert.Contains("Attività 10", preview.Pages[2]);
        await ApproveInformation(service, f, preview);
        Assert.Equal(preview.Pages[1], (await service.IncomingAsync(Incoming("!todo 2", "page-two"))).ReplyText);
        var task = await db.Set<OrganizationTask>().FirstAsync(); task.Update(task.Title, "ALTRE NOTE PRIVATE", TaskPriority.High, task.Status, null, Now.AddDays(1)); await db.SaveChangesAsync();
        var unchanged = await service.PreviewListAsync(f.OrganizerPersonId, f.EventId, preview.Kind);
        Assert.Equal(preview.Fingerprint, unchanged.Fingerprint); Assert.True(unchanged.Approved);
        Assert.Contains("Pagina inesistente", (await service.IncomingAsync(Incoming("!todo 4", "out-of-range"))).ReplyText);
        Assert.Contains("Sintassi", (await service.IncomingAsync(Incoming("!todo -1", "invalid-page"))).ReplyText);
    }

    [Fact]
    public async Task InformationReplaysAndPresenceCrossCollisionsRejectChangedPayloadOrPerson()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        var first = await service.IncomingAsync(Incoming("!aiuto", "info")); Assert.Equal(WhatsAppService.RealHelp, first.ReplyText);
        Assert.Contains("!porto", first.ReplyText); Assert.Contains("!conferma", first.ReplyText); Assert.DoesNotContain("!vota", first.ReplyText);
        Assert.Equal(new WhatsAppIncomingResult(true, true), await service.IncomingAsync(Incoming("!aiuto", "info")));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.IncomingAsync(Incoming("!menu", "info")));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.IncomingAsync(Incoming("!AIUTO", "info")));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.IncomingAsync(Incoming("!presenza si", "info")));
        await service.IncomingAsync(Incoming("!presenza no", "presence"));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.IncomingAsync(Incoming("!aiuto", "presence")));
        var db = p.GetRequiredService<TRobotDbContext>(); var other = new Person("Altra sintetica"); db.AddRange(other, new EventParticipant(f.EventId, other.Id), new WhatsAppIdentityAlias(f.EventId, Options().Fingerprint("sender", "other@lid"), other.Id)); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalConflictException>(() => service.IncomingAsync(Incoming("!aiuto", "info") with { SenderIds = ["other@lid"] }));
        Assert.Single(await db.Set<WhatsAppCommandReceipt>().ToListAsync()); Assert.Single(await db.Set<WhatsAppIncomingReceipt>().ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InformationReceiptAndApprovalAuditRollbackTogether(bool approval)
    {
        var failure = new FailAfterSave(); await using var f = new LocalWebFactory { SaveInterceptor = failure };
        using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider; var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        var preview = await service.PreviewListAsync(f.OrganizerPersonId, f.EventId, WhatsAppListKind.Menu);
        failure.Armed = true;
        if (approval) await Assert.ThrowsAsync<ApplicationException>(() => ApproveInformation(service, f, preview));
        else await Assert.ThrowsAsync<ApplicationException>(() => service.IncomingAsync(Incoming("!aiuto")));
        failure.Armed = false; var db = p.GetRequiredService<TRobotDbContext>();
        Assert.Empty(await db.Set<WhatsAppCommandReceipt>().ToListAsync()); Assert.Empty(await db.Set<WhatsAppListApproval>().ToListAsync());
        Assert.Equal(0, await db.Set<LocalAudit>().CountAsync(x => x.Action == "InformationReceived" || x.Action == "ListApproved"));
    }

    [Fact]
    public async Task InformationRevocationBlocksReadsApprovalAndReplaysBeforeAnyWrite()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        var preview = await service.PreviewListAsync(f.OrganizerPersonId, f.EventId, WhatsAppListKind.Menu);
        await service.IncomingAsync(Incoming("!aiuto")); var db = p.GetRequiredService<TRobotDbContext>();
        (await db.EventOrganizers.SingleAsync()).SetActive(false); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.IncomingAsync(Incoming("!aiuto")));
        await Assert.ThrowsAsync<LocalAccessException>(() => ApproveInformation(service, f, preview));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.PreviewListAsync(f.OrganizerPersonId, f.EventId, preview.Kind));
        Assert.Single(await db.Set<WhatsAppCommandReceipt>().ToListAsync()); Assert.Empty(await db.Set<WhatsAppListApproval>().ToListAsync());
    }

    [Fact]
    public async Task HttpListPreviewEncodesTitlesRequiresCsrfAndRechecksContentBeforeApproval()
    {
        await using var f = new LocalWebFactory(); var gateway = new FakeGateway();
        using var app = f.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        { services.RemoveAll<WhatsAppGatewayOptions>(); services.AddSingleton(Options()); services.RemoveAll<IWhatsAppGateway>(); services.AddSingleton<IWhatsAppGateway>(gateway); }));
        WhatsAppListPreview preview;
        using (var setup = app.Services.CreateScope())
        {
            var service = Service(setup.ServiceProvider, gateway, new Clock()); await Activate(setup.ServiceProvider, f, service);
            var db = setup.ServiceProvider.GetRequiredService<TRobotDbContext>(); db.Add(new MenuItem(Guid.NewGuid(), f.EventId, "<script>titolo</script>", MenuCategory.Food, 1, "porzioni")); await db.SaveChangesAsync();
            preview = await service.PreviewListAsync(f.OrganizerPersonId, f.EventId, WhatsAppListKind.Menu);
        }
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync($"/WhatsApp/ListPreview?eventId={f.EventId}&kind=Menu")).StatusCode);
        var token = Token(await client.GetStringAsync("/Access/Login")); await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)));
        var html = await client.GetStringAsync($"/WhatsApp/ListPreview?eventId={f.EventId}&kind=Menu"); token = Token(html);
        Assert.Contains("&lt;script&gt;titolo&lt;/script&gt;", html); Assert.DoesNotContain("<script>titolo</script>", html);
        Assert.Contains("Gruppo sintetico", html); Assert.Contains(Group, html);
        var fields = new List<(string, string)> { ("EventId", f.EventId.ToString()), ("OperationId", Guid.NewGuid().ToString()), ("Kind", "Menu"), ("GroupId", preview.GroupId), ("ConnectionRevision", preview.ConnectionRevision.ToString()), ("Fingerprint", preview.Fingerprint), ("Confirmed", "true") };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/WhatsApp/ApproveList", Form(fields.ToArray()))).StatusCode);
        fields.Add(("__RequestVerificationToken", token));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/WhatsApp/ApproveList", Form(fields.ToArray()))).StatusCode);
        using (var check = app.Services.CreateScope())
        {
            var db = check.ServiceProvider.GetRequiredService<TRobotDbContext>(); Assert.Single(await db.Set<WhatsAppListApproval>().ToListAsync());
            db.Add(new MenuItem(Guid.NewGuid(), f.EventId, "Nuova voce", MenuCategory.Food, 1, "porzioni")); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/WhatsApp/ApproveList", Form(fields.ToArray()))).StatusCode);
        Assert.Equal(0, gateway.Sends);
    }

    private static Task ApproveInformation(WhatsAppService service, LocalWebFactory f, WhatsAppListPreview preview, Guid? operation = null) =>
        service.ApproveListAsync(f.OrganizerPersonId, f.EventId, operation ?? Guid.NewGuid(), preview.Kind, preview.GroupId, preview.ConnectionRevision, preview.Fingerprint, true);
}
