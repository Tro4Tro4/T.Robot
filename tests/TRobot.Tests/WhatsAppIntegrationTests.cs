using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TRobot.Application.Local;
using TRobot.Application.WhatsApp;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using TRobot.Web.Controllers;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed partial class WhatsAppIntegrationTests
{
    private const string Group = "synthetic-group@g.us";
    private const string Sender = "synthetic-user@lid";
    private const string Alternate = "synthetic-user@s.whatsapp.net";
    private static readonly DateTime Now = new(2030, 12, 30, 11, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider { public DateTime Utc { get; set; } = Now; public override DateTimeOffset GetUtcNow() => new(Utc); }
    private static WhatsAppGatewayOptions Options(bool enabled = true) => new(enabled, "http://127.0.0.1:5240", new string('a', 64));
    private static WhatsAppService Service(IServiceProvider p, FakeGateway gateway, Clock clock, bool enabled = true)
    { var store = p.GetRequiredService<ILocalStore>(); return new(store, p.GetRequiredService<IWhatsAppStore>(), new(store, clock), gateway, Options(enabled), clock); }
    private static WhatsAppIncoming Incoming(string text = "!presenza si", string messageId = "synthetic-message") =>
        new(Group, [Sender, Alternate], messageId, text, new DateTimeOffset(Now).ToUnixTimeSeconds());
    private static async Task<Guid> Activate(IServiceProvider p, LocalWebFactory f, WhatsAppService service)
    {
        await service.SelectGroupAsync(f.OrganizerPersonId, f.EventId, Group, 0, true);
        var person = (await p.GetRequiredService<TRobotDbContext>().EventParticipants.SingleAsync()).PersonId;
        await service.MapAsync(f.OrganizerPersonId, f.EventId, Sender, person, 1, true); return person;
    }

    [Fact]
    public async Task IncomingPresenceUsesMappedIdentityAndAtomicDedicatedReceiptWithoutChangingSimulatorIdentity()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var gateway = new FakeGateway(); var clock = new Clock(); var service = Service(p, gateway, clock);
        var person = await Activate(p, f, service); var message = Incoming();
        Assert.Equal(new WhatsAppIncomingResult(true, false, "✅"), await service.IncomingAsync(message));
        Assert.Equal(new WhatsAppIncomingResult(true, true), await service.IncomingAsync(message));
        clock.Utc = Now.AddSeconds(1);
        await service.IncomingAsync(Incoming("!presenza no", "second-message"));
        Assert.True((await service.IncomingAsync(message)).Duplicate);
        var db = p.GetRequiredService<TRobotDbContext>();
        Assert.Equal(ParticipationStatus.Absent, (await db.EventParticipants.SingleAsync()).Status);
        Assert.Equal("synthetic-http-sender", (await db.People.SingleAsync(x => x.Id == person)).WhatsAppSenderId);
        Assert.Equal(2, await db.Set<WhatsAppIncomingReceipt>().CountAsync());
        Assert.Empty(await db.ParticipationReceipts.ToListAsync()); Assert.Empty(await db.Set<OutboundMessage>().ToListAsync());
        var receipt = await db.Set<WhatsAppIncomingReceipt>().SingleAsync(x => x.Status == ParticipationStatus.Confirmed);
        Assert.Equal(ParticipationStatus.NoResponse, receipt.PreviousStatus); Assert.Equal(f.OrganizerPersonId, receipt.ActorId);
        Assert.Equal(64, receipt.MessageHash.Length); Assert.DoesNotContain("synthetic", receipt.MessageHash);
        await Assert.ThrowsAsync<LocalConflictException>(() => service.IncomingAsync(message with { Text = "!presenza forse" }));
        Assert.Equal(2, await db.Set<LocalAudit>().CountAsync(x => x.Action == "PresenceReceived"));
    }

    [Theory]
    [InlineData("dm")]
    [InlineData("group")]
    [InlineData("unknown")]
    [InlineData("before-activation")]
    [InlineData("old")]
    [InlineData("future")]
    [InlineData("unsupported")]
    [InlineData("missing-alias")]
    public async Task CallbackFiltersWrongChannelStaleOrUnmappedMessagesWithoutWrites(string kind)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock); await Activate(p, f, service);
        var input = Incoming();
        input = kind switch
        {
            "dm" => input with { GroupId = "synthetic@s.whatsapp.net" }, "group" => input with { GroupId = "other@g.us" },
            "unknown" => input with { SenderIds = ["unknown@lid"] }, "missing-alias" => input with { SenderIds = [] },
            "before-activation" => input with { TimestampUnixSeconds = input.TimestampUnixSeconds - 1 },
            "future" => input with { TimestampUnixSeconds = input.TimestampUnixSeconds + 31 },
            "unsupported" => input with { Text = "!vota abcdef12 1" }, _ => input
        };
        if (kind == "old") clock.Utc = Now.AddSeconds(121);
        Assert.False((await service.IncomingAsync(input)).Handled);
        var db = p.GetRequiredService<TRobotDbContext>(); Assert.Empty(await db.Set<WhatsAppIncomingReceipt>().ToListAsync());
        Assert.Equal(ParticipationStatus.NoResponse, (await db.EventParticipants.SingleAsync()).Status);
        Assert.Equal(0, await db.Set<LocalAudit>().CountAsync(x => x.Action == "PresenceReceived"));
    }

    [Theory]
    [InlineData("account")]
    [InlineData("assignment")]
    [InlineData("administrator")]
    public async Task RevocationDisablesNewCallbacksAndReplaysAndDeviceAccess(string kind)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); await service.IncomingAsync(Incoming());
        var db = p.GetRequiredService<TRobotDbContext>(); var user = await db.Users.SingleAsync();
        if (kind == "account") user.IsEnabled = false;
        else if (kind == "administrator") user.IsSystemAdministrator = false;
        else (await db.EventOrganizers.SingleAsync()).SetActive(false);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.IncomingAsync(Incoming()));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.HealthAsync(f.OrganizerPersonId, f.EventId));
        Assert.Single(await db.Set<WhatsAppIncomingReceipt>().ToListAsync());
    }

    [Fact]
    public async Task AssociationsRejectAmbiguousAliasesAndDifferentExistingPersonAndNeverImportPeople()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        var db = p.GetRequiredService<TRobotDbContext>(); var other = new Person("Altra persona sintetica");
        db.AddRange(other, new EventParticipant(f.EventId, other.Id)); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalConflictException>(() => service.MapAsync(f.OrganizerPersonId, f.EventId, Sender, other.Id, 1, true));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.MapAsync(f.OrganizerPersonId, f.EventId, Sender, Guid.NewGuid(), 1, true));
        db.Add(new WhatsAppIdentityAlias(f.EventId, Options().Fingerprint("sender", "different@lid"), other.Id)); await db.SaveChangesAsync();
        Assert.False((await service.IncomingAsync(Incoming() with { SenderIds = [Sender, "different@lid"] })).Handled);
        Assert.Equal(3, await db.People.CountAsync()); Assert.Empty(await db.Set<WhatsAppIncomingReceipt>().ToListAsync());
    }

    [Fact]
    public async Task CallbackFailureAfterSaveRollsBackPresenceReceiptAndAudit()
    {
        var failure = new FailAfterSave(); await using var f = new LocalWebFactory { SaveInterceptor = failure };
        using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider; var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        failure.Armed = true; await Assert.ThrowsAsync<ApplicationException>(() => service.IncomingAsync(Incoming())); failure.Armed = false;
        var db = p.GetRequiredService<TRobotDbContext>(); Assert.Empty(await db.Set<WhatsAppIncomingReceipt>().ToListAsync());
        Assert.Equal(ParticipationStatus.NoResponse, (await db.EventParticipants.SingleAsync()).Status);
        Assert.Equal(0, await db.Set<LocalAudit>().CountAsync(x => x.Action == "PresenceReceived"));
        Assert.True((await service.IncomingAsync(Incoming())).Handled);
    }

    [Fact]
    public async Task RealManualMessagesRequireApprovalAndDoNotRetryAfterTimeoutOrSuccess()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var gateway = new FakeGateway { FailSend = true }; var service = Service(p, gateway, new Clock()); await Activate(p, f, service);
        var id = await service.DraftAsync(f.OrganizerPersonId, f.EventId, Guid.NewGuid(), "Messaggio operativo sintetico");
        await Assert.ThrowsAsync<LocalValidationException>(() => service.SendAsync(f.OrganizerPersonId, f.EventId, id, 0, false));
        Assert.Equal(WhatsAppLiveMessageStatus.Uncertain, await service.SendAsync(f.OrganizerPersonId, f.EventId, id, 0, true));
        Assert.Equal(WhatsAppLiveMessageStatus.Uncertain, await service.SendAsync(f.OrganizerPersonId, f.EventId, id, 0, true)); Assert.Equal(1, gateway.Sends);
        gateway.FailSend = false;
        var next = await service.DraftAsync(f.OrganizerPersonId, f.EventId, Guid.NewGuid(), "Altro messaggio operativo");
        Assert.Equal(WhatsAppLiveMessageStatus.Sent, await service.SendAsync(f.OrganizerPersonId, f.EventId, next, 0, true));
        Assert.Equal(WhatsAppLiveMessageStatus.Sent, await service.SendAsync(f.OrganizerPersonId, f.EventId, next, 0, true)); Assert.Equal(2, gateway.Sends);
        var db = p.GetRequiredService<TRobotDbContext>(); Assert.Empty(await db.Set<OutboundMessage>().ToListAsync());
        Assert.Equal(2, await db.Set<WhatsAppLiveMessage>().CountAsync());
    }

    [Fact]
    public async Task ChangingGroupInvalidatesDraftAndFailedGatewayGroupChangeLeavesBindingDisabled()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var gateway = new FakeGateway(); var service = Service(p, gateway, new Clock()); await Activate(p, f, service);
        var id = await service.DraftAsync(f.OrganizerPersonId, f.EventId, Guid.NewGuid(), "Bozza per vecchio gruppo");
        await service.SelectGroupAsync(f.OrganizerPersonId, f.EventId, "second-group@g.us", 1, true);
        await Assert.ThrowsAsync<LocalConflictException>(() => service.SendAsync(f.OrganizerPersonId, f.EventId, id, 0, true)); Assert.Equal(0, gateway.Sends);
        gateway.FailGroup = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SelectGroupAsync(f.OrganizerPersonId, f.EventId, Group, 3, true));
        var db = p.GetRequiredService<TRobotDbContext>(); Assert.False((await db.Set<WhatsAppConnection>().SingleAsync()).Enabled);
        Assert.False((await service.IncomingAsync(Incoming())).Handled);
        Assert.Equal(WhatsAppLiveMessageStatus.Draft, (await db.Set<WhatsAppLiveMessage>().SingleAsync()).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GroupChangeAndDisconnectWaitUntilClaimedLiveSendFinishes(bool disconnect)
    {
        await using var f = new LocalWebFactory(); var gateway = new FakeGateway(); var clock = new Clock(); Guid id;
        using (var setup = f.Services.CreateScope())
        {
            var service = Service(setup.ServiceProvider, gateway, clock); await Activate(setup.ServiceProvider, f, service);
            id = await service.DraftAsync(f.OrganizerPersonId, f.EventId, Guid.NewGuid(), "Messaggio approvato");
        }
        gateway.SendStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        gateway.ReleaseSend = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sendScope = f.Services.CreateScope(); using var groupScope = f.Services.CreateScope();
        var sending = Service(sendScope.ServiceProvider, gateway, clock).SendAsync(f.OrganizerPersonId, f.EventId, id, 0, true);
        await gateway.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var changing = disconnect
            ? Service(groupScope.ServiceProvider, gateway, clock).DisconnectAsync(f.OrganizerPersonId, f.EventId, true)
            : Service(groupScope.ServiceProvider, gateway, clock).SelectGroupAsync(f.OrganizerPersonId, f.EventId, Group, 1, true);
        try { Assert.False(changing.IsCompleted); Assert.Equal(1, gateway.GroupSets); Assert.Equal(0, gateway.Disconnects); }
        finally { gateway.ReleaseSend.TrySetResult(); await Task.WhenAll(sending, changing); }
        Assert.Equal(WhatsAppLiveMessageStatus.Sent, await sending); await changing;
        Assert.Equal(disconnect ? 1 : 2, gateway.GroupSets); Assert.Equal(disconnect ? 1 : 0, gateway.Disconnects);
    }

    [Fact]
    public async Task CallbackEndpointRequiresLoopbackAndConstantTimeBearerEvenWithAuthenticatedCookie()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        var controller = new WhatsAppIncomingController(service, Options()) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
        controller.Request.Headers.Authorization = "Bearer " + Options().Token;
        Assert.IsType<UnauthorizedResult>(await controller.Incoming(Incoming(), default));
        controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Loopback; controller.Request.Headers.Authorization = "Bearer " + new string('b', 64);
        Assert.IsType<UnauthorizedResult>(await controller.Incoming(Incoming(), default));
        controller.Request.Headers.Authorization = "Bearer " + Options().Token;
        Assert.IsType<OkObjectResult>(await controller.Incoming(Incoming(), default));
        var disabled = new WhatsAppIncomingController(service, Options(false)) { ControllerContext = controller.ControllerContext };
        Assert.IsType<NotFoundResult>(await disabled.Incoming(Incoming(), default));
    }

    [Fact]
    public async Task HttpPairingAndRealSendRequireAuthenticationCsrfAndCurrentRevision()
    {
        await using var f = new LocalWebFactory(); var gateway = new FakeGateway();
        using var app = f.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        { services.RemoveAll<WhatsAppGatewayOptions>(); services.AddSingleton(Options()); services.RemoveAll<IWhatsAppGateway>(); services.AddSingleton<IWhatsAppGateway>(gateway); }));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/WhatsApp?eventId=" + f.EventId)).StatusCode);
        var token = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)));
        var page = await client.GetStringAsync("/WhatsApp?eventId=" + f.EventId); token = Token(page);
        Assert.Contains("Collega dispositivo", page); Assert.Contains("whatsapp.js", page);
        var fields = new[] { ("EventId", f.EventId.ToString()), ("Confirmed", "true") };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/WhatsApp/Connect", Form(fields))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/WhatsApp/Connect", Form(fields.Append(("__RequestVerificationToken", token)).ToArray()))).StatusCode);
        Assert.Equal(1, gateway.Connects);
        var group = fields.Append(("GroupId", Group)).Append(("__RequestVerificationToken", token)).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/WhatsApp/SelectGroup", Form(group))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/WhatsApp/SelectGroup", Form(group.Append(("Revision", "0")).ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/WhatsApp/Members?eventId=" + f.EventId)).StatusCode);
        using var verify = app.Services.CreateScope(); (await verify.ServiceProvider.GetRequiredService<TRobotDbContext>().Users.SingleAsync()).IsSystemAdministrator = false;
        await verify.ServiceProvider.GetRequiredService<TRobotDbContext>().SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/WhatsApp/Status?eventId=" + f.EventId)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/WhatsApp/Groups?eventId=" + f.EventId)).StatusCode);
    }

    [Fact]
    public async Task DisabledConfigurationDoesNotContactGatewayAndRejectsRealOperations()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var gateway = new FakeGateway(); var service = Service(p, gateway, new Clock(), false);
        Assert.False((await service.ReadAsync(f.OrganizerPersonId, f.EventId)).GatewayEnabled);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.ConnectAsync(f.OrganizerPersonId, f.EventId, true));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.IncomingAsync(Incoming()));
        Assert.Equal(0, gateway.Connects); Assert.Equal(0, gateway.Sends);
        Assert.Throws<LocalValidationException>(() => new WhatsAppGatewayOptions(true, "https://example.com:5240", new string('a', 64)).RequireEnabled());
    }

    [Fact]
    public async Task PreviewResolvesExactApprovedGroupAndEncodesNameForAssignedNonAdministrator()
    {
        await using var f = new LocalWebFactory(); var gateway = new FakeGateway
        {
            AvailableGroups = [new("second-group@g.us", "ALTRO GRUPPO NON AUTORIZZATO"), new(Group, "<script>Gruppo di prova</script>")]
        };
        using var app = f.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        { services.RemoveAll<WhatsAppGatewayOptions>(); services.AddSingleton(Options()); services.RemoveAll<IWhatsAppGateway>(); services.AddSingleton<IWhatsAppGateway>(gateway); }));
        Guid id;
        using (var setup = app.Services.CreateScope())
        {
            var service = Service(setup.ServiceProvider, gateway, new Clock());
            var db = setup.ServiceProvider.GetRequiredService<TRobotDbContext>();
            var other = new Person("Altro amministratore sintetico");
            db.AddRange(other, new EventOrganizer(f.EventId, other.Id), new TRobot.Infrastructure.Identity.OrganizerUser
            { Id = Guid.NewGuid(), PersonId = other.Id, UserName = "preview-activator", NormalizedUserName = "PREVIEW-ACTIVATOR", IsSystemAdministrator = true });
            await db.SaveChangesAsync();
            await service.SelectGroupAsync(other.Id, f.EventId, Group, 0, true);
            var person = (await db.EventParticipants.SingleAsync()).PersonId;
            await service.MapAsync(other.Id, f.EventId, Sender, person, 1, true);
            (await db.Users.SingleAsync(x => x.PersonId == f.OrganizerPersonId)).IsSystemAdministrator = false;
            await db.SaveChangesAsync();
            id = await service.DraftAsync(f.OrganizerPersonId, f.EventId, Guid.NewGuid(), "Testo da verificare");
        }
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)));
        var html = await client.GetStringAsync($"/WhatsApp/Preview?eventId={f.EventId}&id={id}");
        Assert.Contains("&lt;script&gt;Gruppo di prova&lt;/script&gt;", html); Assert.DoesNotContain("<script>Gruppo di prova</script>", html);
        Assert.DoesNotContain("ALTRO GRUPPO NON AUTORIZZATO", html); Assert.Contains(Group, html);
        Assert.Contains("action=\"/WhatsApp/Send\"", html); Assert.Contains("Destinatario verificato", html);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/WhatsApp/Groups?eventId=" + f.EventId)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/WhatsApp/Status?eventId=" + f.EventId)).StatusCode);
        using var check = app.Services.CreateScope(); Assert.Equal(0, await check.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<LocalAudit>().CountAsync(x => x.Action == "LiveSending"));
        Assert.Equal(0, gateway.Sends);
    }

    [Theory]
    [InlineData("offline")]
    [InlineData("missing-name")]
    [InlineData("duplicate-id")]
    [InlineData("node-pin")]
    [InlineData("different-group")]
    [InlineData("same-group-revision")]
    public async Task PreviewKeepsStoredRecipientAndDisablesSendWhenTargetCannotBeVerified(string kind)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var gateway = new FakeGateway(); var service = Service(p, gateway, new Clock()); await Activate(p, f, service);
        var id = await service.DraftAsync(f.OrganizerPersonId, f.EventId, Guid.NewGuid(), "Bozza conservata");
        if (kind == "offline") gateway.FailHealth = true;
        if (kind == "missing-name") gateway.AvailableGroups = [new("second-group@g.us", "Nome altro gruppo")];
        if (kind == "duplicate-id") gateway.AvailableGroups = [new(Group, "Nome A"), new(Group, "Nome B")];
        if (kind == "node-pin") gateway.HealthGroupOverride = "second-group@g.us";
        if (kind == "different-group") await service.SelectGroupAsync(f.OrganizerPersonId, f.EventId, "second-group@g.us", 1, true);
        if (kind == "same-group-revision") await service.SelectGroupAsync(f.OrganizerPersonId, f.EventId, Group, 1, true);
        var preview = await service.PreviewAsync(f.OrganizerPersonId, f.EventId, id);
        Assert.False(preview.CanSend); Assert.False(preview.RecipientVerified); Assert.Equal(Group, preview.Message.GroupId);
        Assert.Equal("Bozza conservata", preview.Message.Body); Assert.NotEqual("Nome altro gruppo", preview.GroupName);
        if (kind is "offline" or "missing-name" or "duplicate-id") Assert.Null(preview.GroupName);
        if (kind is "different-group" or "same-group-revision") Assert.Equal("Gruppo sintetico", preview.GroupName);
        if (kind == "offline") Assert.Contains("Gateway non raggiungibile", preview.RecipientStatus);
        Assert.Equal(0, gateway.Sends);
        var db = p.GetRequiredService<TRobotDbContext>(); Assert.Equal(WhatsAppLiveMessageStatus.Draft, (await db.Set<WhatsAppLiveMessage>().SingleAsync()).Status);
    }

    [Fact]
    public async Task HistoricalPreviewLoadsOlderMessagesAndChecksAccessBeforeAnyGatewayMetadata()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var gateway = new FakeGateway(); var clock = new Clock(); var service = Service(p, gateway, clock); await Activate(p, f, service);
        var oldest = await service.DraftAsync(f.OrganizerPersonId, f.EventId, Guid.NewGuid(), "Messaggio storico");
        await service.SendAsync(f.OrganizerPersonId, f.EventId, oldest, 0, true);
        for (var i = 0; i < 31; i++)
        { clock.Utc = Now.AddMinutes(i + 1); await service.DraftAsync(f.OrganizerPersonId, f.EventId, Guid.NewGuid(), "Altra bozza " + i); }
        Assert.DoesNotContain((await service.ReadAsync(f.OrganizerPersonId, f.EventId)).Messages, x => x.Id == oldest);
        var preview = await service.PreviewAsync(f.OrganizerPersonId, f.EventId, oldest);
        Assert.Equal("Messaggio storico", preview.Message.Body); Assert.Equal(WhatsAppLiveMessageStatus.Sent, preview.Message.Status); Assert.False(preview.CanSend);
        var reads = gateway.HealthReads;
        await Assert.ThrowsAsync<LocalAccessException>(() => service.PreviewAsync(Guid.NewGuid(), f.EventId, oldest));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.PreviewAsync(f.OrganizerPersonId, Guid.NewGuid(), oldest));
        Assert.Equal(reads, gateway.HealthReads);
        gateway.FailHealth = true;
        Assert.Equal("Messaggio storico", (await service.PreviewAsync(f.OrganizerPersonId, f.EventId, oldest)).Message.Body);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HttpPreviewKeepsBodyAndBlocksSendFormWhenGatewayOfflineOrSameGroupStampIsStale(bool stale)
    {
        await using var f = new LocalWebFactory(); var gateway = new FakeGateway();
        using var app = f.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        { services.RemoveAll<WhatsAppGatewayOptions>(); services.AddSingleton(Options()); services.RemoveAll<IWhatsAppGateway>(); services.AddSingleton<IWhatsAppGateway>(gateway); }));
        Guid id;
        using (var setup = app.Services.CreateScope())
        {
            var service = Service(setup.ServiceProvider, gateway, new Clock()); await Activate(setup.ServiceProvider, f, service);
            id = await service.DraftAsync(f.OrganizerPersonId, f.EventId, Guid.NewGuid(), "Testo della bozza conservato");
            if (stale) await service.SelectGroupAsync(f.OrganizerPersonId, f.EventId, Group, 1, true);
            else gateway.FailHealth = true;
        }
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)));
        var response = await client.GetAsync($"/WhatsApp/Preview?eventId={f.EventId}&id={id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Testo della bozza conservato", html); Assert.Contains(Group, html);
        Assert.DoesNotContain("action=\"/WhatsApp/Send\"", html);
        Assert.Contains(stale ? "Gruppo sintetico" : "Nome non disponibile", html);
        Assert.Contains(stale ? "prepara una nuova bozza" : "Gateway non raggiungibile", html);
        Assert.Equal(0, gateway.Sends);
    }

    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) => new(fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { if (Armed) throw new ApplicationException("Errore sintetico dopo salvataggio presenza."); return ValueTask.FromResult(result); }
    }
    private sealed class FakeGateway : IWhatsAppGateway
    {
        public bool FailSend { get; set; }
        public bool FailGroup { get; set; }
        public bool FailHealth { get; set; }
        public string? HealthGroupOverride { get; set; }
        public int HealthReads { get; private set; }
        public IReadOnlyList<GatewayGroup> AvailableGroups { get; set; } = [new(Group, "Gruppo sintetico"), new("second-group@g.us", "Secondo gruppo")];
        public int Sends { get; private set; }
        public int Connects { get; private set; }
        public int GroupSets { get; private set; }
        public int Disconnects { get; private set; }
        public TaskCompletionSource? SendStarted { get; set; }
        public TaskCompletionSource? ReleaseSend { get; set; }
        public string? ActiveGroup { get; private set; }
        public Task<GatewayHealth> HealthAsync(CancellationToken ct)
        { HealthReads++; if (FailHealth) throw new HttpRequestException("Gateway non disponibile"); return Task.FromResult(new GatewayHealth("connected", null, HealthGroupOverride ?? ActiveGroup)); }
        public Task<GatewayHealth> ConnectAsync(CancellationToken ct) { Connects++; return HealthAsync(ct); }
        public Task<GatewayHealth> DisconnectAsync(CancellationToken ct) { Disconnects++; ActiveGroup = null; return Task.FromResult(new GatewayHealth("disconnected")); }
        public Task<IReadOnlyList<GatewayGroup>> GroupsAsync(CancellationToken ct) => Task.FromResult(AvailableGroups);
        public Task<IReadOnlyList<GatewayMember>> MembersAsync(string groupId, CancellationToken ct) => Task.FromResult<IReadOnlyList<GatewayMember>>([new(Sender, [Alternate], "Membro sintetico")]);
        public Task SetGroupAsync(string? groupId, CancellationToken ct) { GroupSets++; if (FailGroup) throw new InvalidOperationException("Errore tecnico sintetico"); ActiveGroup = groupId; return Task.CompletedTask; }
        public async Task<GatewaySendResult> SendAsync(Guid operationId, string groupId, string text, CancellationToken ct)
        { Sends++; SendStarted?.TrySetResult(); if (ReleaseSend is not null) await ReleaseSend.Task.WaitAsync(ct); if (FailSend) throw new TaskCanceledException(); return new GatewaySendResult("sent", "synthetic-outgoing"); }
    }
}
