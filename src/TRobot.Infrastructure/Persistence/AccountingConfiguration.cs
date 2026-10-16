using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> b)
    {
        LocalMapping.Record(b, "Expenses");
        b.Property(x => x.Description).IsRequired().HasMaxLength(150); b.Property(x => x.Amount).HasPrecision(12, 2);
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, PersonId = x.AdvancedByPersonId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.DeclaredByActorId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.CreatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.Property(x => x.ChangedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.ToTable("Expenses", t => t.HasCheckConstraint("CK_Expenses_Status", "Status IN (0,1,2,3)"));
    }
}
internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        LocalMapping.Record(b, "Payments"); b.Property(x => x.Amount).HasPrecision(12, 2);
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, PersonId = x.FromPersonId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, PersonId = x.ToPersonId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.DeclaredByActorId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.CreatedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.Property(x => x.ChangedAtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.ToTable("Payments", t =>
        {
            t.HasCheckConstraint("CK_Payments_Status", "Status IN (0,1,2)");
            t.HasCheckConstraint("CK_Payments_DistinctPeople", "FromPersonId <> ToPersonId");
        });
    }
}
internal sealed class AccountingSettingsConfiguration : IEntityTypeConfiguration<AccountingSettings>
{
    public void Configure(EntityTypeBuilder<AccountingSettings> b)
    {
        LocalMapping.Record(b, "AccountingSettings"); b.HasIndex(x => x.EventId).IsUnique();
        b.Property(x => x.ChildWeight).HasPrecision(5, 3);
    }
}
internal sealed class ParticipantWeightConfiguration : IEntityTypeConfiguration<ParticipantWeight>
{
    public void Configure(EntityTypeBuilder<ParticipantWeight> b)
    {
        LocalMapping.Record(b, "ParticipantWeights"); b.HasIndex(x => new { x.EventId, x.PersonId }).IsUnique();
        b.Property(x => x.Weight).HasPrecision(5, 3);
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
    }
}
