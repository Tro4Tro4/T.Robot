using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using TRobot.Application.Local;
using TRobot.Application.Messaging;
using TRobot.Application.WhatsApp;
using TRobot.Domain;
using TRobot.Infrastructure.Identity;
using TRobot.Infrastructure.Persistence;
using TRobot.Web.Services;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class MessageDeliveryTests
{
    private static readonly DateTime Noon = new(2026, 12, 31, 11, 0, 0, DateTimeKind.Utc);
    private sealed class Clock(DateTime utc) : TimeProvider
    { public DateTime Utc { get; set; } = utc; public override DateTimeOffset GetUtcNow() => new(Utc); }

    private static async Task<MessageService> QueueAsync(IServiceProvider provider, Clock clock, RecordingGateway gateway,
        Guid eventId, Guid actorId, string body = "Avviso operativo sintetico")
    {
        var service = MessageTestSupport.Service(provider, clock, gateway);
        await MessageTestSupport.ActivateAsync(provider, eventId, actorId, clock.Utc);
        await MessageTestSupport.QueueAsync(service, actorId, eventId, body);
        return service;
    }

    [Fact]
    public async Task ImmediateConfirmationSendsOnceAndRecordsGatewayConfirmation()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(Noon); var gateway = new RecordingGateway();
        var service = await QueueAsync(scope.ServiceProvider, clock, gateway, factory.EventId, factory.OrganizerPersonId);
        Assert.Empty(gateway.OperationIds); // confirming only authorizes; it never transmits.
        var outcome = await service.DispatchAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.Equal(new DispatchOutcome(1, 0, 0, 0, 0), outcome);
        Assert.Equal(1, gateway.Sends);
        var message = Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Messages);
        Assert.Equal(MessageDeliveryMode.WhatsApp, message.DeliveryMode);
        Assert.Equal(OutboundMessageStatus.Sent, message.Status);
        Assert.Equal(message.Id, Assert.Single(gateway.OperationIds));
        Assert.Equal(Noon, message.SentAtUtc); Assert.Equal(1, message.Attempts); Assert.Equal(Noon, message.UpdatedAtUtc);
        Assert.Equal(0, (await service.DispatchAsync(factory.OrganizerPersonId, factory.EventId)).Attempted);
        Assert.Equal(1, gateway.Sends); // the second pass finds nothing left to send.
        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Single(await db.Set<LocalAudit>().Where(x => x.Action == "Sent").ToListAsync());
        Assert.Empty(await db.Set<LocalAudit>().Where(x => x.Action == "SentSimulated").ToListAsync());
    }

    [Fact]
    public async Task ConfirmationRequiresActiveGroupAndExplicitAuthorization()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(Noon); var gateway = new RecordingGateway();
        var service = MessageTestSupport.Service(scope.ServiceProvider, clock, gateway);
        var id = await service.SaveDraftAsync(factory.OrganizerPersonId, factory.EventId, null,
            new MessageEdit(Guid.NewGuid(), "Avviso", MessageKind.Operational, null, 0));
        // No active WhatsApp connection: the draft cannot be authorized.
        await Assert.ThrowsAsync<LocalValidationException>(() =>
            service.TransitionAsync(factory.OrganizerPersonId, factory.EventId, id, 0, true, false));
        await Assert.ThrowsAsync<LocalValidationException>(() =>
            service.TransitionAsync(factory.OrganizerPersonId, factory.EventId, id, 0, false, false));
        await MessageTestSupport.ActivateAsync(scope.ServiceProvider, factory.EventId, factory.OrganizerPersonId, Noon);
        Assert.True(await service.TransitionAsync(factory.OrganizerPersonId, factory.EventId, id, 0, true, false));
        var message = Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Messages);
        Assert.Equal(OutboundMessageStatus.Queued, message.Status);
        Assert.Equal(factory.OrganizerPersonId, message.ConfirmedByActorId);
        Assert.Equal(Noon, message.ConfirmedAtUtc);
        Assert.Equal(RecordingGateway.SyntheticGroup, message.TargetGroupId);
        Assert.Empty(gateway.OperationIds);
    }

    [Fact]
    public async Task TimeoutIsRecordedAsUncertainAndNeverRetried()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(Noon);
        var gateway = new RecordingGateway { OnSend = (_, _, _) => Task.FromException<GatewaySendResult>(new TaskCanceledException()) };
        var service = await QueueAsync(scope.ServiceProvider, clock, gateway, factory.EventId, factory.OrganizerPersonId);
        Assert.Equal(new DispatchOutcome(0, 0, 1, 0, 0), await service.DispatchAsync(factory.OrganizerPersonId, factory.EventId));
        var message = Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Messages);
        Assert.Equal(OutboundMessageStatus.Uncertain, message.Status);
        Assert.Null(message.SentAtUtc); Assert.Equal(1, message.Attempts); Assert.Contains("non determinabile", message.LastError);
        // Automatic processing never retries an ambiguous outcome.
        clock.Utc = Noon.AddDays(1);
        Assert.Equal(0, (await service.DispatchAsync(factory.OrganizerPersonId, factory.EventId)).Attempted);
        Assert.Equal(1, gateway.Sends);
    }

    [Fact]
    public async Task GatewayRefusalBeforeTransmissionIsFailedWithTechnicalCode()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(Noon);
        var gateway = new RecordingGateway { OnSend = (_, _, _) => Task.FromException<GatewaySendResult>(new WhatsAppGatewayRejectedException("invalid_send")) };
        var service = await QueueAsync(scope.ServiceProvider, clock, gateway, factory.EventId, factory.OrganizerPersonId);
        Assert.Equal(new DispatchOutcome(0, 1, 0, 0, 0), await service.DispatchAsync(factory.OrganizerPersonId, factory.EventId));
        var message = Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Messages);
        Assert.Equal(OutboundMessageStatus.Failed, message.Status);
        Assert.Contains("invalid_send", message.LastError);
        Assert.Equal(0, (await service.DispatchAsync(factory.OrganizerPersonId, factory.EventId)).Attempted);
        Assert.Equal(1, gateway.Sends);
    }

    [Fact]
    public async Task LiveStatusSeparatesGatewayConnectionAndGroup()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(Noon); var gateway = new RecordingGateway();
        var service = MessageTestSupport.Service(scope.ServiceProvider, clock, gateway);
        await MessageTestSupport.ActivateAsync(scope.ServiceProvider, factory.EventId, factory.OrganizerPersonId, Noon);
        var status = await service.DeliveryStatusAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.True(status.GatewayEnabled); Assert.True(status.GatewayReachable);
        Assert.Equal("connected", status.GatewayState); Assert.True(status.GroupMatches); Assert.True(status.Ready);
        // The group configured locally differs from the one active on the device: sending stays blocked.
        gateway.ActiveGroup = "other-group@g.us";
        status = await service.DeliveryStatusAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.Equal("connected", status.GatewayState); Assert.False(status.GroupMatches); Assert.False(status.Ready);
        // A failing health check is reported as unreachable instead of hiding the stored connection.
        gateway.OnHealth = () => throw new LocalValidationException("Gateway locale non raggiungibile.");
        status = await service.DeliveryStatusAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.False(status.GatewayReachable); Assert.Equal("unreachable", status.GatewayState);
        Assert.True(status.ActiveGroupId is not null); Assert.False(status.Ready);
        var disabled = MessageTestSupport.Service(scope.ServiceProvider, clock, gateway, enabled: false);
        status = await disabled.DeliveryStatusAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.False(status.GatewayEnabled); Assert.Equal("disabled", status.GatewayState);
        await Assert.ThrowsAsync<LocalAccessException>(() =>
            service.DeliveryStatusAsync(Guid.NewGuid(), factory.EventId));
    }

    [Fact]
    public async Task DisabledGatewayAndPauseKeepMessagesQueuedWithoutAttempts()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(Noon); var gateway = new RecordingGateway();
        var service = await QueueAsync(scope.ServiceProvider, clock, gateway, factory.EventId, factory.OrganizerPersonId);
        // Gateway disabled at runtime: the confirmed message stays queued with no attempt.
        var disabledService = MessageTestSupport.Service(scope.ServiceProvider, clock, gateway, enabled: false);
        Assert.Equal(DispatchOutcome.Empty, await disabledService.DispatchAsync(factory.OrganizerPersonId, factory.EventId));
        Assert.Equal(OutboundMessageStatus.Queued,
            Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Messages).Status);
        Assert.Empty(gateway.OperationIds);
        Assert.Equal(0, (await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Messages.Single().Attempts);

        await service.ConfigureAsync(factory.OrganizerPersonId, factory.EventId, new SettingsEdit(true, 23, 9, 3, 5, 0));
        var paused = await service.DispatchAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.Equal(0, paused.Attempted);
        Assert.Equal(OutboundMessageStatus.Queued,
            Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Messages).Status);
        Assert.Empty(gateway.OperationIds);
    }

    [Fact]
    public async Task GroupChangedAfterConfirmationStopsTheSend()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(Noon); var gateway = new RecordingGateway();
        var service = await QueueAsync(scope.ServiceProvider, clock, gateway, factory.EventId, factory.OrganizerPersonId);
        await MessageTestSupport.ActivateAsync(scope.ServiceProvider, factory.EventId, factory.OrganizerPersonId, Noon, "other-group@g.us");
        var outcome = await service.DispatchAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.Equal(DispatchOutcome.Empty, outcome);
        var message = Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Messages);
        Assert.Equal(OutboundMessageStatus.Queued, message.Status);
        Assert.Empty(gateway.OperationIds);
    }

    [Fact]
    public async Task ConfirmationWhoseOrganizerLostAccessIsNotSent()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(Noon); var gateway = new RecordingGateway();
        var service = await QueueAsync(scope.ServiceProvider, clock, gateway, factory.EventId, factory.OrganizerPersonId);
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var other = new Person("Secondo organizzatore sintetico");
        db.AddRange(other, new EventOrganizer(factory.EventId, other.Id), new OrganizerUser
        { Id = Guid.NewGuid(), PersonId = other.Id, UserName = "delivery-other", NormalizedUserName = "DELIVERY-OTHER" });
        var assignment = await db.EventOrganizers.SingleAsync(x => x.PersonId == factory.OrganizerPersonId);
        assignment.SetActive(false); await db.SaveChangesAsync();
        // A different authorized organizer can process the queue, but not a confirmation whose owner lost access.
        Assert.Equal(DispatchOutcome.Empty, await service.DispatchAsync(other.Id, factory.EventId));
        Assert.Empty(gateway.OperationIds);
        Assert.Equal(OutboundMessageStatus.Queued,
            Assert.Single((await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<OutboundMessage>().AsNoTracking().ToListAsync())).Status);
        // The revoked organizer cannot even start the processing.
        await Assert.ThrowsAsync<LocalAccessException>(() => service.DispatchAsync(factory.OrganizerPersonId, factory.EventId));
    }

    [Fact]
    public async Task InterruptedClaimBecomesUncertainWithoutSending()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(Noon); var gateway = new RecordingGateway();
        var service = await QueueAsync(scope.ServiceProvider, clock, gateway, factory.EventId, factory.OrganizerPersonId);
        // Simulate a crash between the persisted claim and the transport call.
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var message = await db.Set<OutboundMessage>().SingleAsync();
        message.ClaimSending(Noon); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        clock.Utc = Noon.AddMinutes(10);
        var outcome = await service.DispatchAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.Equal(1, outcome.Recovered);
        Assert.Empty(gateway.OperationIds);
        var recovered = Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Messages);
        Assert.Equal(OutboundMessageStatus.Uncertain, recovered.Status);
        Assert.Contains("processo interrotto", recovered.LastError);
        Assert.Equal(1, recovered.Attempts);
        Assert.Single(await db.Set<LocalAudit>().Where(x => x.Action == "UncertainRecovered").ToListAsync());
    }

    [Fact]
    public async Task ConcurrentDispatchOfTheSameMessageSendsOnce()
    {
        await using var factory = new LocalWebFactory();
        var clock = new Clock(Noon); var gateway = new RecordingGateway { ReleaseSend = new(), SendStarted = new() };
        await using (var seed = factory.Services.CreateAsyncScope())
            await QueueAsync(seed.ServiceProvider, clock, gateway, factory.EventId, factory.OrganizerPersonId);
        await using var first = factory.Services.CreateAsyncScope();
        await using var second = factory.Services.CreateAsyncScope();
        var firstService = MessageTestSupport.Service(first.ServiceProvider, clock, gateway);
        var secondService = MessageTestSupport.Service(second.ServiceProvider, clock, gateway);
        var running = firstService.DispatchAsync(factory.OrganizerPersonId, factory.EventId);
        await gateway.SendStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var queued = secondService.DispatchAsync(factory.OrganizerPersonId, factory.EventId);
        gateway.ReleaseSend!.TrySetResult();
        await Task.WhenAll(running, queued);
        Assert.Equal(1, gateway.Sends);
        Assert.Single(gateway.OperationIds);
    }

    [Fact]
    public async Task ManualResolutionRecordsOperatorOutcomeWithoutAnotherSend()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(Noon);
        var gateway = new RecordingGateway { OnSend = (_, _, _) => Task.FromResult(new GatewaySendResult("uncertain")) };
        var service = await QueueAsync(scope.ServiceProvider, clock, gateway, factory.EventId, factory.OrganizerPersonId);
        await service.DispatchAsync(factory.OrganizerPersonId, factory.EventId);
        var message = Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Messages);
        Assert.Equal(OutboundMessageStatus.Uncertain, message.Status);
        await Assert.ThrowsAsync<LocalValidationException>(() =>
            service.ResolveAsync(factory.OrganizerPersonId, factory.EventId, message.Id, message.Revision, true, "verificato", false));
        await Assert.ThrowsAsync<LocalValidationException>(() =>
            service.ResolveAsync(factory.OrganizerPersonId, factory.EventId, message.Id, message.Revision, true, " ", true));
        await Assert.ThrowsAsync<LocalAccessException>(() =>
            service.ResolveAsync(Guid.NewGuid(), factory.EventId, message.Id, message.Revision, true, "verificato", true));
        clock.Utc = Noon.AddMinutes(5);
        Assert.True(await service.ResolveAsync(factory.OrganizerPersonId, factory.EventId, message.Id, message.Revision, true, "presente nel gruppo", true));
        var resolved = Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Messages);
        Assert.Equal(OutboundMessageStatus.Sent, resolved.Status);
        Assert.Equal("presente nel gruppo", resolved.OutcomeNote);
        Assert.Equal(1, gateway.Sends);
        Assert.Equal(0, (await service.DispatchAsync(factory.OrganizerPersonId, factory.EventId)).Attempted);
        Assert.Equal(1, gateway.Sends);
    }

    [Fact]
    public async Task DispatchAndDraftWritesRequireAnAuthorizedOrganizer()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(Noon); var gateway = new RecordingGateway();
        var service = MessageTestSupport.Service(scope.ServiceProvider, clock, gateway);
        await Assert.ThrowsAsync<LocalAccessException>(() => service.DispatchAsync(Guid.NewGuid(), factory.EventId));
        await Assert.ThrowsAsync<LocalAccessException>(() =>
            service.SaveDraftAsync(Guid.NewGuid(), factory.EventId, null, new MessageEdit(Guid.NewGuid(), "x", MessageKind.Operational, null, 0)));
        Assert.Empty(gateway.OperationIds);
    }

    [Fact]
    public async Task UpgradeKeepsLegacyRowsSimulatedAndExcludedFromDispatch()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True"); await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261108152657_AddAttendanceFamilyStatus");
        var ev = new Event("Evento legacy sintetico", Noon); db.Add(ev); await db.SaveChangesAsync();
        var queued = Guid.NewGuid(); var sent = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO OutboundMessages (Id, EventId, Revision, Body, Kind, Status, ScheduledAtUtc, SentAtUtc, SummaryFingerprint) VALUES ({queued}, {ev.Id}, {0}, {"Vecchia coda simulata"}, {0}, {1}, {Noon.AddDays(-3)}, NULL, NULL)");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO OutboundMessages (Id, EventId, Revision, Body, Kind, Status, ScheduledAtUtc, SentAtUtc, SummaryFingerprint) VALUES ({sent}, {ev.Id}, {2}, {"Vecchio invio simulato"}, {1}, {2}, {Noon.AddDays(-4)}, {Noon.AddDays(-4)}, NULL)");
        db.ChangeTracker.Clear(); await db.Database.MigrateAsync();
        var rows = await db.Set<OutboundMessage>().AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, rows.Count); Assert.All(rows, x => Assert.Equal(MessageDeliveryMode.Simulated, x.DeliveryMode));
        Assert.All(rows, x => Assert.Equal(Noon.AddDays(x.Id == queued ? -3 : -4), x.CreatedAtUtc));
        // No candidate even though the queued row is overdue: the dispatcher only selects WhatsApp rows.
        Assert.Empty(await new OutboxDispatchStore(db).DispatchCandidatesAsync(Noon, Noon.AddHours(-1)));
        Assert.Equal(0L, await db.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM pragma_foreign_key_check").SingleAsync());
        Assert.Equal("ok", await db.Database.SqlQueryRaw<string>("SELECT integrity_check AS Value FROM pragma_integrity_check").SingleAsync());
    }

    [Fact]
    public void SimulationCannotMarkAWhatsAppMessageAsSent()
    {
        var message = new OutboundMessage(Guid.NewGuid(), Guid.NewGuid(), "WhatsApp reale", MessageKind.Operational, Noon,
            MessageDeliveryMode.WhatsApp);
        Assert.Throws<InvalidOperationException>(() => message.Queue(true));
        Assert.Throws<InvalidOperationException>(() => message.MarkSent(Noon));
        Assert.Equal(0, message.Revision);
    }

    [Fact]
    public async Task WorkerHonoursConfigurationAndDoesNotRepeatCompletedSends()
    {
        await using var baseFactory = new LocalWebFactory();
        var gateway = new RecordingGateway();
        var clock = new Clock(Noon);
        var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<WhatsAppGatewayOptions>(); services.AddSingleton(MessageTestSupport.Options());
            services.RemoveAll<IWhatsAppGateway>(); services.AddSingleton<IWhatsAppGateway>(gateway);
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
        }));
        await using (var scope = factory.Services.CreateAsyncScope())
            await QueueAsync(scope.ServiceProvider, clock, gateway, baseFactory.EventId, baseFactory.OrganizerPersonId);
        var disabled = new MessageDispatchWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Configuration(false), clock, NullLogger<MessageDispatchWorker>.Instance);
        await disabled.RunOnceAsync();
        Assert.Empty(gateway.OperationIds);
        var enabled = new MessageDispatchWorker(factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Configuration(true), clock, NullLogger<MessageDispatchWorker>.Instance);
        await enabled.RunOnceAsync();
        Assert.Single(gateway.OperationIds);
        await enabled.RunOnceAsync();
        Assert.Single(gateway.OperationIds);
        using var check = factory.Services.CreateScope();
        Assert.Equal(OutboundMessageStatus.Sent,
            (await check.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<OutboundMessage>().SingleAsync()).Status);
    }

    [Fact]
    public async Task HttpDispatchRequiresConfirmationAndRealMessageStatusIsVisible()
    {
        await using var baseFactory = new LocalWebFactory();
        var gateway = new RecordingGateway();
        var clock = new Clock(Noon);
        var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<WhatsAppGatewayOptions>(); services.AddSingleton(MessageTestSupport.Options());
            services.RemoveAll<IWhatsAppGateway>(); services.AddSingleton<IWhatsAppGateway>(gateway);
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
        }));
        Guid id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await MessageTestSupport.ActivateAsync(scope.ServiceProvider, baseFactory.EventId, baseFactory.OrganizerPersonId, Noon);
            var service = MessageTestSupport.Service(scope.ServiceProvider, clock, gateway);
            id = await MessageTestSupport.QueueAsync(service, baseFactory.OrganizerPersonId, baseFactory.EventId, "Messaggio sintetico HTTP");
        }
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", login)));
        var preview = await client.GetStringAsync($"/Messages/Preview?eventId={baseFactory.EventId}&id={id}");
        Assert.Contains("Confermato, in attesa", preview);
        var index = await client.GetStringAsync("/Messages?eventId=" + baseFactory.EventId);
        Assert.Contains("Elabora ora", index); Assert.Contains("Stato della consegna", index);
        var live = await client.GetStringAsync("/Messages/Status?eventId=" + baseFactory.EventId);
        Assert.Contains("\"gatewayState\":\"connected\"", live); Assert.Contains("\"groupMatches\":true", live);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Messages/Dispatch", Form(("eventId", baseFactory.EventId.ToString()), ("__RequestVerificationToken", Token(preview))))).StatusCode);
        var dispatch = await client.PostAsync("/Messages/Dispatch", Form(("eventId", baseFactory.EventId.ToString()), ("confirmed", "true"), ("__RequestVerificationToken", Token(preview))));
        Assert.Equal(HttpStatusCode.Redirect, dispatch.StatusCode);
        var refreshed = await client.GetStringAsync($"/Messages/Preview?eventId={baseFactory.EventId}&id={id}");
        Assert.Contains("Invio confermato dal gateway", refreshed);
        Assert.Single(gateway.OperationIds);
    }

    private static IConfiguration Configuration(bool enabled) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Messaging:Dispatcher:Enabled"] = enabled.ToString(),
            ["Messaging:Dispatcher:IntervalSeconds"] = "30"
        }).Build();
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) => new(fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
}
