using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PropertyManagement.Domain.Entities;

namespace PropertyManagement.Infrastructure.Persistence.Configurations;

/// <summary>Mapping for manager-only notes.</summary>
public class PropertyManagerNoteConfiguration : IEntityTypeConfiguration<PropertyManagerNote>
{
    public void Configure(EntityTypeBuilder<PropertyManagerNote> builder)
    {
        builder.ToTable("PropertyManagerNotes");

        builder.HasKey(note => note.Id);

        builder.Property(note => note.Body).IsRequired().HasMaxLength(4000);
        builder.Property(note => note.AuthorUserId).IsRequired().HasMaxLength(450);
        builder.Property(note => note.AuthorName).IsRequired().HasMaxLength(256);
        builder.Property(note => note.CreatedAtUtc).IsRequired();

        builder.HasIndex(note => note.RentalApplicationId);
    }
}
