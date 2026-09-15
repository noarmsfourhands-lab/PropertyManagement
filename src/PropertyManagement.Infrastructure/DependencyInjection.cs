using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PropertyManagement.Infrastructure.Identity;
using PropertyManagement.Infrastructure.Persistence;
using PropertyManagement.Infrastructure.Seeding;
using PropertyManagement.Infrastructure.Services;

namespace PropertyManagement.Infrastructure;

/// <summary>
/// Registers everything the infrastructure layer owns, so the web project's start-up stays a list
/// of intentions rather than a list of implementation details.
/// </summary>
public static class DependencyInjection
{
    public const string ConnectionStringName = "DefaultConnection";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' was not found. See the README for setup.");

        services.AddDbContext<PropertyManagementDbContext>(dbOptions =>
            dbOptions.UseSqlServer(connectionString, sqlOptions =>
                sqlOptions.MigrationsAssembly(typeof(PropertyManagementDbContext).Assembly.FullName)));

        services
            .AddIdentity<ApplicationUser, IdentityRole>(identityOptions =>
            {
                identityOptions.User.RequireUniqueEmail = true;
                identityOptions.SignIn.RequireConfirmedAccount = false;

                identityOptions.Password.RequiredLength = 8;
                identityOptions.Password.RequireDigit = true;
                identityOptions.Password.RequireUppercase = true;
                identityOptions.Password.RequireLowercase = true;
                identityOptions.Password.RequireNonAlphanumeric = true;

                identityOptions.Lockout.MaxFailedAccessAttempts = 5;
                identityOptions.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<PropertyManagementDbContext>()
            .AddClaimsPrincipalFactory<ApplicationUserClaimsPrincipalFactory>()
            .AddDefaultTokenProviders();

        services.Configure<SeedOptions>(configuration.GetSection(SeedOptions.SectionName));
        services.AddScoped<DatabaseSeeder>();

        services.AddScoped<IPropertyService, PropertyService>();
        services.AddScoped<IRentalApplicationService, RentalApplicationService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<INoteService, NoteService>();
        services.AddScoped<IApplicationApplicantService, ApplicationApplicantService>();

        // Injected wherever the code needs "now", so tests can supply their own clock.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
