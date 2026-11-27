using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;

namespace TRobot.Tests;

public sealed class AttendanceQuestionsMigrationTests
{
    private const string PreviousMigration = "20261127072505_MultiEventWhatsApp";
    private static readonly DateTime Now = new(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UpgradeKeepsExistingSettingsAskingForDatesAndAddsEmptyQuestionTables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        var ev = new Event("Evento sintetico", Now);
        db.Add(ev);
        await db.SaveChangesAsync();
        var deadline = Now.AddDays(10);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AttendanceCollectionSettings" ("Id", "EventId", "DeadlineUtc", "Revision")
            VALUES ({ev.Id}, {ev.Id}, {deadline}, {3})
            """);
        db.ChangeTracker.Clear();

        await db.GetService<IMigrator>().MigrateAsync();
        db.ChangeTracker.Clear();

        var settings = await db.Set<AttendanceCollectionSettings>().AsNoTracking().SingleAsync();
        Assert.True(settings.AskStayDates);
        Assert.Equal(1, settings.EventDays);
        Assert.Equal(3, settings.Revision);
        Assert.Equal(deadline, settings.DeadlineUtc);
        foreach (var table in new[] { "AttendanceQuestions", "AttendanceQuestionOptions", "AttendanceAnswers" })
            Assert.Equal(0, await ScalarAsync(connection, $"SELECT COUNT(*) FROM \"{table}\""));
        Assert.Equal(1, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_index_list('AttendanceAnswers') WHERE \"unique\" = 1 AND origin = 'c'"));
        Assert.Equal("ok", await ScalarTextAsync(connection, "PRAGMA integrity_check"));
        Assert.Empty(await db.Database.SqlQueryRaw<string>("SELECT 'x' AS Value FROM pragma_foreign_key_check()").ToListAsync());
    }

    private static async Task<long> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<string> ScalarTextAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
