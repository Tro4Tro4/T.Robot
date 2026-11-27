using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class AttendanceQuestionConfiguration : IEntityTypeConfiguration<AttendanceQuestion>
{
    public void Configure(EntityTypeBuilder<AttendanceQuestion> b)
    {
        LocalMapping.Record(b, "AttendanceQuestions");
        b.Property(x => x.Text).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.EventId, x.Position });
    }
}

internal sealed class AttendanceQuestionOptionConfiguration : IEntityTypeConfiguration<AttendanceQuestionOption>
{
    public void Configure(EntityTypeBuilder<AttendanceQuestionOption> b)
    {
        LocalMapping.Record(b, "AttendanceQuestionOptions");
        b.Property(x => x.Text).HasMaxLength(60).IsRequired();
        b.HasOne<AttendanceQuestion>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.QuestionId, x.Position }).IsUnique();
    }
}

internal sealed class AttendanceAnswerConfiguration : IEntityTypeConfiguration<AttendanceAnswer>
{
    public void Configure(EntityTypeBuilder<AttendanceAnswer> b)
    {
        LocalMapping.Record(b, "AttendanceAnswers");
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AttendanceQuestion>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AttendanceQuestionOption>().WithMany().HasForeignKey(x => x.OptionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.EventId, x.PersonId, x.QuestionId }).IsUnique();
        b.Property(x => x.AnsweredAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
    }
}
