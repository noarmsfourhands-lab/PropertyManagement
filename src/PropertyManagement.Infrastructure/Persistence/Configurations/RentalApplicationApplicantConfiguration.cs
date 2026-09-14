using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Entities;

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
    }
}
