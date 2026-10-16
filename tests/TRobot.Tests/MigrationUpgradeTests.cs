using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;

namespace TRobot.Tests;

public sealed class MigrationUpgradeTests
{
    [Fact]
    public async Task AccessLifecycleUpgradeKeepsExistingAccountsEnabledAndMembershipsActive()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261016094949_OrganizerIdentity");
        var ev = new Event("Evento pre-revoche", DateTime.UtcNow);
        var person = new Person("Organizzatore pre-revoche");
        await InsertLegacyEvent(db, ev);
        await InsertLegacyPerson(db, person);
        db.Add(new EventParticipant(ev.Id, person.Id));
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO EventOrganizers (EventId, PersonId, Role, CreatedAtUtc) VALUES ({ev.Id}, {person.Id}, 'Administrator', {now})");
        var userId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AspNetUsers (Id, PersonId, IsSystemAdministrator, UserName, NormalizedUserName, PasswordHash, SecurityStamp, ConcurrencyStamp, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount) VALUES ({userId}, {person.Id}, 1, 'upgrade-admin', 'UPGRADE-ADMIN', 'synthetic-hash-to-preserve', 'synthetic-stamp', 'synthetic-concurrency', 0, 0, 0, 1, 0)");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AccountAudits (Id, PersonId, ActorId, Action, OccurredAtUtc) VALUES ({Guid.NewGuid()}, {person.Id}, NULL, 'AdministratorInitialized', {now})");
        await InsertLegacyReceipt(db, new ParticipationReceipt(ev.Id, person.Id, person.Id, "before-lifecycle", ParticipationStatus.NoResponse, ParticipationStatus.NoResponse, now));
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var user = await db.Users.SingleAsync();
        Assert.True(user.IsEnabled);
        Assert.True(user.IsSystemAdministrator);
        Assert.Equal("synthetic-hash-to-preserve", user.PasswordHash);
        Assert.Equal("synthetic-stamp", user.SecurityStamp);
        var assignment = await db.EventOrganizers.SingleAsync();
        Assert.True(assignment.IsActive);
        Assert.Equal(OrganizerRole.Administrator, assignment.Role);
        Assert.Null((await db.AccountAudits.SingleAsync()).EventId);
        Assert.Equal("before-lifecycle", (await db.ParticipationReceipts.SingleAsync()).MessageId);
    }

    [Fact]
    public async Task IdentityUpgradePreservesExistingStatusAndMessageHistory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261016083054_AttendanceSimulation");
        var ev = new Event("Evento precedente a Identity", DateTime.UtcNow);
        var organizer = new Person("Organizzatore precedente");
        var sender = new Person("Partecipante precedente", "synthetic-upgrade");
        var participant = new EventParticipant(ev.Id, sender.Id);
        participant.ChangeStatus(ParticipationStatus.Undecided, DateTime.UtcNow);
        await InsertLegacyEvent(db, ev);
        await InsertLegacyPerson(db, organizer);
        await InsertLegacyPerson(db, sender);
        db.Add(participant);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO EventOrganizers (EventId, PersonId, Role, CreatedAtUtc) VALUES ({ev.Id}, {organizer.Id}, 'Administrator', {DateTime.UtcNow})");
        await InsertLegacyReceipt(db, new ParticipationReceipt(ev.Id, sender.Id, organizer.Id, "existing-message", ParticipationStatus.NoResponse, ParticipationStatus.Undecided, DateTime.UtcNow));
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(ParticipationStatus.Undecided, (await db.EventParticipants.SingleAsync()).Status);
        Assert.Equal("existing-message", (await db.ParticipationReceipts.SingleAsync()).MessageId);
        Assert.Equal(organizer.Id, (await db.ParticipationReceipts.SingleAsync()).OrganizerPersonId);
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.AccountAudits.ToListAsync());
    }

    [Fact]
    public async Task UpgradePreservesExistingMilestoneOneParticipation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261016074445_InitialCreate");
        var ev = new Event("Evento preesistente test", DateTime.UtcNow);
        var person = new Person("Persona preesistente test");
        await InsertLegacyEvent(db, ev);
        await InsertLegacyPerson(db, person);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO EventParticipants (EventId, PersonId, Status, CreatedAtUtc) VALUES ({ev.Id}, {person.Id}, 'NoResponse', {DateTime.UtcNow})");

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var participant = await db.EventParticipants.SingleAsync();
        Assert.Equal(ev.Id, participant.EventId);
        Assert.Equal(person.Id, participant.PersonId);
        Assert.Equal(ParticipationStatus.NoResponse, participant.Status);
        Assert.Null(participant.UpdatedAtUtc);
        Assert.Empty(await db.ParticipationReceipts.ToListAsync());
    }
    private static Task<int> InsertLegacyEvent(TRobotDbContext db, Event ev) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Events (Id, Name, StartsAtUtc, TimeZoneId, CreatedAtUtc) VALUES ({ev.Id}, {ev.Name}, {ev.StartsAtUtc}, {ev.TimeZoneId}, {ev.CreatedAtUtc})");

    internal static Task<int> InsertLegacyPerson(TRobotDbContext db, Person person) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO People (Id, DisplayName, WhatsAppSenderId, CreatedAtUtc) VALUES ({person.Id}, {person.DisplayName}, {person.WhatsAppSenderId}, {person.CreatedAtUtc})");

    internal static Task<int> InsertLegacyReceipt(TRobotDbContext db, ParticipationReceipt receipt) =>
        db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO ParticipationReceipts (Id, EventId, PersonId, OrganizerPersonId, MessageId, PreviousStatus, Status, ProcessedAtUtc) VALUES ({receipt.Id}, {receipt.EventId}, {receipt.PersonId}, {receipt.OrganizerPersonId}, {receipt.MessageId}, {receipt.PreviousStatus.ToString()}, {receipt.Status.ToString()}, {receipt.ProcessedAtUtc})");
}
