using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Entities;

namespace PropertyManagement.Infrastructure.Persistence.Configurations;

/// <summary>Mapping for <see cref="RentalApplication"/> and its owned applicant-information section.</summary>
public class RentalApplicationConfiguration : IEntityTypeConfiguration<RentalApplication>
{
    public void Configure(EntityTypeBuilder<RentalApplication> builder)
    {
        builder.ToTable("RentalApplications");

        builder.HasKey(application => application.Id);

        builder.Property(application => application.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(application => application.ClaimedByUserId).HasMaxLength(450);

        // Per-section concurrency. SQL Server permits one rowversion per table, so these are
        // application-managed tokens: a save that carries a stale token for its own section is
        // rejected, while a save to the other section is untouched.
        builder.Property(application => application.ApplicantInformationVersion)
            .IsConcurrencyToken()
            .IsRequired();

        builder.Property(application => application.ResidenceHistoryVersion)
            .IsConcurrencyToken()
            .IsRequired();

        builder.OwnsOne(application => application.ApplicantInformation, information =>
        {
            information.Property(value => value.FirstName).HasColumnName("ApplicantFirstName").HasMaxLength(100);
            information.Property(value => value.LastName).HasColumnName("ApplicantLastName").HasMaxLength(100);
            information.Property(value => value.Phone).HasColumnName("ApplicantPhone").HasMaxLength(30);
            information.Property(value => value.Email).HasColumnName("ApplicantEmail").HasMaxLength(256);
            information.Property(value => value.AddressLine1).HasColumnName("ApplicantAddressLine1").HasMaxLength(200);
            information.Property(value => value.AddressLine2).HasColumnName("ApplicantAddressLine2").HasMaxLength(200);
            information.Property(value => value.City).HasColumnName("ApplicantCity").HasMaxLength(100);
            information.Property(value => value.State).HasColumnName("ApplicantState").HasMaxLength(50);
            information.Property(value => value.PostalCode).HasColumnName("ApplicantPostalCode").HasMaxLength(20);
            information.Ignore(value => value.FullName);
        });

        // The list filters by status and by property, and the property is reached through the unit.
        builder.HasIndex(application => application.Status);
        builder.HasIndex(application => new { application.UnitId, application.Status });

        builder.HasOne(application => application.Unit)
            .WithMany(unit => unit.Applications)
            .HasForeignKey(application => application.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(application => application.Residences)
            .WithOne(residence => residence.RentalApplication)
            .HasForeignKey(residence => residence.RentalApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(application => application.Events)
            .WithOne(entry => entry.RentalApplication)
            .HasForeignKey(entry => entry.RentalApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(application => application.Notes)
            .WithOne(note => note.RentalApplication)
            .HasForeignKey(note => note.RentalApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(application => application.Applicants)
            .WithOne(link => link.RentalApplication)
            .HasForeignKey(link => link.RentalApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(application => application.ApplicantInformationSaved);
        builder.Ignore(application => application.ResidenceHistorySaved);
    }
}
