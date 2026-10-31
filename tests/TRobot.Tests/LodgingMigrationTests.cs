using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TRobot.Domain;
using TRobot.Infrastructure.Identity;
using TRobot.Infrastructure.Persistence;

namespace TRobot.Tests;

public sealed class LodgingMigrationTests
{
    [Fact]
    public async Task UpgradeAddsOnlyEmptyLodgingTablesAndPreservesEveryPreviousRow()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261019073019_ExtendAttendanceCollection");
        var ev = new Event("Evento prima delle camere", DateTime.UtcNow);
        var person = new Person("Partecipante precedente", "synthetic-lodging-migration");
        var participant = new EventParticipant(ev.Id, person.Id);
        participant.ChangeStatus(ParticipationStatus.Confirmed, DateTime.UtcNow);
        db.AddRange(ev, person, participant, new EventOrganizer(ev.Id, person.Id),
            new OrganizerUser
            {
                Id = Guid.NewGuid(), PersonId = person.Id, UserName = "synthetic-migration",
                PasswordHash = "synthetic-hash-preserved", SecurityStamp = "synthetic-security-stamp",
                IsEnabled = true, IsSystemAdministrator = true
            },
            new ParticipationReceipt(ev.Id, person.Id, person.Id, "synthetic-previous-receipt",
                ParticipationStatus.NoResponse, ParticipationStatus.Confirmed, DateTime.UtcNow));
        await db.SaveChangesAsync();
        var before = await Rows(connection);

        await db.GetService<IMigrator>().MigrateAsync("20261023072906_AddLodgingPlanning");
        db.ChangeTracker.Clear();
        var after = await Rows(connection);
        Assert.Equal(before.Count + 4, after.Count);
        foreach (var table in before) Assert.Equal(table.Value, after[table.Key]);
        foreach (var table in new[] { "LodgingPlans", "LodgingApartments", "LodgingRooms", "LodgingAssignments" })
            Assert.Empty(after[table]);
        await using var integrity = connection.CreateCommand();
        integrity.CommandText = "PRAGMA integrity_check";
        Assert.Equal("ok", await integrity.ExecuteScalarAsync());
        integrity.CommandText = "PRAGMA foreign_key_check";
        await using var reader = await integrity.ExecuteReaderAsync();
        Assert.False(await reader.ReadAsync());
    }

    private static async Task<Dictionary<string, string[]>> Rows(SqliteConnection connection)
    {
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory' ORDER BY name";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }
        var result = new Dictionary<string, string[]>();
        foreach (var table in tables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\"";
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<string>();
            while (await reader.ReadAsync())
            {
                var values = Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null
                    : reader.GetValue(i) is byte[] bytes ? Convert.ToHexString(bytes)
                    : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)).ToArray();
                rows.Add(JsonSerializer.Serialize(values));
            }
            result.Add(table, rows.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }
        return result;
    }
}
