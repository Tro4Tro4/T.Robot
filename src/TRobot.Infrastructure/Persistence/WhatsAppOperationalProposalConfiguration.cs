using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class WhatsAppOperationalProposalConfiguration : IEntityTypeConfiguration<WhatsAppOperationalProposal>
{
    public void Configure(EntityTypeBuilder<WhatsAppOperationalProposal> b)
    {
        LocalMapping.Record(b, "WhatsAppOperationalProposals");
        b.Property(x => x.CodeHash).HasMaxLength(64).IsRequired();
        b.Property(x => x.GroupId).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.EventId, x.CodeHash }).IsUnique();
        b.HasIndex(x => new { x.EventId, x.PersonId, x.ExpiresAtUtc });
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.CreatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.Property(x => x.ExpiresAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.Property(x => x.ConsumedAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : null);
        b.ToTable("WhatsAppOperationalProposals", t => t.HasCheckConstraint("CK_WhatsAppOperationalProposals_Action", "Action IN (0,1,2)"));
    }
}
