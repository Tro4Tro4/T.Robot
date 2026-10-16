using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<OutboundMessage>
{
    public void Configure(EntityTypeBuilder<OutboundMessage> b)
    {
        LocalMapping.Record(b, "OutboundMessages");
        b.ToTable("OutboundMessages", t =>
        {
            t.HasCheckConstraint("CK_OutboundMessages_Kind", "Kind IN (0,1,2)");
            t.HasCheckConstraint("CK_OutboundMessages_Status", "Status IN (0,1,2,3)");
            t.HasCheckConstraint("CK_OutboundMessages_Outcome", "(Status = 2 AND SentAtUtc IS NOT NULL) OR (Status <> 2 AND SentAtUtc IS NULL)");
        });
        b.Property(x => x.Body).IsRequired().HasMaxLength(4000);
        b.Property(x => x.SummaryFingerprint).HasMaxLength(200);
        b.Property(x => x.ScheduledAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.Property(x => x.SentAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
    }
}
internal sealed class BotSettingsConfiguration : IEntityTypeConfiguration<BotSettings>
{
    public void Configure(EntityTypeBuilder<BotSettings> b)
    {
        LocalMapping.Record(b, "BotSettings"); b.HasIndex(x => x.EventId).IsUnique();
        b.ToTable("BotSettings", t => t.HasCheckConstraint("CK_BotSettings_Ranges",
            "QuietStartHour BETWEEN 0 AND 23 AND QuietEndHour BETWEEN 0 AND 23 AND MaxSpontaneousPerDay BETWEEN 0 AND 3 AND Sarcasm BETWEEN 0 AND 10"));
        b.Property(x => x.LastSummaryFingerprint).HasMaxLength(200);
        b.Property(x => x.LastSummaryAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
    }
}
