using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Entities;

namespace PropertyManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for <see cref="Lease"/>. The unique index on the application id is what makes a second
/// approval of the same application impossible even if two requests race past the service check.
/// </summary>
public class LeaseConfiguration : IEntityTypeConfiguration<Lease>
{
    public void Configure(EntityTypeBuilder<Lease> builder)
    {
        builder.ToTable("Leases");

        builder.HasKey(lease => lease.Id);

        builder.Property(lease => lease.StartDate).IsRequired();
        builder.Property(lease => lease.EndDate).IsRequired();
        builder.Property(lease => lease.MonthlyRent).HasPrecision(18, 2).IsRequired();


        // Two jobs at once. Availability is answered by "does any lease for this unit cover
        // today", so the index leads with the unit and carries the start of the term. Unique,
        // because approval always dates the lease the day it is granted: two managers approving
        // two applications for the same unit at the same moment both pass the availability check,
        // and this is what stops the second insert. A later approval on a different day is stopped
        // by the availability check instead, because the first lease covers that day.
        builder.HasIndex(lease => new { lease.UnitId, lease.StartDate }).IsUnique();

        builder.HasIndex(lease => lease.RentalApplicationId).IsUnique();

        builder.HasOne(lease => lease.Unit)
            .WithMany(unit => unit.Leases)
            .HasForeignKey(lease => lease.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(lease => lease.RentalApplication)
            .WithOne(application => application.Lease)
            .HasForeignKey<Lease>(lease => lease.RentalApplicationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
