using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class EventAuditConfiguration : IEntityTypeConfiguration<EventAudit>
{
    public void Configure(EntityTypeBuilder<EventAudit> builder)
    {
        builder.ToTable("EventAudits", table =>
        {
            table.HasCheckConstraint("CK_EventAudits_Action", "Action IN ('EventCreated', 'EventUpdated', 'ParticipantAdded', 'ParticipantRenamed')");
            table.HasCheckConstraint("CK_EventAudits_Person", "(Action IN ('ParticipantAdded', 'ParticipantRenamed') AND PersonId IS NOT NULL) OR (Action NOT IN ('ParticipantAdded', 'ParticipantRenamed') AND PersonId IS NULL)");
            table.HasCheckConstraint("CK_EventAudits_Fingerprint", "RegistrationFingerprint IS NULL OR (Action = 'ParticipantAdded' AND length(RegistrationFingerprint) = 64 AND RegistrationFingerprint NOT GLOB '*[^0-9A-F]*')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.RegistrationFingerprint).HasMaxLength(64);
        builder.Property(x => x.OccurredAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        builder.HasIndex(x => new { x.EventId, x.OccurredAtUtc });
        builder.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
    }
}
