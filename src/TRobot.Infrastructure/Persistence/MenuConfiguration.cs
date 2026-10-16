using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

internal sealed class MenuConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> b)
    {
        LocalMapping.Record(b, "MenuItems");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Unit).HasMaxLength(30).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.Property(x => x.Quantity).HasPrecision(10, 3);
        b.HasOne<Person>().WithMany().HasForeignKey(x => x.ResponsiblePersonId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("MenuItems", t =>
        {
            t.HasCheckConstraint("CK_MenuItems_Category", "Category IN (0, 1, 2)");
            t.HasCheckConstraint("CK_MenuItems_Availability", "Availability IN (0, 1, 2, 3)");
            t.HasCheckConstraint("CK_MenuItems_Responsible", "Availability <> 1 OR ResponsiblePersonId IS NOT NULL");
        });
    }
}
