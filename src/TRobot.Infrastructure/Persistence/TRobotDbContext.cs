using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using TRobot.Domain;
using TRobot.Infrastructure.Identity;

namespace TRobot.Infrastructure.Persistence;

public sealed class TRobotDbContext(DbContextOptions<TRobotDbContext> options) : IdentityUserContext<OrganizerUser, Guid>(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<EventParticipant> EventParticipants => Set<EventParticipant>();
    public DbSet<EventOrganizer> EventOrganizers => Set<EventOrganizer>();
    public DbSet<ParticipationReceipt> ParticipationReceipts => Set<ParticipationReceipt>();
    public DbSet<AccountAudit> AccountAudits => Set<AccountAudit>();
    public DbSet<EventAudit> EventAudits => Set<EventAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TRobotDbContext).Assembly);
    }
}
