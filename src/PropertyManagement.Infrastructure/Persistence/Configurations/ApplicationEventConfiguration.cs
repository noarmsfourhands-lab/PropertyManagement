using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Entities;

namespace PropertyManagement.Infrastructure.Persistence.Configurations;

/// <summary>Mapping for the append-only application audit trail.</summary>
public class ApplicationEventConfiguration : IEntityTypeConfiguration<ApplicationEvent>
{
    public void Configure(EntityTypeBuilder<ApplicationEvent> builder)
    {
        builder.ToTable("ApplicationEvents");

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.FromStatus).HasConversion<int>().IsRequired();
        builder.Property(entry => entry.ToStatus).HasConversion<int>().IsRequired();
        builder.Property(entry => entry.Outcome).HasConversion<int>();
        builder.Property(entry => entry.Comment).HasMaxLength(2000);
        builder.Property(entry => entry.ActorUserId).IsRequired().HasMaxLength(450);
        builder.Property(entry => entry.ActorName).IsRequired().HasMaxLength(256);
        builder.Property(entry => entry.OccurredAtUtc).IsRequired();

        // The history renders newest-first for one application.
        builder.HasIndex(entry => new { entry.RentalApplicationId, entry.OccurredAtUtc });
    }
}
