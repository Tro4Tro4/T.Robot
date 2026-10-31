using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class MealPreferenceSessionConfiguration : IEntityTypeConfiguration<MealPreferenceSession>
{
    public void Configure(EntityTypeBuilder<MealPreferenceSession> b)
    {
        LocalMapping.Record(b, "MealPreferenceSessions");
        b.HasKey(x => new { x.EventId, x.Id });
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.Id })
            .HasPrincipalKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.GroupId).HasMaxLength(200).IsRequired();
        b.Property(x => x.IdentityFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.EligibilityFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.PersonOptions).IsRequired(); b.Property(x => x.SelectedIds).IsRequired();
        b.Property(x => x.ReviewedPages).IsRequired();
        b.Property(x => x.StartedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.Property(x => x.ExpiresAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.ToTable("MealPreferenceSessions", t =>
        {
            t.HasCheckConstraint("CK_MealPreferenceSessions_Step", "Step BETWEEN 0 AND 5");
            t.HasCheckConstraint("CK_MealPreferenceSessions_Meal", "Meal IS NULL OR Meal IN (0,1,2)");
            t.HasCheckConstraint("CK_MealPreferenceSessions_Page", "Page >= 1");
        });
    }
}
