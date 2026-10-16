using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;

namespace TRobot.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly TRobotDbContext db;

    public PersistenceTests()
    {
        connection.Open();
        db = new(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        db.Database.Migrate();
    }

    private (Event Event, Person Person) Seed()
    {
        var ev = new Event("Evento test", new DateTime(2026, 12, 31, 19, 0, 0, DateTimeKind.Utc));
        var person = new Person("Persona test");
        db.AddRange(ev, person);
        db.SaveChanges();
        return (ev, person);
    }

    [Fact]
    public void MigrationIsRepeatableAndRoundTripsAllEntities()
    {
        db.Database.Migrate();
        var (ev, person) = Seed();
        db.AddRange(new EventParticipant(ev.Id, person.Id), new EventOrganizer(ev.Id, person.Id, OrganizerRole.Administrator));
        db.SaveChanges();
        db.ChangeTracker.Clear();
        Assert.Equal(8, db.Database.GetAppliedMigrations().Count());
        var loaded = db.Events.Single();
        Assert.Equal(ev.StartsAtUtc, loaded.StartsAtUtc);
        Assert.Equal(DateTimeKind.Utc, loaded.StartsAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAtUtc.Kind);
        Assert.Equal("Europe/Rome", loaded.TimeZoneId);
        Assert.Null(db.People.Single().WhatsAppSenderId);
        Assert.Equal(ParticipationStatus.NoResponse, db.EventParticipants.Single().Status);
        Assert.Equal(OrganizerRole.Administrator, db.EventOrganizers.Single().Role);
    }

    [Fact]
    public void MultiplePeopleCanHaveNoWhatsAppIdentity()
    {
        db.People.AddRange(new Person("Uno"), new Person("Due"));
        db.SaveChanges();
        Assert.Equal(2, db.People.Count());
    }

    [Fact]
    public void WhatsAppIdentityIsUnique()
    {
        db.People.AddRange(new Person("Uno", "synthetic-id"), new Person("Due", "synthetic-id"));
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Theory]
    [InlineData("EventParticipants")]
    [InlineData("EventOrganizers")]
    public void MembershipCannotBeDuplicated(string table)
    {
        var (ev, person) = Seed();
        if (table == "EventParticipants") db.Add(new EventParticipant(ev.Id, person.Id));
        else db.Add(new EventOrganizer(ev.Id, person.Id));
        db.SaveChanges();
        // Copia SQL per verificare il vincolo DB, oltre alla identity map EF.
        var sql = table == "EventParticipants"
            ? "INSERT INTO EventParticipants SELECT * FROM EventParticipants"
            : "INSERT INTO EventOrganizers SELECT * FROM EventOrganizers";
        Assert.Throws<SqliteException>(() => db.Database.ExecuteSqlRaw(sql));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingReferencesAreRejected(bool organizer)
    {
        if (organizer) db.Add(new EventOrganizer(Guid.NewGuid(), Guid.NewGuid()));
        else db.Add(new EventParticipant(Guid.NewGuid(), Guid.NewGuid()));
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferencedPeopleCannotBeDeleted(bool organizer)
    {
        var (ev, person) = Seed();
        if (organizer) db.Add(new EventOrganizer(ev.Id, person.Id));
        else db.Add(new EventParticipant(ev.Id, person.Id));
        db.SaveChanges();
        db.ChangeTracker.Clear();
        db.People.Remove(db.People.Single());
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidStatusOrRoleIsRejectedByDatabase(bool organizer)
    {
        var (ev, person) = Seed();
        if (organizer) db.Add(new EventOrganizer(ev.Id, person.Id));
        else db.Add(new EventParticipant(ev.Id, person.Id));
        db.SaveChanges();
        var sql = organizer ? "UPDATE EventOrganizers SET Role = 'Invalid'"
            : "UPDATE EventParticipants SET Status = 'Invalid'";
        Assert.Throws<SqliteException>(() => db.Database.ExecuteSqlRaw(sql));
    }

    public void Dispose()
    {
        db.Dispose();
        connection.Dispose();
    }
}
