using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class PollConfiguration : IEntityTypeConfiguration<Poll>
{
    public void Configure(EntityTypeBuilder<Poll> b)
    {
        LocalMapping.Record(b, "Polls"); b.HasAlternateKey(x => new { x.EventId, x.Id });
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.DeadlineUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.Property(x => x.ClosedAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
        b.HasOne<OutboundMessage>().WithMany().HasForeignKey(x => new { x.EventId, x.PublicationMessageId })
            .HasPrincipalKey(x => new { x.EventId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<OutboundMessage>().WithMany().HasForeignKey(x => new { x.EventId, x.ResultMessageId })
            .HasPrincipalKey(x => new { x.EventId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<PollOption>().WithMany().HasForeignKey(x => new { x.EventId, x.Id, x.DecisionOptionId })
            .HasPrincipalKey(x => new { x.EventId, x.PollId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("Polls", t =>
        {
            t.HasCheckConstraint("CK_Polls_Status", "Status IN (0,1,2,3)");
            t.HasCheckConstraint("CK_Polls_Publication", "Status NOT IN (1,2) OR PublicationMessageId IS NOT NULL");
            t.HasCheckConstraint("CK_Polls_Closed", "(Status = 2 AND ClosedAtUtc IS NOT NULL) OR (Status <> 2 AND ClosedAtUtc IS NULL)");
            t.HasCheckConstraint("CK_Polls_Result", "(ResultMessageId IS NULL AND DecisionOptionId IS NULL) OR Status = 2");
        });
    }
}
internal sealed class PollOptionConfiguration : IEntityTypeConfiguration<PollOption>
{
    public void Configure(EntityTypeBuilder<PollOption> b)
    {
        LocalMapping.Record(b, "PollOptions"); b.HasAlternateKey(x => new { x.EventId, x.PollId, x.Id });
        b.HasIndex(x => new { x.EventId, x.PollId, x.Position }).IsUnique();
        b.Property(x => x.Text).HasMaxLength(150).IsRequired();
        b.HasOne<Poll>().WithMany().HasForeignKey(x => new { x.EventId, x.PollId })
            .HasPrincipalKey(x => new { x.EventId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("PollOptions", t => t.HasCheckConstraint("CK_PollOptions_Position", "Position BETWEEN 1 AND 10"));
    }
}
internal sealed class PollVoteConfiguration : IEntityTypeConfiguration<PollVote>
{
    public void Configure(EntityTypeBuilder<PollVote> b)
    {
        LocalMapping.Record(b, "PollVotes"); b.HasIndex(x => new { x.EventId, x.PollId, x.PersonId }).IsUnique();
        b.HasOne<PollOption>().WithMany().HasForeignKey(x => new { x.EventId, x.PollId, x.OptionId })
            .HasPrincipalKey(x => new { x.EventId, x.PollId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class PollVoteReceiptConfiguration : IEntityTypeConfiguration<PollVoteReceipt>
{
    public void Configure(EntityTypeBuilder<PollVoteReceipt> b)
    {
        LocalMapping.Record(b, "PollVoteReceipts");
        b.Property(x => x.AtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.HasOne<PollOption>().WithMany().HasForeignKey(x => new { x.EventId, x.PollId, x.OptionId })
            .HasPrincipalKey(x => new { x.EventId, x.PollId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}
