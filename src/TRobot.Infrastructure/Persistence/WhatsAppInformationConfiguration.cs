using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class WhatsAppListApprovalConfiguration : IEntityTypeConfiguration<WhatsAppListApproval>
{
    public void Configure(EntityTypeBuilder<WhatsAppListApproval> b)
    {
        LocalMapping.Record(b, "WhatsAppListApprovals"); b.Property(x => x.Fingerprint).HasMaxLength(64).IsRequired(); b.Property(x => x.GroupId).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.EventId, x.Kind });
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.ApprovedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.ToTable("WhatsAppListApprovals", t => t.HasCheckConstraint("CK_WhatsAppListApprovals_Kind", "Kind IN (0,1)"));
    }
}
internal sealed class WhatsAppCommandReceiptConfiguration : IEntityTypeConfiguration<WhatsAppCommandReceipt>
{
    public void Configure(EntityTypeBuilder<WhatsAppCommandReceipt> b)
    {
        LocalMapping.Record(b, "WhatsAppCommandReceipts"); b.Property(x => x.MessageHash).HasMaxLength(64).IsRequired(); b.Property(x => x.CommandHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.EventId, x.MessageHash }).IsUnique();
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.AtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
    }
}
