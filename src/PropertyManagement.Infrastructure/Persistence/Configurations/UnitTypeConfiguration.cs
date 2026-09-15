using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Entities;

namespace PropertyManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for the <see cref="UnitType"/> lookup. Deactivation is a flag rather than a delete so
/// that units already carrying the type keep a valid reference.
/// </summary>
public class UnitTypeConfiguration : IEntityTypeConfiguration<UnitType>
{
    public void Configure(EntityTypeBuilder<UnitType> builder)
    {
        builder.ToTable("UnitTypes");

        builder.HasKey(unitType => unitType.Id);

        builder.Property(unitType => unitType.Name).IsRequired().HasMaxLength(100);
        builder.Property(unitType => unitType.IsActive).IsRequired();

        builder.HasIndex(unitType => unitType.Name).IsUnique();

        // No index on IsActive on purpose: this is a handful of rows, so any plan is a scan and an
        // index would only cost writes.
    }
}
