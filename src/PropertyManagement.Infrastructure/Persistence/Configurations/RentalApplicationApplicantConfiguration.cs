using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Infrastructure.Identity;

namespace PropertyManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapping for the applicant join. The composite key stops the same applicant being attached twice.
/// </summary>
public class RentalApplicationApplicantConfiguration : IEntityTypeConfiguration<RentalApplicationApplicant>
{
    public void Configure(EntityTypeBuilder<RentalApplicationApplicant> builder)
    {
        builder.ToTable("RentalApplicationApplicants");

        builder.HasKey(link => new { link.RentalApplicationId, link.ApplicantUserId });

        builder.Property(link => link.ApplicantUserId).IsRequired().HasMaxLength(450);

        // "Show me my applications" filters on this column.
        builder.HasIndex(link => link.ApplicantUserId);

        // A real foreign key, because every permission decision in the application runs through
        // this column: whether somebody may open, edit, submit or withdraw is decided by asking
        // whether their id is in this set. An id left pointing at a deleted account would leave the
        // application readable by nobody, editable by nobody, and unrepairable, because the primary
        // applicant cannot be removed either. Restrict refuses the deletion instead.
        //
        // The audit columns elsewhere deliberately have no key: they store a name beside the id, so
        // they keep reading correctly after an account is gone. This one stores authority.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(link => link.ApplicantUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
