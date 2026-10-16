using Microsoft.EntityFrameworkCore;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

public sealed class TRobotDbContext(DbContextOptions<TRobotDbContext> options) : DbContext(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<EventParticipant> EventParticipants => Set<EventParticipant>();
    public DbSet<EventOrganizer> EventOrganizers => Set<EventOrganizer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TRobotDbContext).Assembly);
}
