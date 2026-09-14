using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Entities;

namespace PropertyManagement.Infrastructure.Persistence.Configurations;

/// <summary>Mapping for <see cref="Unit"/>.</summary>
public class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("Units");

        builder.HasKey(unit => unit.Id);

        builder.Property(unit => unit.UnitNumber).IsRequired().HasMaxLength(20);
        builder.Property(unit => unit.Bedrooms).IsRequired();
        builder.Property(unit => unit.MonthlyRent).HasPrecision(18, 2).IsRequired();

        // A unit number is unique inside its own property, not across the whole estate.
        builder.HasIndex(unit => new { unit.PropertyId, unit.UnitNumber }).IsUnique();

        builder.HasOne(unit => unit.UnitType)
            .WithMany(unitType => unitType.Units)
            .HasForeignKey(unit => unit.UnitTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
