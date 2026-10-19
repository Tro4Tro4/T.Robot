using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class AttendanceCollectionSettingsConfiguration : IEntityTypeConfiguration<AttendanceCollectionSettings>
{
    public void Configure(EntityTypeBuilder<AttendanceCollectionSettings> b)
    {
        LocalMapping.Record(b, "AttendanceCollectionSettings"); b.HasIndex(x => x.EventId).IsUnique();
        b.ToTable("AttendanceCollectionSettings", t => t.HasCheckConstraint("CK_AttendanceCollectionSettings_Event", "Id = EventId"));
        b.Property(x => x.DeadlineUtc).HasConversion(x => x,
            x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
    }
}
