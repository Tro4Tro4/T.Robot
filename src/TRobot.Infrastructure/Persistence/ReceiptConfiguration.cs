using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<ParticipationReceipt>
{
    public void Configure(EntityTypeBuilder<ParticipationReceipt> builder)
    {
        builder.ToTable("ParticipationReceipts", table =>
        {
            table.HasCheckConstraint("CK_ParticipationReceipts_MessageId", "length(trim(MessageId)) BETWEEN 1 AND 100");
            table.HasCheckConstraint("CK_ParticipationReceipts_Status", "Status IN ('NoResponse', 'Confirmed', 'Undecided', 'Absent')");
            table.HasCheckConstraint("CK_ParticipationReceipts_PreviousStatus", "PreviousStatus IN ('NoResponse', 'Confirmed', 'Undecided', 'Absent')");
            table.HasCheckConstraint("CK_ParticipationReceipts_Origin", "Origin IN ('Simulation', 'Organizer')");
        });
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.EventId, x.MessageId }).IsUnique();
        builder.Property(x => x.MessageId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PreviousStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Origin).HasConversion<string>().HasMaxLength(20).HasDefaultValue(AttendanceOrigin.Simulation);
        builder.Property(x => x.ProcessedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        builder.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EventOrganizer>().WithMany().HasForeignKey(x => new { x.EventId, PersonId = x.OrganizerPersonId }).OnDelete(DeleteBehavior.Restrict);
    }
}
