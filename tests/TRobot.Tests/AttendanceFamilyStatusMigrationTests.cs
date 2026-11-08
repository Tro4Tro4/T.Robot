using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;

namespace TRobot.Tests;

public sealed class AttendanceFamilyStatusMigrationTests
{
    [Fact]
    public async Task UpgradePreservesDialoguesStaysAndReceiptsWithNullFamilyStatus()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True"); await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261031205555_AddMealPreferences");
        var ev = new Event("Evento sintetico upgrade presenze famiglia", DateTime.UtcNow);
        var person = new Person("Partecipante sintetico upgrade");
        var participant = new EventParticipant(ev.Id, person.Id); participant.ChangeStatus(ParticipationStatus.Confirmed, DateTime.UtcNow);
        var stay = new AttendanceStay(ev.Id, person.Id); stay.Set(new(2030, 12, 30), 4);
        db.AddRange(ev, person, participant, stay,
            new AttendanceStayChange(ev.Id, person.Id, person.Id, person.Id, null, null, ParticipationStatus.NoResponse, new(2030, 12, 30), 4, DateTime.UtcNow),
            new WhatsAppIncomingReceipt(ev.Id, new string('b', 64), person.Id, person.Id, ParticipationStatus.NoResponse, ParticipationStatus.Confirmed, DateTime.UtcNow));
        await db.SaveChangesAsync();
        // The previous schema has no FamilyStatus column: write the open dialogue with raw SQL.
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO AttendanceDialogues (EventId, Id, ActorId, Arrival, ConnectionRevision, Days, ExpiresAtUtc, GroupId,
            HouseholdId, InitialSnapshotHash, IsEdit, PreserveDays, Revision, SnapshotHash, StartedAtUtc, Step)
            VALUES ({ev.Id}, {person.Id}, {person.Id}, {"2030-12-30"}, {2}, {4}, {"2030-12-30 11:15:00"}, {"synthetic-upgrade@g.us"},
            {null}, {null}, {false}, {false}, {3}, {new string('c', 64)}, {"2030-12-30 11:00:00"}, {(int)AttendanceDialogueStep.Confirmation})");
        var before = await Rows(connection);

        await db.GetService<IMigrator>().MigrateAsync("AddAttendanceFamilyStatus");
        db.ChangeTracker.Clear();
        var after = await Rows(connection, before);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var table in before) Assert.Equal(table.Value.Rows, after[table.Key].Rows);
        var dialogue = await db.Set<AttendanceDialogue>().SingleAsync();
        Assert.Equal(AttendanceDialogueStep.Confirmation, dialogue.Step); Assert.Null(dialogue.FamilyStatus);
        Assert.Equal(new DateOnly(2030, 12, 30), dialogue.Arrival); Assert.Equal(4, dialogue.Days); Assert.Equal(3, dialogue.Revision);
        Assert.Single(await db.Set<AttendanceStay>().ToListAsync()); Assert.Single(await db.Set<AttendanceStayChange>().ToListAsync());
        Assert.Single(await db.Set<WhatsAppIncomingReceipt>().ToListAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        await using var check = connection.CreateCommand(); check.CommandText = "PRAGMA integrity_check";
        Assert.Equal("ok", await check.ExecuteScalarAsync()); check.CommandText = "PRAGMA foreign_key_check";
        await using var reader = await check.ExecuteReaderAsync(); Assert.False(await reader.ReadAsync());
    }

    private sealed record TableState(string[] Columns, string[] Rows);
    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    // Reads every table restricted to the columns that existed before the upgrade.
    private static async Task<Dictionary<string, TableState>> Rows(SqliteConnection connection, Dictionary<string, TableState>? previous = null)
    {
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory' ORDER BY name";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        }
        var result = new Dictionary<string, TableState>();
        foreach (var table in tables)
        {
            var columns = new List<string>();
            if (previous is not null) columns.AddRange(previous[table].Columns);
            else
            {
                await using var schema = connection.CreateCommand();
                schema.CommandText = "PRAGMA table_info(" + Quote(table) + ")";
                await using var reader = await schema.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
            }
            var rows = new List<string>();
            await using var query = connection.CreateCommand();
            query.CommandText = "SELECT " + string.Join(", ", columns.Select(Quote)) + " FROM " + Quote(table);
            await using (var reader = await query.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    rows.Add(JsonSerializer.Serialize(Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? null
                        : reader.GetValue(i) is byte[] bytes ? Convert.ToHexString(bytes)
                        : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture)).ToArray()));
            result.Add(table, new(columns.ToArray(), rows.OrderBy(x => x, StringComparer.Ordinal).ToArray()));
        }
        return result;
    }
}
