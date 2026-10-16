using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class TaskConfiguration : IEntityTypeConfiguration<OrganizationTask>
{
    public void Configure(EntityTypeBuilder<OrganizationTask> b)
    {
        LocalMapping.Record(b, "OrganizationTasks");
        b.ToTable("OrganizationTasks", t =>
        {
            t.HasCheckConstraint("CK_OrganizationTasks_Priority", "Priority IN (0,1,2)");
            t.HasCheckConstraint("CK_OrganizationTasks_Status", "Status IN (0,1,2,3)");
        });
        b.Property(x => x.Title).HasMaxLength(160).IsRequired();
        b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.DueAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.Restrict);
    }
}
