using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

public static class LocalMapping
{
    public static void Record<T>(EntityTypeBuilder<T> b, string table) where T : class, ILocalRecord
    {
        b.ToTable(table); b.HasKey(x => x.Id); b.Property(x => x.Revision).IsConcurrencyToken();
        b.HasIndex(x => x.EventId); b.HasOne<Event>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
    }
}
internal sealed class LocalAuditConfiguration : IEntityTypeConfiguration<LocalAudit>
{
    public void Configure(EntityTypeBuilder<LocalAudit> b)
    {
        LocalMapping.Record(b, "LocalAudits");
        b.Property(x => x.Module).HasMaxLength(40).IsRequired(); b.Property(x => x.Action).HasMaxLength(80).IsRequired();
        b.Property(x => x.AtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}
