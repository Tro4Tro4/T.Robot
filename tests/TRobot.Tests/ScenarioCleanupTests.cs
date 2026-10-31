using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TRobot.Application.Maintenance;
using TRobot.Domain;
using TRobot.Infrastructure.Identity;
using TRobot.Infrastructure.Persistence;

namespace TRobot.Tests;

public sealed class ScenarioCleanupTests
{
    [Fact]
    public async Task DeletesOnlyScenarioIncludingCyclicPollAndPreservesIdentityGlobalAuditAndOtherEvent()
    {
        await using var f = await Fixture.Create();
        var preview = await f.Service.ExecuteAsync(f.Request());
        Assert.False(preview.Applied);
        Assert.Equal(1, preview.RemovedRows["Polls"]);
        Assert.Equal(1, preview.RemovedRows["PollOptions"]);
        Assert.Equal(2, await f.Db.Events.CountAsync());
        var result = await f.Service.ExecuteAsync(f.Apply(preview));
        Assert.True(result.Applied);
        Assert.Equal(preview.PreservedRowsFingerprint, result.PreservedRowsFingerprint);
        Assert.Equal(f.Other.Id, (await f.Db.Events.SingleAsync()).Id);
        Assert.Single(await f.Db.Users.ToArrayAsync());
        Assert.Single(await f.Db.People.ToArrayAsync());
        Assert.Single(await f.Db.EventParticipants.ToArrayAsync());
        Assert.Equal("AdministratorInitialized", (await f.Db.AccountAudits.SingleAsync()).Action);
        Assert.Empty(await f.Db.Set<Poll>().ToArrayAsync());
        Assert.Empty(await f.Db.Set<PollOption>().ToArrayAsync());
        Assert.Equal(1, await Scalar(f.Db, "PRAGMA foreign_keys"));
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("disabled")]
    [InlineData("not-admin")]
    [InlineData("unassigned")]
    [InlineData("wrong-event")]
    [InlineData("name")]
    [InlineData("revision")]
    [InlineData("confirmation")]
    [InlineData("backup")]
    [InlineData("stale")]
    public async Task RefusesUnsafeRequestWithoutDeletingAnything(string issue)
    {
        await using var f = await Fixture.Create();
        var preview = await f.Service.ExecuteAsync(f.Request());
        var request = f.Apply(preview);
        if (issue == "binding")
        {
            var connection = new WhatsAppConnection(ScenarioCleanupService.AllowedEventId);
            connection.Activate("test@g.us", f.Person.Id, DateTime.UtcNow); f.Db.Add(connection);
        }
        if (issue == "disabled") f.Admin.IsEnabled = false;
        if (issue == "not-admin") f.Admin.IsSystemAdministrator = false;
        if (issue == "unassigned") (await f.Db.EventOrganizers.SingleAsync()).SetActive(false);
        if (issue == "wrong-event") request = request with { EventId = f.Other.Id };
        if (issue == "name") f.Target.UpdateDetails("Diverso", f.Target.StartsAtUtc, f.Target.TimeZoneId);
        if (issue == "revision") request = request with { Revision = 99 };
        if (issue == "confirmation") request = request with { Confirmation = "yes" };
        if (issue == "backup") request = request with { BackupSha256 = null };
        if (issue == "stale") f.Person.Rename("Modificato dopo dry-run");
        await f.Db.SaveChangesAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => f.Service.ExecuteAsync(request));
        Assert.Equal(2, await f.Db.Events.CountAsync());
        Assert.Single(await f.Db.Set<Poll>().ToArrayAsync());
        Assert.Single(await f.Db.Set<PollOption>().ToArrayAsync());
    }

    [Fact]
    public async Task VerificationFailureRollsBackAllDeletes()
    {
        await using var f = await Fixture.Create();
        // A trigger simulates an unexpected side effect on a global row.
        await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER cleanup_side_effect AFTER DELETE ON Events BEGIN UPDATE People SET DisplayName = 'Unexpected'; END;");
        var preview = await f.Service.ExecuteAsync(f.Request());
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.ExecuteAsync(f.Apply(preview)));
        f.Db.ChangeTracker.Clear();
        Assert.Equal(2, await f.Db.Events.CountAsync());
        Assert.Equal("Admin", (await f.Db.People.SingleAsync()).DisplayName);
        Assert.Single(await f.Db.Set<PollOption>().ToArrayAsync());
    }

    private static async Task<long> Scalar(TRobotDbContext db, string sql)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), "trobot-cleanup-" + Guid.NewGuid() + ".db");
        public required TRobotDbContext Db { get; set; }
        public required Person Person { get; init; }
        public required Event Target { get; init; }
        public required Event Other { get; init; }
        public required OrganizerUser Admin { get; init; }
        public ScenarioCleanupService Service => new(new ScenarioCleanupStore(Db));
        public ScenarioCleanupRequest Request() => new("admin", ScenarioCleanupService.AllowedEventId, 0);
        public ScenarioCleanupRequest Apply(ScenarioCleanupReport preview) => Request() with
        { Apply = true, ExpectedDatabaseFingerprint = preview.DatabaseFingerprint,
            Confirmation = $"DELETE:{ScenarioCleanupService.AllowedEventId:D}", BackupSha256 = new string('A', 64) };
        public static async Task<Fixture> Create()
        {
            var person = new Person("Admin");
            var target = new Event(ScenarioCleanupService.AllowedName, DateTime.UtcNow);
            typeof(Event).GetProperty(nameof(Event.Id))!.SetValue(target, ScenarioCleanupService.AllowedEventId);
            var other = new Event("Evento reale", DateTime.UtcNow);
            var admin = new OrganizerUser { Id = Guid.NewGuid(), UserName = "admin", NormalizedUserName = "ADMIN",
                PersonId = person.Id, IsSystemAdministrator = true };
            var f = new Fixture { Db = null!, Person = person, Target = target, Other = other, Admin = admin };
            var cs = new SqliteConnectionStringBuilder { DataSource = f.path, ForeignKeys = true, Pooling = false }.ToString();
            f.Db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(cs).Options);
            await f.Db.Database.EnsureCreatedAsync();
            f.Db.AddRange(person, target, other, admin,
                new EventOrganizer(target.Id, person.Id, OrganizerRole.Administrator),
                new EventParticipant(target.Id, person.Id), new EventParticipant(other.Id, person.Id),
                new AccountAudit(person.Id, person.Id, "AdministratorInitialized", DateTime.UtcNow),
                new AccountAudit(person.Id, person.Id, "EventGranted", DateTime.UtcNow, target.Id));
            await f.Db.SaveChangesAsync();
            var message = new OutboundMessage(target.Id, Guid.NewGuid(), "Test", MessageKind.Operational, DateTime.UtcNow);
            var poll = new Poll(target.Id, Guid.NewGuid(), "Sondaggio", DateTime.UtcNow.AddDays(1));
            f.Db.AddRange(message, poll); await f.Db.SaveChangesAsync();
            var option = new PollOption(target.Id, poll.Id, 1, "Opzione");
            f.Db.Add(option); await f.Db.SaveChangesAsync();
            poll.Publish(message.Id); poll.Close(DateTime.UtcNow, option.Id); await f.Db.SaveChangesAsync();
            return f;
        }
        public async ValueTask DisposeAsync()
        { await Db.DisposeAsync(); File.Delete(path); }
    }
}
