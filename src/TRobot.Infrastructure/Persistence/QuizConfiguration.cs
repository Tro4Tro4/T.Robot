using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class GameMembershipConfiguration : IEntityTypeConfiguration<GameMembership>
{
    public void Configure(EntityTypeBuilder<GameMembership> b)
    {
        LocalMapping.Record(b, "GameMemberships"); b.HasIndex(x => new { x.EventId, x.PersonId }).IsUnique();
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class QuizConfiguration : IEntityTypeConfiguration<Quiz>
{
    public void Configure(EntityTypeBuilder<Quiz> b)
    {
        LocalMapping.Record(b, "Quizzes"); b.HasAlternateKey(x => new { x.EventId, x.Id });
        b.Property(x => x.Question).HasMaxLength(200).IsRequired();
        b.Property(x => x.DeadlineUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.Property(x => x.ClosedAtUtc).HasConversion(x => x, x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
        b.HasOne<OutboundMessage>().WithMany().HasForeignKey(x => new { x.EventId, x.PublicationMessageId })
            .HasPrincipalKey(x => new { x.EventId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("Quizzes", t =>
        {
            t.HasCheckConstraint("CK_Quizzes_Status", "Status IN (0,1,2,3)");
            t.HasCheckConstraint("CK_Quizzes_Correct", "CorrectPosition BETWEEN 1 AND 10");
            t.HasCheckConstraint("CK_Quizzes_Publication", "Status NOT IN (1,2) OR PublicationMessageId IS NOT NULL");
            t.HasCheckConstraint("CK_Quizzes_Closed", "(Status = 2 AND ClosedAtUtc IS NOT NULL) OR (Status <> 2 AND ClosedAtUtc IS NULL)");
        });
    }
}
internal sealed class QuizOptionConfiguration : IEntityTypeConfiguration<QuizOption>
{
    public void Configure(EntityTypeBuilder<QuizOption> b)
    {
        LocalMapping.Record(b, "QuizOptions"); b.HasAlternateKey(x => new { x.EventId, x.QuizId, x.Id });
        b.HasIndex(x => new { x.EventId, x.QuizId, x.Position }).IsUnique(); b.Property(x => x.Text).HasMaxLength(150).IsRequired();
        b.HasOne<Quiz>().WithMany().HasForeignKey(x => new { x.EventId, x.QuizId }).HasPrincipalKey(x => new { x.EventId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("QuizOptions", t => t.HasCheckConstraint("CK_QuizOptions_Position", "Position BETWEEN 1 AND 10"));
    }
}
internal sealed class QuizAnswerConfiguration : IEntityTypeConfiguration<QuizAnswer>
{
    public void Configure(EntityTypeBuilder<QuizAnswer> b)
    {
        LocalMapping.Record(b, "QuizAnswers"); b.HasIndex(x => new { x.EventId, x.QuizId, x.PersonId }).IsUnique();
        b.Property(x => x.AtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.HasOne<QuizOption>().WithMany().HasForeignKey(x => new { x.EventId, x.QuizId, x.OptionId })
            .HasPrincipalKey(x => new { x.EventId, x.QuizId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}
