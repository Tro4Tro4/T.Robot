using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class HouseholdConfiguration : IEntityTypeConfiguration<Household>
{
    public void Configure(EntityTypeBuilder<Household> b)
    {
        LocalMapping.Record(b, "Households"); b.HasAlternateKey(x => new { x.EventId, x.Id });
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
    }
}
internal sealed class HouseholdMemberConfiguration : IEntityTypeConfiguration<HouseholdMember>
{
    public void Configure(EntityTypeBuilder<HouseholdMember> b)
    {
        LocalMapping.Record(b, "HouseholdMembers");
        b.HasAlternateKey(x => new { x.EventId, x.HouseholdId, x.Id });
        b.HasIndex(x => new { x.EventId, x.PersonId }).IsUnique().HasFilter("\"IsActive\" = 1");
        b.HasOne<Household>().WithMany().HasForeignKey(x => new { x.EventId, x.HouseholdId })
            .HasPrincipalKey(x => new { x.EventId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<HouseholdMember>().WithMany().HasForeignKey(x => new { x.EventId, x.HouseholdId, x.CompanionOfMemberId })
            .HasPrincipalKey(x => new { x.EventId, x.HouseholdId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("HouseholdMembers", t =>
        {
            t.HasCheckConstraint("CK_HouseholdMembers_Kind", "Kind IN (0,1)");
            t.HasCheckConstraint("CK_HouseholdMembers_NotSelf", "CompanionOfMemberId IS NULL OR CompanionOfMemberId <> Id");
        });
    }
}
