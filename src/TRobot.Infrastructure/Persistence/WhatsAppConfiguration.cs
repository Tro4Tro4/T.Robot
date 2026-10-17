using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class WhatsAppConnectionConfiguration : IEntityTypeConfiguration<WhatsAppConnection>
{
    public void Configure(EntityTypeBuilder<WhatsAppConnection> b)
    {
        LocalMapping.Record(b, "WhatsAppConnections");
        b.Property(x => x.GroupId).HasMaxLength(200);
        b.HasIndex(x => x.Enabled).IsUnique().HasFilter("Enabled = 1");
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.ActivatedAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
        b.ToTable("WhatsAppConnections", t => t.HasCheckConstraint("CK_WhatsAppConnections_Active", "Enabled = 0 OR (GroupId IS NOT NULL AND ActorId IS NOT NULL AND ActivatedAtUtc IS NOT NULL)"));
    }
}
internal sealed class WhatsAppIdentityAliasConfiguration : IEntityTypeConfiguration<WhatsAppIdentityAlias>
{
    public void Configure(EntityTypeBuilder<WhatsAppIdentityAlias> b)
    {
        LocalMapping.Record(b, "WhatsAppIdentityAliases"); b.Property(x => x.SenderHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.EventId, x.SenderHash }).IsUnique();
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class WhatsAppIncomingReceiptConfiguration : IEntityTypeConfiguration<WhatsAppIncomingReceipt>
{
    public void Configure(EntityTypeBuilder<WhatsAppIncomingReceipt> b)
    {
        LocalMapping.Record(b, "WhatsAppIncomingReceipts"); b.Property(x => x.MessageHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => new { x.EventId, x.MessageHash }).IsUnique();
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.AtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
    }
}
internal sealed class WhatsAppLiveMessageConfiguration : IEntityTypeConfiguration<WhatsAppLiveMessage>
{
    public void Configure(EntityTypeBuilder<WhatsAppLiveMessage> b)
    {
        LocalMapping.Record(b, "WhatsAppLiveMessages"); b.Property(x => x.Body).HasMaxLength(1000).IsRequired(); b.Property(x => x.GroupId).HasMaxLength(200).IsRequired();
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.CreatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.Property(x => x.OutcomeAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
        b.ToTable("WhatsAppLiveMessages", t => t.HasCheckConstraint("CK_WhatsAppLiveMessages_Status", "Status IN (0,1,2,3)"));
    }
}
