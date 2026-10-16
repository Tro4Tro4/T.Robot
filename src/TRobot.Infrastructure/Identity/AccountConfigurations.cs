using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Identity;

internal sealed class OrganizerUserConfiguration : IEntityTypeConfiguration<OrganizerUser>
{
    public void Configure(EntityTypeBuilder<OrganizerUser> builder)
    {
        builder.HasIndex(x => x.PersonId).IsUnique();
        builder.Property(x => x.IsEnabled).HasDefaultValue(true).HasSentinel(true);
        builder.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class AccountAuditConfiguration : IEntityTypeConfiguration<AccountAudit>
{
    public void Configure(EntityTypeBuilder<AccountAudit> builder)
    {
        builder.ToTable("AccountAudits", table =>
        {
            table.HasCheckConstraint("CK_AccountAudits_Action",
                "Action IN ('AdministratorInitialized', 'OrganizerCreated', 'PasswordChanged', 'EventGranted', 'EventRevoked', 'AccountEnabled', 'AccountDisabled')");
            table.HasCheckConstraint("CK_AccountAudits_Event",
                "Action NOT IN ('EventGranted', 'EventRevoked') OR EventId IS NOT NULL");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.OccurredAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        builder.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}
