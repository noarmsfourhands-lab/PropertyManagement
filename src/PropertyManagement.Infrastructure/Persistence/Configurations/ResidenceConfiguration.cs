using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Entities;

namespace PropertyManagement.Infrastructure.Persistence.Configurations;

/// <summary>Mapping for <see cref="Residence"/>.</summary>
public class ResidenceConfiguration : IEntityTypeConfiguration<Residence>
{
    public void Configure(EntityTypeBuilder<Residence> builder)
    {
        builder.ToTable("Residences");

        builder.HasKey(residence => residence.Id);

        // Guards this row against a concurrent edit of the same row. See the note on the entity for
        // why the section's token cannot do this job.
        builder.Property(residence => residence.Version).IsConcurrencyToken().IsRequired();

        builder.Property(residence => residence.AddressLine1).IsRequired().HasMaxLength(200);
        builder.Property(residence => residence.AddressLine2).HasMaxLength(200);
        builder.Property(residence => residence.City).IsRequired().HasMaxLength(100);
        builder.Property(residence => residence.State).IsRequired().HasMaxLength(50);
        builder.Property(residence => residence.PostalCode).IsRequired().HasMaxLength(20);
        builder.Property(residence => residence.LandlordName).IsRequired().HasMaxLength(200);
        builder.Property(residence => residence.LandlordPhone).IsRequired().HasMaxLength(30);
        builder.Property(residence => residence.MoveInDate).IsRequired();
        builder.Property(residence => residence.MoveOutDate);

        builder.HasIndex(residence => residence.RentalApplicationId);
    }
}
