using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class LodgingPlanConfiguration : IEntityTypeConfiguration<LodgingPlan>
{
    public void Configure(EntityTypeBuilder<LodgingPlan> b)
    {
        LocalMapping.Record(b, "LodgingPlans"); b.HasIndex(x => x.EventId).IsUnique();
        b.Property(x => x.FinalizedFingerprint).HasMaxLength(64);
    }
}
internal sealed class LodgingApartmentConfiguration : IEntityTypeConfiguration<LodgingApartment>
{
    public void Configure(EntityTypeBuilder<LodgingApartment> b)
    {
        LocalMapping.Record(b, "LodgingApartments"); b.HasAlternateKey(x => new { x.EventId, x.Id });
        b.Property(x => x.Name).HasMaxLength(150).IsRequired(); b.Property(x => x.Directions).HasMaxLength(1000);
    }
}
internal sealed class LodgingRoomConfiguration : IEntityTypeConfiguration<LodgingRoom>
{
    public void Configure(EntityTypeBuilder<LodgingRoom> b)
    {
        LocalMapping.Record(b, "LodgingRooms"); b.HasAlternateKey(x => new { x.EventId, x.Id });
        b.HasOne<LodgingApartment>().WithMany().HasForeignKey(x => new { x.EventId, x.ApartmentId })
            .HasPrincipalKey(x => new { x.EventId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Name).HasMaxLength(150).IsRequired(); b.Property(x => x.Notes).HasMaxLength(1000);
        b.ToTable("LodgingRooms", t => t.HasCheckConstraint("CK_LodgingRooms_Capacity", "Capacity BETWEEN 1 AND 1000"));
    }
}
internal sealed class LodgingAssignmentConfiguration : IEntityTypeConfiguration<LodgingAssignment>
{
    public void Configure(EntityTypeBuilder<LodgingAssignment> b)
    {
        LocalMapping.Record(b, "LodgingAssignments"); b.HasIndex(x => new { x.EventId, x.PersonId }).IsUnique();
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<LodgingRoom>().WithMany().HasForeignKey(x => new { x.EventId, x.RoomId })
            .HasPrincipalKey(x => new { x.EventId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
