using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class AttendanceStayConfiguration : IEntityTypeConfiguration<AttendanceStay>
{
    public void Configure(EntityTypeBuilder<AttendanceStay> b)
    {
        LocalMapping.Record(b, "AttendanceStays"); b.HasKey(x => new { x.EventId, x.Id }); b.Ignore(x => x.LastDay); b.Ignore(x => x.Nights);
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("AttendanceStays", t => t.HasCheckConstraint("CK_AttendanceStays_Days", "Days BETWEEN 1 AND 31"));
    }
}
internal sealed class AttendanceDialogueConfiguration : IEntityTypeConfiguration<AttendanceDialogue>
{
    public void Configure(EntityTypeBuilder<AttendanceDialogue> b)
    {
        LocalMapping.Record(b, "AttendanceDialogues");
        b.HasKey(x => new { x.EventId, x.Id });
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.GroupId).HasMaxLength(200).IsRequired(); b.Property(x => x.SnapshotHash).HasMaxLength(64);
        b.Property(x => x.StartedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.Property(x => x.ExpiresAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
    }
}
internal sealed class AttendanceStayChangeConfiguration : IEntityTypeConfiguration<AttendanceStayChange>
{
    public void Configure(EntityTypeBuilder<AttendanceStayChange> b)
    {
        LocalMapping.Record(b, "AttendanceStayChanges");
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.AtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
    }
}
