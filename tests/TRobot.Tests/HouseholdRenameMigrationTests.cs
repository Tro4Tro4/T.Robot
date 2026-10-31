using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;

namespace TRobot.Tests;

public sealed class HouseholdRenameMigrationTests
{
    [Fact]
    public async Task UpgradePreservesAllPreviousColumnsAndLeavesLegacyFingerprintNull()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261023072906_AddLodgingPlanning");
        var ev = new Event("Evento prima della rinomina nuclei", DateTime.UtcNow);
        var adult = new Person("Adulto precedente");
        var child = new Person("Bambino precedente");
        db.AddRange(ev, adult, child, new EventParticipant(ev.Id, adult.Id), new EventParticipant(ev.Id, child.Id));
        await db.SaveChangesAsync();
        var householdId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Households (Id, EventId, Name, Revision) VALUES ({householdId}, {ev.Id}, {"Nucleo precedente"}, {3})");
        var adultMember = new HouseholdMember(ev.Id, householdId, adult.Id, HouseholdMemberKind.Adult, null, Guid.NewGuid());
        var apartment = new LodgingApartment(Guid.NewGuid(), ev.Id, "Appartamento precedente", "Indicazioni precedenti");
        var room = new LodgingRoom(Guid.NewGuid(), ev.Id, apartment.Id, "Camera precedente", 2, "Note precedenti");
        var plan = new LodgingPlan(ev.Id);
        plan.FinalizePlan(new string('A', 64));
        db.AddRange(adultMember, new HouseholdMember(ev.Id, householdId, child.Id, HouseholdMemberKind.Child, adultMember.Id, Guid.NewGuid()),
            apartment, room, plan, new LodgingAssignment(ev.Id, adult.Id, room.Id), new LodgingAssignment(ev.Id, child.Id, room.Id));
        await db.SaveChangesAsync();
        var before = await ReadPreviousColumns(connection);

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var after = await ReadPreviousColumns(connection, before);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var table in before) Assert.Equal(table.Value.Rows, after[table.Key].Rows);
        var household = await db.Set<Household>().SingleAsync();
        Assert.Equal(householdId, household.Id);
        Assert.Equal("Nucleo precedente", household.Name);
        Assert.Equal(3, household.Revision);
        Assert.Null(household.CreationNameFingerprint);
        Assert.Equal(new string('A', 64), (await db.Set<LodgingPlan>().SingleAsync()).FinalizedFingerprint);
        await using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA integrity_check";
        Assert.Equal("ok", await check.ExecuteScalarAsync());
        check.CommandText = "PRAGMA foreign_key_check";
        await using var reader = await check.ExecuteReaderAsync();
        Assert.False(await reader.ReadAsync());
    }

    private sealed record TableState(string[] Columns, string[] Rows);
    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    private static async Task<Dictionary<string, TableState>> ReadPreviousColumns(SqliteConnection connection,
        Dictionary<string, TableState>? previous = null)
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
