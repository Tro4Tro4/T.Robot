using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class SimulationActionReceiptConfiguration : IEntityTypeConfiguration<SimulationActionReceipt>
{
    public void Configure(EntityTypeBuilder<SimulationActionReceipt> b)
    {
        LocalMapping.Record(b, "SimulationActionReceipts");
        b.HasIndex(x => new { x.EventId, x.TargetId });
        b.Property(x => x.AtUtc).HasConversion(x => x, x => DateTime.SpecifyKind(x, DateTimeKind.Utc));
        b.HasOne<EventParticipant>().WithMany().HasForeignKey(x => new { x.EventId, x.PersonId }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
    }
}
