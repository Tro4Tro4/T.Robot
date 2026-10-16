using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        builder.ToTable("Events", table =>
        {
            table.HasCheckConstraint("CK_Events_Name", "length(trim(Name)) BETWEEN 1 AND 200");
            table.HasCheckConstraint("CK_Events_TimeZoneId", "length(trim(TimeZoneId)) BETWEEN 1 AND 100");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.StartsAtUtc);
        builder.Property(x => x.Revision).HasDefaultValue(0).IsConcurrencyToken();
        builder.Property(x => x.StartsAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        builder.Property(x => x.CreatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
    }
}

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("People", table =>
        {
            table.HasCheckConstraint("CK_People_DisplayName", "length(trim(DisplayName)) BETWEEN 1 AND 150");
            table.HasCheckConstraint("CK_People_WhatsAppSenderId", "WhatsAppSenderId IS NULL OR length(trim(WhatsAppSenderId)) BETWEEN 1 AND 200");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DisplayName).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Revision).HasDefaultValue(0).IsConcurrencyToken();
        builder.Property(x => x.WhatsAppSenderId).HasMaxLength(200);
        builder.HasIndex(x => x.WhatsAppSenderId).IsUnique();
        builder.Property(x => x.CreatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
    }
}

internal sealed class ParticipantConfiguration : IEntityTypeConfiguration<EventParticipant>
{
    public void Configure(EntityTypeBuilder<EventParticipant> builder)
    {
        builder.ToTable("EventParticipants", table => table.HasCheckConstraint("CK_EventParticipants_Status", "Status IN ('NoResponse', 'Confirmed', 'Undecided', 'Absent')"));
        builder.HasKey(x => new { x.EventId, x.PersonId });
        builder.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).IsConcurrencyToken();
        builder.HasIndex(x => new { x.EventId, x.Status });
        builder.Property(x => x.UpdatedAtUtc).HasConversion(x => x,
            x => x.HasValue ? DateTime.SpecifyKind(x.Value, DateTimeKind.Utc) : x);
        builder.Property(x => x.CreatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
    }
}

internal sealed class OrganizerConfiguration : IEntityTypeConfiguration<EventOrganizer>
{
    public void Configure(EntityTypeBuilder<EventOrganizer> builder)
    {
        builder.ToTable("EventOrganizers", table => table.HasCheckConstraint("CK_EventOrganizers_Role", "Role IN ('Organizer', 'Administrator')"));
        builder.HasKey(x => new { x.EventId, x.PersonId });
        builder.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(x => x.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.IsActive).HasDefaultValue(true).HasSentinel(true).IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
    }
}
