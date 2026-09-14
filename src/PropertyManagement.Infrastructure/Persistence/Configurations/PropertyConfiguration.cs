using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Entities;

namespace PropertyManagement.Infrastructure.Persistence.Configurations;

/// <summary>Mapping for <see cref="Property"/>.</summary>
public class PropertyConfiguration : IEntityTypeConfiguration<Property>
{
    public void Configure(EntityTypeBuilder<Property> builder)
    {
        builder.ToTable("Properties");

        builder.HasKey(property => property.Id);

        builder.Property(property => property.Name).IsRequired().HasMaxLength(200);
        builder.Property(property => property.AddressLine1).IsRequired().HasMaxLength(200);
        builder.Property(property => property.AddressLine2).HasMaxLength(200);
        builder.Property(property => property.City).IsRequired().HasMaxLength(100);
        builder.Property(property => property.State).IsRequired().HasMaxLength(50);
        builder.Property(property => property.PostalCode).IsRequired().HasMaxLength(20);

        builder.HasIndex(property => property.Name);

        builder.HasMany(property => property.Units)
            .WithOne(unit => unit.Property)
            .HasForeignKey(unit => unit.PropertyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
