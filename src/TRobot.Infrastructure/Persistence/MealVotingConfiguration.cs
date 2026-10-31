using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class MealVotingCycleConfiguration : IEntityTypeConfiguration<MealVotingCycle>
{
    public void Configure(EntityTypeBuilder<MealVotingCycle> b)
    {
        LocalMapping.Record(b, "MealVotingCycles"); b.HasAlternateKey(x => new { x.EventId, x.Id });
        b.Property(x => x.CreatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.Property(x => x.OpenedAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
        b.Property(x => x.ClosedAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
        b.ToTable("MealVotingCycles", t => t.HasCheckConstraint("CK_MealVotingCycles_State", "State BETWEEN 0 AND 3"));
        b.HasIndex(x => x.EventId).IsUnique().HasFilter("State <> 3");
        b.HasIndex(x => new { x.EventId, x.Number }).IsUnique();
    }
}
internal sealed class MealVotingEntryConfiguration : IEntityTypeConfiguration<MealVotingEntry>
{
    public void Configure(EntityTypeBuilder<MealVotingEntry> b)
    {
        LocalMapping.Record(b, "MealVotingEntries"); b.HasAlternateKey(x => new { x.EventId, x.CycleId, x.Id });
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasOne<MealVotingCycle>().WithMany().HasForeignKey(x => new { x.EventId, x.CycleId }).HasPrincipalKey(x => new { x.EventId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("MealVotingEntries", t => t.HasCheckConstraint("CK_MealVotingEntries_Meal", "Meal IN (0,1,2)"));
    }
}
internal sealed class MealVotingSourceConfiguration : IEntityTypeConfiguration<MealVotingSource>
{
    public void Configure(EntityTypeBuilder<MealVotingSource> b)
    {
        LocalMapping.Record(b, "MealVotingSources");
        b.HasIndex(x => new { x.EventId, x.CycleId, x.ProposalId }).IsUnique();
        b.HasOne<MealVotingEntry>().WithMany().HasForeignKey(x => new { x.EventId, x.CycleId, x.EntryId }).HasPrincipalKey(x => new { x.EventId, x.CycleId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<MealProposal>().WithMany().HasForeignKey(x => new { x.EventId, x.ProposalId }).HasPrincipalKey(x => new { x.EventId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class MealPreferenceBallotConfiguration : IEntityTypeConfiguration<MealPreferenceBallot>
{
    public void Configure(EntityTypeBuilder<MealPreferenceBallot> b)
    {
        LocalMapping.Record(b, "MealPreferenceBallots");
        b.HasIndex(x => new { x.EventId, x.CycleId, x.PersonId, x.Day, x.Meal }).IsUnique();
        b.Property(x => x.SelectionJson).IsRequired();
        b.HasOne<MealVotingCycle>().WithMany().HasForeignKey(x => new { x.EventId, x.CycleId }).HasPrincipalKey(x => new { x.EventId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("MealPreferenceBallots", t => t.HasCheckConstraint("CK_MealPreferenceBallots_Meal", "Meal IN (0,1,2)"));
    }
}
internal sealed class MealPreferenceChangeConfiguration : IEntityTypeConfiguration<MealPreferenceChange>
{
    public void Configure(EntityTypeBuilder<MealPreferenceChange> b)
    {
        LocalMapping.Record(b, "MealPreferenceChanges");
        b.Property(x => x.PayloadFingerprint).HasMaxLength(64).IsRequired();
        b.Property(x => x.PreviousJson).IsRequired(); b.Property(x => x.SelectionJson).IsRequired();
        b.Property(x => x.AtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.HasOne<MealVotingCycle>().WithMany().HasForeignKey(x => new { x.EventId, x.CycleId }).HasPrincipalKey(x => new { x.EventId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.VoterId }).HasPrincipalKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("MealPreferenceChanges", t => t.HasCheckConstraint("CK_MealPreferenceChanges_Meal", "Meal IN (0,1,2)"));
    }
}
