using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;

namespace TRobot.Tests;

public sealed class MultiEventWhatsAppMigrationTests
{
    private const string PreviousMigration = "20261119084028_MessageDelivery";
    private static readonly DateTime Now = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UpgradeBackfillsFeaturesAndCreatesMultiEventIndexes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var eventA = new Event("Evento A", Now);
        var eventB = new Event("Evento B", Now);
        var person = new Person("Organizzatore sintetico");
        db.AddRange(eventA, eventB, person,
            new EventOrganizer(eventA.Id, person.Id, OrganizerRole.Administrator),
            new EventOrganizer(eventB.Id, person.Id, OrganizerRole.Administrator));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "WhatsAppConnections" ("Id", "EventId", "GroupId", "ActorId", "Enabled", "ActivatedAtUtc", "Revision")
            VALUES ({eventA.Id}, {eventA.Id}, {"group-a@g.us"}, {person.Id}, {1}, {Now}, {1})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "WhatsAppConnections" ("Id", "EventId", "GroupId", "ActorId", "Enabled", "ActivatedAtUtc", "Revision")
            VALUES ({eventB.Id}, {eventB.Id}, NULL, NULL, {0}, NULL, {0})
            """);
        db.ChangeTracker.Clear();

        await db.GetService<IMigrator>().MigrateAsync();
        db.ChangeTracker.Clear();

        var rows = await db.Set<WhatsAppConnection>().AsNoTracking().OrderBy(x => x.GroupId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.True(row.FeatureAttendance);
            Assert.True(row.FeatureInformation);
            Assert.True(row.FeatureOperations);
            Assert.True(row.FeatureMealProposals);
            Assert.True(row.FeatureMealPreferences);
            Assert.True(row.FeatureFunReplies);
            Assert.Equal(0, row.FeaturesRevision);
        });

        Assert.Equal(1, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_index_list('WhatsAppConnections') WHERE name = 'IX_WhatsAppConnections_EventId' AND \"unique\" = 1"));
        Assert.Equal(1, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_index_list('WhatsAppConnections') WHERE name = 'IX_WhatsAppConnections_GroupId' AND \"unique\" = 1 AND partial = 1"));
        Assert.Equal(1, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_index_list('WhatsAppConnections') WHERE name = 'IX_WhatsAppConnections_Enabled' AND \"unique\" = 0"));
        Assert.Equal("ok", await ScalarTextAsync(connection, "PRAGMA integrity_check"));
    }

    [Fact]
    public async Task UpgradeAllowsTwoActiveGroupsAndRejectsADuplicateGroup()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.Database.MigrateAsync();

        var eventA = new Event("Evento A", Now);
        var eventB = new Event("Evento B", Now);
        var eventC = new Event("Evento C", Now);
        var person = new Person("Organizzatore sintetico");
        db.AddRange(eventA, eventB, eventC, person);
        await db.SaveChangesAsync();

        var first = new WhatsAppConnection(eventA.Id);
        first.Activate("group-a@g.us", person.Id, Now);
        var second = new WhatsAppConnection(eventB.Id);
        second.Activate("group-b@g.us", person.Id, Now);
        db.AddRange(first, second);
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.Set<WhatsAppConnection>().CountAsync(x => x.Enabled));

        var duplicateGroup = new WhatsAppConnection(eventC.Id);
        duplicateGroup.Activate("group-a@g.us", person.Id, Now);
        db.Add(duplicateGroup);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task DowngradeRestoresTheSingleActiveGroupSchemaWithOneActiveRow()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var ev = new Event("Evento A", Now);
        var person = new Person("Organizzatore sintetico");
        db.AddRange(ev, person, new EventOrganizer(ev.Id, person.Id, OrganizerRole.Administrator));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "WhatsAppConnections" ("Id", "EventId", "GroupId", "ActorId", "Enabled", "ActivatedAtUtc", "Revision")
            VALUES ({ev.Id}, {ev.Id}, {"group-a@g.us"}, {person.Id}, {1}, {Now}, {1})
            """);

        await db.GetService<IMigrator>().MigrateAsync();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        Assert.Equal(0, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_table_info('WhatsAppConnections') WHERE name = 'FeatureAttendance'"));
        Assert.Equal(1, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_index_list('WhatsAppConnections') WHERE name = 'IX_WhatsAppConnections_Enabled' AND \"unique\" = 1 AND partial = 1"));
        Assert.Equal(1, await ScalarAsync(connection, "SELECT COUNT(*) FROM \"WhatsAppConnections\" WHERE \"Enabled\" = 1"));
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string?> ScalarTextAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync());
    }
}
