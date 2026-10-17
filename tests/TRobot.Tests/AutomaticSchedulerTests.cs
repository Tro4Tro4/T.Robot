using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TRobot.Application.Local;
using TRobot.Application.Messaging;
using TRobot.Domain;
using TRobot.Infrastructure.Identity;
using TRobot.Infrastructure.Persistence;
using TRobot.Web.Services;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class AutomaticSchedulerTests
{
    private static readonly DateTime Noon = new(2026, 12, 31, 11, 0, 0, DateTimeKind.Utc);
    private sealed class Clock : TimeProvider
    {
        public DateTime Utc { get; set; } = Noon;
        public override DateTimeOffset GetUtcNow() => new(Utc);
    }
    private static MessageService Service(IServiceProvider provider, TimeProvider clock, ILocalStore? replacement = null)
    {
        var store = replacement ?? provider.GetRequiredService<ILocalStore>();
        return new(store, new LocalOperations(store, clock), clock);
    }
    private static IConfiguration Configuration(bool enabled = true) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["LocalScheduler:Enabled"] = enabled.ToString() }).Build();
    private static AutomaticSchedulerWorker Worker(IServiceScopeFactory scopes, TimeProvider clock, bool enabled = true) =>
        new(scopes, clock, Configuration(enabled), NullLogger<AutomaticSchedulerWorker>.Instance);
    private static async Task EventDay(IServiceProvider provider, Guid eventId, DateTime utc)
    {
        var db = provider.GetRequiredService<TRobotDbContext>();
        var ev = await db.Events.SingleAsync(x => x.Id == eventId);
        ev.UpdateDetails(ev.Name, utc, ev.TimeZoneId, ev.Location, ev.Program);
        await db.SaveChangesAsync();
    }
    private static async Task<Guid> Queue(MessageService service, Guid actorId, Guid eventId, MessageKind kind = MessageKind.Operational)
    {
        var id = await service.SaveDraftAsync(actorId, eventId, null, new(Guid.NewGuid(), "Avviso sintetico", kind, null, 0));
        await service.TransitionAsync(actorId, eventId, id, 0, true, false);
        return id;
    }

    [Fact]
    public void DomainActivationRequiresConfirmationPreservesActorAndSupportsExplicitTakeover()
    {
        var settings = new BotSettings(Guid.NewGuid()); var actor = Guid.NewGuid(); var next = Guid.NewGuid();
        Assert.False(settings.AutomaticEnabled); Assert.Null(settings.AutomaticActorId);
        Assert.Throws<ArgumentException>(() => settings.SetAutomatic(true, actor, false));
        Assert.Throws<ArgumentException>(() => settings.SetAutomatic(true, Guid.Empty, true));
        Assert.Equal(0, settings.Revision);
        Assert.True(settings.SetAutomatic(true, actor, true));
        Assert.False(settings.SetAutomatic(true, actor, true));
        Assert.True(settings.SetAutomatic(true, next, true));
        Assert.Equal(next, settings.AutomaticActorId); Assert.Equal(2, settings.Revision);
        Assert.True(settings.SetAutomatic(false, actor, true));
        Assert.Equal(next, settings.AutomaticActorId); Assert.False(settings.AutomaticEnabled);
    }

    [Fact]
    public async Task UpgradeLeavesExistingSettingsDisabledAndPreservesTheirValues()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261017022511_SimulationActions");
        var ev = new Event("Evento precedente", Noon); db.Add(ev); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO BotSettings (Id, EventId, Revision, Paused, QuietStartHour, QuietEndHour, MaxSpontaneousPerDay, Sarcasm) VALUES ({ev.Id}, {ev.Id}, 7, 1, 22, 8, 2, 4)");
        await db.Database.MigrateAsync();
        var settings = await db.Set<BotSettings>().SingleAsync();
        Assert.False(settings.AutomaticEnabled); Assert.Null(settings.AutomaticActorId);
        Assert.Equal(7, settings.Revision); Assert.True(settings.Paused);
        Assert.Equal(22, settings.QuietStartHour); Assert.Equal(8, settings.QuietEndHour);
        Assert.Equal(2, settings.MaxSpontaneousPerDay); Assert.Equal(4, settings.Sarcasm);
        Assert.Empty(await new AutomaticSchedulerStore(db).CandidatesAsync());
    }

    [Fact]
    public async Task ActivationProtectsAccessConfirmationRevisionAndNoopDoesNotCreateSettings()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(); var service = Service(scope.ServiceProvider, clock);
        var actor = factory.OrganizerPersonId; var eventId = factory.EventId;
        Assert.Equal(new TickResult(0, false), await service.TickAutomaticAsync(actor, eventId));
        Assert.False(await service.ConfigureAutomaticAsync(actor, eventId, false, 0, true));
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Empty(await db.Set<BotSettings>().ToListAsync()); Assert.Empty(await db.Set<LocalAudit>().ToListAsync());
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ConfigureAutomaticAsync(Guid.NewGuid(), eventId, true, 0, true));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.ConfigureAutomaticAsync(actor, eventId, true, 0, false));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.ConfigureAutomaticAsync(actor, eventId, true, -1, true));
        Assert.True(await service.ConfigureAutomaticAsync(actor, eventId, true, 0, true));
        Assert.False(await service.ConfigureAutomaticAsync(actor, eventId, true, 1, true));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.ConfigureAutomaticAsync(actor, eventId, false, 0, true));
        Assert.Equal(1, await db.Set<LocalAudit>().CountAsync());
        Assert.Equal(1, (await service.ListAsync(actor, eventId)).Settings.Revision);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task StaleCandidatesCannotRunAfterAccountOrAssignmentRevocationAndResumeWhenRestored(bool account)
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(); var service = Service(scope.ServiceProvider, clock);
        var actor = factory.OrganizerPersonId; var eventId = factory.EventId;
        await EventDay(scope.ServiceProvider, eventId, Noon);
        var id = await Queue(service, actor, eventId);
        await service.ConfigureAutomaticAsync(actor, eventId, true, 0, true);
        var discovery = scope.ServiceProvider.GetRequiredService<IAutomaticSchedulerStore>();
        var candidate = Assert.Single(await discovery.CandidatesAsync());
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var user = await db.Users.SingleAsync(x => x.PersonId == actor);
        var assignment = await db.EventOrganizers.SingleAsync(x => x.PersonId == actor && x.EventId == eventId);
        if (account) user.IsEnabled = false; else assignment.SetActive(false);
        await db.SaveChangesAsync();
        Assert.Empty(await discovery.CandidatesAsync());
        await Assert.ThrowsAsync<LocalAccessException>(() => service.TickAutomaticAsync(candidate.ActorId, candidate.EventId));
        Assert.Equal(OutboundMessageStatus.Queued, (await db.Set<OutboundMessage>().AsNoTracking().SingleAsync(x => x.Id == id)).Status);
        user = await db.Users.SingleAsync(x => x.PersonId == actor);
        assignment = await db.EventOrganizers.SingleAsync(x => x.PersonId == actor && x.EventId == eventId);
        if (account) user.IsEnabled = true; else assignment.SetActive(true);
        await db.SaveChangesAsync();
        Assert.Single(await discovery.CandidatesAsync());
        Assert.Equal(new TickResult(1, false), await service.TickAutomaticAsync(actor, eventId));
        Assert.True((await service.ListAsync(actor, eventId)).Settings.AutomaticEnabled);
    }

    [Fact]
    public async Task StaleCandidatesObserveDisableAndActivatorReplacementWithinTransaction()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(); var service = Service(scope.ServiceProvider, clock);
        var actor = factory.OrganizerPersonId; var eventId = factory.EventId;
        await EventDay(scope.ServiceProvider, eventId, Noon);
        await Queue(service, actor, eventId);
        await service.ConfigureAutomaticAsync(actor, eventId, true, 0, true);
        await service.ConfigureAutomaticAsync(actor, eventId, false, 1, true);
        Assert.Equal(new TickResult(0, false), await service.TickAutomaticAsync(actor, eventId));
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var other = new Person("Altro organizzatore sintetico");
        db.AddRange(other, new EventOrganizer(eventId, other.Id), new OrganizerUser
        { Id = Guid.NewGuid(), PersonId = other.Id, UserName = "other-scheduler", NormalizedUserName = "OTHER-SCHEDULER" });
        await db.SaveChangesAsync();
        await service.ConfigureAutomaticAsync(other.Id, eventId, true, 2, true);
        var audits = await db.Set<LocalAudit>().CountAsync();
        Assert.Equal(new TickResult(0, false), await service.TickAutomaticAsync(actor, eventId));
        Assert.Equal(audits, await db.Set<LocalAudit>().CountAsync());
        Assert.Equal(3, (await service.ListAsync(actor, eventId)).Settings.Revision);
        Assert.Equal(new TickResult(1, false), await service.TickAutomaticAsync(other.Id, eventId));
        var sentAudit = await db.Set<LocalAudit>().SingleAsync(x => x.Action == "SentSimulated");
        Assert.Equal(other.Id, sentAudit.ActorId);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task ManualAndAutomaticSharePauseQuietDailyCapAndOperationalException(bool automatic)
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(); var service = Service(scope.ServiceProvider, clock);
        var actor = factory.OrganizerPersonId; var eventId = factory.EventId;
        await EventDay(scope.ServiceProvider, eventId, Noon);
        await service.ConfigureAutomaticAsync(actor, eventId, true, 0, true);
        for (var i = 0; i < 4; i++) await Queue(service, actor, eventId, MessageKind.Entertainment);
        await Queue(service, actor, eventId);
        Task<TickResult> Tick() => automatic ? service.TickAutomaticAsync(actor, eventId) : service.TickAsync(actor, eventId, true);
        await service.ConfigureAsync(actor, eventId, new(true, 23, 9, 2, 5, 1));
        Assert.Equal(new TickResult(0, false), await Tick());
        await service.ConfigureAsync(actor, eventId, new(false, 23, 9, 2, 5, 2));
        Assert.Equal(new TickResult(3, false), await Tick()); // two spontaneous + one operational
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var audits = await db.Set<LocalAudit>().CountAsync();
        Assert.Equal(new TickResult(0, false), await Tick());
        Assert.Equal(audits, await db.Set<LocalAudit>().CountAsync());
        Assert.Equal(3, (await service.ListAsync(actor, eventId)).Settings.Revision);
        clock.Utc = Noon.AddHours(12); // next local day, within quiet hours
        await Queue(service, actor, eventId);
        Assert.Equal(new TickResult(1, false), await Tick());
        clock.Utc = Noon.AddDays(1);
        Assert.Equal(new TickResult(2, false), await Tick());
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task ManualAndAutomaticShareSummaryNoveltyQuietQueueAndEventDaySuppression(bool automatic)
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(); var service = Service(scope.ServiceProvider, clock);
        var actor = factory.OrganizerPersonId; var eventId = factory.EventId;
        await EventDay(scope.ServiceProvider, eventId, Noon.AddDays(40));
        await service.ConfigureAutomaticAsync(actor, eventId, true, 0, true);
        Task<TickResult> Tick() => automatic ? service.TickAutomaticAsync(actor, eventId) : service.TickAsync(actor, eventId, true);
        Assert.Equal(new TickResult(1, true), await Tick());
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var audits = await db.Set<LocalAudit>().CountAsync();
        var revision = (await service.ListAsync(actor, eventId)).Settings.Revision;
        clock.Utc = Noon.AddDays(8);
        Assert.Equal(new TickResult(0, false), await Tick());
        Assert.Equal(audits, await db.Set<LocalAudit>().CountAsync());
        Assert.Equal(revision, (await service.ListAsync(actor, eventId)).Settings.Revision);
        db.Add(new OrganizationTask(eventId, "Titolo privato non pubblicabile", "Nota privata", TaskPriority.Normal, null, null));
        await db.SaveChangesAsync();
        clock.Utc = Noon.AddDays(8).AddHours(12);
        Assert.Equal(new TickResult(0, true), await Tick());
        Assert.Equal(new TickResult(0, false), await Tick());
        var messages = (await service.ListAsync(actor, eventId)).Messages;
        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => { Assert.DoesNotContain("Titolo privato", m.Body); Assert.DoesNotContain("Nota privata", m.Body); });
        await EventDay(scope.ServiceProvider, eventId, clock.Utc);
        clock.Utc = clock.Utc.AddHours(12);
        Assert.Equal(new TickResult(0, false), await Tick());
        Assert.Single((await service.ListAsync(actor, eventId)).Messages, m => m.Status == OutboundMessageStatus.Queued);
    }

    [Fact]
    public async Task AutomaticAuditFailureRollsBackOutboxSummaryAndRevision()
    {
        var interceptor = new FailAudit(); await using var factory = new LocalWebFactory { SaveInterceptor = interceptor };
        using var scope = factory.Services.CreateScope(); var clock = new Clock();
        var service = Service(scope.ServiceProvider, clock); var actor = factory.OrganizerPersonId; var eventId = factory.EventId;
        await EventDay(scope.ServiceProvider, eventId, Noon.AddDays(40));
        await service.ConfigureAutomaticAsync(actor, eventId, true, 0, true);
        interceptor.Armed = true;
        await Assert.ThrowsAsync<LocalValidationException>(() => service.TickAutomaticAsync(actor, eventId));
        interceptor.Armed = false;
        var page = await service.ListAsync(actor, eventId);
        Assert.Empty(page.Messages); Assert.Null(page.Settings.LastSummaryAtUtc); Assert.Equal(1, page.Settings.Revision);
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<LocalAudit>().CountAsync());
        Assert.Equal(new TickResult(1, true), await service.TickAutomaticAsync(actor, eventId));
    }

    [Fact]
    public async Task WorkerUsesFreshScopesIsolatesFailuresAndDoesNotRepeatCompletedWork()
    {
        await using var factory = new LocalWebFactory(); var clock = new Clock();
        using (var setup = factory.Services.CreateScope())
        {
            var service = Service(setup.ServiceProvider, clock);
            await EventDay(setup.ServiceProvider, factory.EventId, Noon);
            await Queue(service, factory.OrganizerPersonId, factory.EventId);
            await service.ConfigureAutomaticAsync(factory.OrganizerPersonId, factory.EventId, true, 0, true);
        }
        var broken = Guid.NewGuid();
        var candidates = new[] { new AutomaticScheduleCandidate(broken, factory.OrganizerPersonId), new(factory.EventId, factory.OrganizerPersonId) };
        var scopes = new WorkerScopes(factory.Services.GetRequiredService<IServiceScopeFactory>(), clock, candidates,
            (eventId, _) => { if (eventId == broken) throw new InvalidOperationException("Private details must not be logged"); });
        var worker = Worker(scopes, clock);
        await worker.RunOnceAsync(); await worker.RunOnceAsync();
        Assert.Equal(6, scopes.Created); Assert.Equal(6, scopes.Disposed);
        using var check = factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal(OutboundMessageStatus.Sent, (await db.Set<OutboundMessage>().SingleAsync()).Status);
        Assert.Equal(1, await db.Set<LocalAudit>().CountAsync(x => x.Action == "SentSimulated"));
    }

    [Fact]
    public async Task WorkerCancellationStopsRemainingEventsAndDisabledInstanceCompletesWithoutScopes()
    {
        await using var factory = new LocalWebFactory(); var clock = new Clock(); using var cancellation = new CancellationTokenSource();
        var first = Guid.NewGuid(); var reached = new List<Guid>();
        var scopes = new WorkerScopes(factory.Services.GetRequiredService<IServiceScopeFactory>(), clock,
            [new(first, factory.OrganizerPersonId), new(factory.EventId, factory.OrganizerPersonId)],
            (eventId, token) => { reached.Add(eventId); cancellation.Cancel(); token.ThrowIfCancellationRequested(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Worker(scopes, clock).RunOnceAsync(cancellation.Token));
        Assert.Equal(new[] { first }, reached); Assert.Equal(scopes.Created, scopes.Disposed);
        var before = scopes.Created;
        using var disabled = Worker(scopes, clock, false);
        await disabled.StartAsync(CancellationToken.None);
        Assert.NotNull(disabled.ExecuteTask);
        await disabled.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(disabled.ExecuteTask.IsCompletedSuccessfully);
        await disabled.RunOnceAsync(); await disabled.StopAsync(CancellationToken.None);
        Assert.Equal(before, scopes.Created);
    }

    [Fact]
    public async Task ConcurrentManualAndAutomaticTicksCannotSendSameMessageTwice()
    {
        var path = Path.Combine(Path.GetTempPath(), "trobot-scheduler-" + Guid.NewGuid().ToString("N") + ".db");
        var options = new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, ForeignKeys = true }.ToString()).Options;
        var actor = new Person("Attivatore sintetico"); var ev = new Event("Evento sintetico", Noon);
        var clock = new Clock();
        try
        {
            await using (var seed = new TRobotDbContext(options))
            {
                await seed.Database.MigrateAsync();
                var settings = new BotSettings(ev.Id); settings.SetAutomatic(true, actor.Id, true);
                var message = new OutboundMessage(ev.Id, Guid.NewGuid(), "Avviso approvato", MessageKind.Operational, Noon); message.Queue(true);
                seed.AddRange(ev, actor, new EventOrganizer(ev.Id, actor.Id), new OrganizerUser
                { Id = Guid.NewGuid(), PersonId = actor.Id, UserName = "synthetic", NormalizedUserName = "SYNTHETIC" }, settings, message);
                await seed.SaveChangesAsync();
            }
            async Task<int> Tick(bool automatic)
            {
                await using var db = new TRobotDbContext(options);
                var store = new LocalStore(db, clock); var service = new MessageService(store, new LocalOperations(store, clock), clock);
                try { return (automatic ? await service.TickAutomaticAsync(actor.Id, ev.Id) : await service.TickAsync(actor.Id, ev.Id, true)).Sent; }
                catch (LocalConflictException) { return 0; }
            }
            var results = await Task.WhenAll(Task.Run(() => Tick(true)), Task.Run(() => Tick(false)));
            Assert.Equal(1, results.Sum());
            await using var check = new TRobotDbContext(options);
            Assert.Equal(1, await check.Set<LocalAudit>().CountAsync(x => x.Action == "SentSimulated"));
            Assert.Equal(OutboundMessageStatus.Sent, (await check.Set<OutboundMessage>().SingleAsync()).Status);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix);
        }
    }

    [Fact]
    public async Task HttpAutomationRequiresAuthenticationCsrfConfirmationAndPresentRevisionAndDisplaysInstanceState()
    {
        await using var factory = new LocalWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Messages/Automation", Form())).StatusCode);
        var login = await client.GetStringAsync("/Access/Login");
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", Token(login))));
        var page = await client.GetStringAsync("/Messages?eventId=" + factory.EventId);
        Assert.Contains("id=\"automation-status\">Disattivato", page);
        var token = Token(page);
        var common = new[] { ("EventId", factory.EventId.ToString()), ("Enabled", "true"), ("Revision", "0"), ("Confirmed", "true"), ("__RequestVerificationToken", token) };
        foreach (var missing in new[] { "__RequestVerificationToken", "Confirmed", "Revision" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Messages/Automation", Form(common.Where(x => x.Item1 != missing).ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Messages/Automation", Form(common))).StatusCode);
        page = await client.GetStringAsync("/Messages?eventId=" + factory.EventId);
        Assert.Contains("Sospeso: elaborazione automatica disattivata in questa istanza", page);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/Messages/Automation", Form(common))).StatusCode);
        using var scope = factory.Services.CreateScope();
        var settings = await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<BotSettings>().SingleAsync();
        Assert.True(settings.AutomaticEnabled); Assert.Equal(factory.OrganizerPersonId, settings.AutomaticActorId);
    }

    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] fields) => new(fields.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
    private sealed class FailAudit : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Armed && data.Context!.ChangeTracker.Entries<LocalAudit>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Errore audit sintetico");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class WorkerScopes(IServiceScopeFactory inner, TimeProvider clock, IReadOnlyList<AutomaticScheduleCandidate> candidates,
        Action<Guid, CancellationToken> beforeAuthorize) : IServiceScopeFactory
    {
        public int Created { get; private set; }
        public int Disposed { get; private set; }
        public IServiceScope CreateScope() { Created++; return new Scope(inner.CreateScope(), this, clock, candidates, beforeAuthorize); }
        private sealed class Scope(IServiceScope innerScope, WorkerScopes owner, TimeProvider clock,
            IReadOnlyList<AutomaticScheduleCandidate> candidates, Action<Guid, CancellationToken> beforeAuthorize) : IServiceScope, IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;
            public object? GetService(Type type)
            {
                if (type == typeof(IAutomaticSchedulerStore)) return new Discovery(candidates);
                if (type == typeof(MessageService))
                {
                    var store = new CheckedStore(innerScope.ServiceProvider.GetRequiredService<ILocalStore>(), beforeAuthorize);
                    return new MessageService(store, new LocalOperations(store, clock), clock);
                }
                return innerScope.ServiceProvider.GetService(type);
            }
            public void Dispose() { innerScope.Dispose(); owner.Disposed++; }
        }
    }
    private sealed class Discovery(IReadOnlyList<AutomaticScheduleCandidate> candidates) : IAutomaticSchedulerStore
    {
        public Task<IReadOnlyList<AutomaticScheduleCandidate>> CandidatesAsync(CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult(candidates); }
    }
    private sealed class CheckedStore(ILocalStore inner, Action<Guid, CancellationToken> beforeAuthorize) : ILocalStore
    {
        public Task<bool> CanManageAsync(Guid actorId, Guid eventId, CancellationToken ct) { beforeAuthorize(eventId, ct); return inner.CanManageAsync(actorId, eventId, ct); }
        public Task<Event?> EventAsync(Guid eventId, CancellationToken ct) => inner.EventAsync(eventId, ct);
        public Task<IReadOnlyList<LocalPerson>> PeopleAsync(Guid eventId, CancellationToken ct) => inner.PeopleAsync(eventId, ct);
        public Task<IReadOnlyList<LocalPerson>> ParticipantsAsync(Guid eventId, CancellationToken ct) => inner.ParticipantsAsync(eventId, ct);
        public Task<IReadOnlyList<LocalParticipation>> ParticipationsAsync(Guid eventId, CancellationToken ct) => inner.ParticipationsAsync(eventId, ct);
        public Task<PublicEventSnapshot> SnapshotAsync(Guid eventId, CancellationToken ct) => inner.SnapshotAsync(eventId, ct);
        public Task<T?> FindAsync<T>(Guid eventId, Guid id, CancellationToken ct) where T : class, ILocalRecord => inner.FindAsync<T>(eventId, id, ct);
        public Task<IReadOnlyList<T>> ListAsync<T>(Guid eventId, CancellationToken ct) where T : class, ILocalRecord => inner.ListAsync<T>(eventId, ct);
        public void Add<T>(T entity) where T : class => inner.Add(entity);
        public Task SaveAsync(CancellationToken ct) => inner.SaveAsync(ct);
        public Task<T> TransactionAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct) => inner.TransactionAsync(action, ct);
    }
}
