using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Infrastructure.Identity;

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

        builder.Property(application => application.PropertyName).IsRequired().HasMaxLength(200);
        builder.Property(application => application.UnitNumber).IsRequired().HasMaxLength(20);

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

        // Restrict, not cascade. These two are records about an application rather than parts of
        // it: the audit trail is append-only by design, and a note is the property manager's own
        // writing. Cascading would mean a future delete of an application quietly destroyed both,
        // and would do it for some statuses and fail for others, because an approved application is
        // already held back by its lease. Refusing while history exists is one rule for every case.
        builder.HasMany(application => application.Events)
            .WithOne(entry => entry.RentalApplication)
            .HasForeignKey(entry => entry.RentalApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(application => application.Notes)
            .WithOne(note => note.RentalApplication)
            .HasForeignKey(note => note.RentalApplicationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(application => application.Applicants)
            .WithOne(link => link.RentalApplication)
            .HasForeignKey(link => link.RentalApplicationId)
            .OnDelete(DeleteBehavior.Cascade);

        // A real foreign key, because this column is read as live authority: whether somebody may
        // review or release an application is decided by comparing it to the signed-in user. An id
        // left pointing at a deleted account would freeze the application for everyone. Restrict
        // means the account cannot be deleted while it still holds a claim.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(application => application.ClaimedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Applicant information is optional while an application is being written and while it is
        // back with the applicant, and a draft can be withdrawn before anything is typed. Once it
        // has been submitted it is frozen, and every one of these was required to get there, so the
        // database says so rather than trusting that every future code path remembers to check.
        // The listed statuses are Submitted, UnderReview, Approved and Denied: the four an
        // application can only reach by passing the submission rule, and the four in which it can
        // no longer be edited. Draft, Returned and Withdrawn are all legitimately incomplete.
        builder.ToTable(table => table.HasCheckConstraint(
            "CK_RentalApplications_SubmittedHasApplicantInformation",
            """
            [Status] NOT IN (1, 2, 4, 5)
            OR ([ApplicantFirstName] IS NOT NULL
                AND [ApplicantLastName] IS NOT NULL
                AND [ApplicantPhone] IS NOT NULL
                AND [ApplicantEmail] IS NOT NULL
                AND [ApplicantAddressLine1] IS NOT NULL
                AND [ApplicantCity] IS NOT NULL
                AND [ApplicantState] IS NOT NULL
                AND [ApplicantPostalCode] IS NOT NULL)
            """));

        builder.Ignore(application => application.ApplicantInformationSaved);
        builder.Ignore(application => application.ResidenceHistorySaved);
    }
}
