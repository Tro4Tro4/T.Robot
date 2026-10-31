using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class MealProposalConfiguration : IEntityTypeConfiguration<MealProposal>
{
    public void Configure(EntityTypeBuilder<MealProposal> b)
    {
        LocalMapping.Record(b, "MealProposals");
        b.Property(x => x.DishName).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.EventId, x.Day, x.Meal });
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.CreatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.Property(x => x.WithdrawnAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
        b.ToTable("MealProposals", t => t.HasCheckConstraint("CK_MealProposals_Meal", "Meal IN (0,1,2)"));
    }
}

internal sealed class MealProposalSessionConfiguration : IEntityTypeConfiguration<MealProposalSession>
{
    public void Configure(EntityTypeBuilder<MealProposalSession> b)
    {
        LocalMapping.Record(b, "MealProposalSessions");
        b.HasKey(x => new { x.EventId, x.Id });
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.Id })
            .HasPrincipalKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.GroupId).HasMaxLength(200).IsRequired();
        b.Property(x => x.IdentityFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.DishName).HasMaxLength(200);
        b.Property(x => x.StartedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.Property(x => x.ExpiresAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.ToTable("MealProposalSessions", t =>
        {
            t.HasCheckConstraint("CK_MealProposalSessions_Step", "Step BETWEEN 0 AND 5");
            t.HasCheckConstraint("CK_MealProposalSessions_Meal", "Meal IS NULL OR Meal IN (0,1,2)");
        });
    }
}
