using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Infrastructure.Identity;

namespace PropertyManagement.Infrastructure.Persistence;

/// <summary>
/// The application's single context. It carries both the domain tables and the ASP.NET Identity
/// tables so that a user and the rows referring to them commit in one transaction.
/// </summary>
public class PropertyManagementDbContext(DbContextOptions<PropertyManagementDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Property> Properties => Set<Property>();

    public DbSet<Unit> Units => Set<Unit>();

    public DbSet<UnitType> UnitTypes => Set<UnitType>();

    public DbSet<Lease> Leases => Set<Lease>();

    public DbSet<RentalApplication> RentalApplications => Set<RentalApplication>();

    public DbSet<RentalApplicationApplicant> RentalApplicationApplicants => Set<RentalApplicationApplicant>();

    public DbSet<Residence> Residences => Set<Residence>();

    public DbSet<ApplicationEvent> ApplicationEvents => Set<ApplicationEvent>();

    public DbSet<PropertyManagerNote> PropertyManagerNotes => Set<PropertyManagerNote>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(typeof(PropertyManagementDbContext).Assembly);
    }
}
