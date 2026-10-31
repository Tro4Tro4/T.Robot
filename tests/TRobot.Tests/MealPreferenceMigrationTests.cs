using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TRobot.Domain;
using TRobot.Infrastructure.Identity;
using TRobot.Infrastructure.Persistence;

namespace TRobot.Tests;

public sealed class MealPreferenceMigrationTests
{
    [Fact]
    public async Task PreferencesUpgradePreservesEveryExistingRowIncludingProposalAndPrivateSession()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True"); await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261031133619_AddMealProposals");
        var ev = new Event("Evento sintetico upgrade preferenze", DateTime.UtcNow);
        var person = new Person("Partecipante sintetico upgrade");
        var session = new MealProposalSession(ev.Id, person.Id);
        session.Start(person.Id, "synthetic-upgrade@g.us", 1, new string('a', 64), DateTime.UtcNow);
        db.AddRange(ev, person, new EventParticipant(ev.Id, person.Id), new EventOrganizer(ev.Id, person.Id), session,
            new MealProposal(ev.Id, person.Id, new(2030, 12, 31), MealKind.Dinner, "Piatto conservato", true, DateTime.UtcNow),
            new OrganizerUser { Id = Guid.NewGuid(), PersonId = person.Id, UserName = "synthetic-upgrade",
                PasswordHash = "synthetic-hash", SecurityStamp = "synthetic-stamp", IsEnabled = true });
        await db.SaveChangesAsync(); var before = await MealProposalMigrationTests.Rows(connection);
        await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
        var after = await MealProposalMigrationTests.Rows(connection);
        Assert.Equal(before.Count + 6, after.Count);
        foreach (var table in before) Assert.Equal(table.Value, after[table.Key]);
        foreach (var table in new[] { "MealVotingCycles", "MealVotingEntries", "MealVotingSources", "MealPreferenceBallots", "MealPreferenceChanges", "MealPreferenceSessions" })
            Assert.Empty(after[table]);
        await using var command = connection.CreateCommand(); command.CommandText = "PRAGMA integrity_check";
        Assert.Equal("ok", await command.ExecuteScalarAsync()); command.CommandText = "PRAGMA foreign_key_check";
        await using var reader = await command.ExecuteReaderAsync(); Assert.False(await reader.ReadAsync());
    }
}
