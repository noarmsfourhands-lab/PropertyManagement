using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Infrastructure.Identity;
using PropertyManagement.Infrastructure.Persistence;
using PropertyManagement.Infrastructure.Seeding;

namespace PropertyManagement.Infrastructure.Tests;

/// <summary>
/// The seeder has to be safe to run on every start-up, so these tests run it twice and check that
/// the second run changes nothing. Migrating is left out: the schema comes from the fixture, which
/// is exactly why the seeder keeps migrating and seeding in separate methods.
/// </summary>
public class DatabaseSeederTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly ServiceProvider _services;

    public DatabaseSeederTests() => _services = ProviderOver(_database);

    /// <summary>
    /// A provider wired for seeding over one database. Three tests need one, over two databases and
    /// with the switch both ways, and writing it out three times is how two of the copies end up
    /// configured slightly differently from the one whose behaviour is being asserted.
    /// </summary>
    private static ServiceProvider ProviderOver(TestDatabase database, bool seedingEnabled = true)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        services.AddDbContext<PropertyManagementDbContext>(options =>
            options.UseSqlite(database.Connection));

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<PropertyManagementDbContext>();

        services.AddSingleton<TimeProvider>(new FixedTimeProvider(TestData.Now));

        services.Configure<SeedOptions>(options =>
        {
            // Stated outright, because the option defaults to off: the safe default for a switch
            // that invents tenants is the one that does nothing.
            options.Enabled = seedingEnabled;
            options.PropertyManagerCount = 2;
            options.ApplicantCount = 3;
            options.PropertyCount = 2;
            options.MinUnitsPerProperty = 8;
            options.MaxUnitsPerProperty = 10;
            options.ApplicationsPerStatus = 1;
        });

        services.AddScoped<DatabaseSeeder>();

        return services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }

    private async Task SeedAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedDataAsync();
    }

    private sealed record Counts(
        int Roles, int Users, int UnitTypes, int Properties, int Units, int Applications, int Leases, int Events);

    private async Task<Counts> CountAsync()
    {
        await using var db = _database.CreateContext();

        return new Counts(
            await db.Roles.CountAsync(),
            await db.Users.CountAsync(),
            await db.UnitTypes.CountAsync(),
            await db.Properties.CountAsync(),
            await db.Units.CountAsync(),
            await db.RentalApplications.CountAsync(),
            await db.Leases.CountAsync(),
            await db.ApplicationEvents.CountAsync());
    }

    [Fact]
    public async Task Seeding_fills_an_empty_database()
    {
        await SeedAsync();

        var counts = await CountAsync();

        Assert.Equal(2, counts.Roles);
        Assert.Equal(5, counts.Users);
        Assert.Equal(6, counts.UnitTypes);
        Assert.Equal(2, counts.Properties);
        Assert.True(counts.Units >= 16);
        Assert.True(counts.Applications > 0);
    }

    [Fact]
    public async Task Seeding_twice_changes_nothing()
    {
        await SeedAsync();
        var first = await CountAsync();

        await SeedAsync();
        var second = await CountAsync();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Every_application_status_is_represented()
    {
        await SeedAsync();

        await using var db = _database.CreateContext();
        var statuses = await db.RentalApplications
            .Select(application => application.Status)
            .Distinct()
            .ToListAsync();

        foreach (var status in Enum.GetValues<ApplicationStatus>())
        {
            Assert.Contains(status, statuses);
        }
    }

    [Fact]
    public async Task Both_roles_exist_and_every_user_is_in_one_of_them()
    {
        await SeedAsync();

        await using var db = _database.CreateContext();

        var roleNames = await db.Roles.Select(role => role.Name).ToListAsync();
        Assert.Contains(UserRole.Applicant, roleNames);
        Assert.Contains(UserRole.PropertyManager, roleNames);

        var usersInRoles = await db.UserRoles.Select(link => link.UserId).Distinct().CountAsync();
        Assert.Equal(await db.Users.CountAsync(), usersInRoles);
    }

    [Fact]
    public async Task Every_seeded_unit_has_the_bedrooms_its_type_says_it_has()
    {
        await SeedAsync();

        await using var db = _database.CreateContext();

        var units = await db.Units
            .Select(unit => new { unit.UnitNumber, unit.Bedrooms, TypeName = unit.UnitType.Name })
            .ToListAsync();

        Assert.NotEmpty(units);

        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Studio"] = 0,
            ["Loft"] = 0,
            ["One Bedroom"] = 1,
            ["Two Bedroom"] = 2,
            ["Three Bedroom"] = 3
        };

        // Drawing the type and the bedroom count independently produced units labelled
        // "Two Bedroom" with nought bedrooms. Demo data that contradicts itself on screen costs
        // more than it saves.
        var wrong = units
            .Where(unit => expected.TryGetValue(unit.TypeName, out var beds) && beds != unit.Bedrooms)
            .Select(unit => $"{unit.UnitNumber} is a {unit.TypeName} with {unit.Bedrooms}")
            .ToList();

        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    [Fact]
    public async Task The_lookup_includes_a_retired_unit_type_that_no_new_unit_uses()
    {
        await SeedAsync();

        await using var db = _database.CreateContext();

        var retired = await db.UnitTypes.SingleAsync(unitType => !unitType.IsActive);
        var unitsUsingIt = await db.Units.CountAsync(unit => unit.UnitTypeId == retired.Id);

        // The retired type exists so the inactive-lookup rule has something to act on, and no
        // seeded unit is given it, because the rule forbids selecting it.
        Assert.Equal(0, unitsUsingIt);
    }

    [Fact]
    public async Task Approved_applications_hold_a_live_lease()
    {
        await SeedAsync();

        await using var db = _database.CreateContext();

        var approved = await db.RentalApplications
            .Include(application => application.Lease)
            .Where(application => application.Status == ApplicationStatus.Approved)
            .ToListAsync();

        Assert.NotEmpty(approved);

        foreach (var application in approved)
        {
            Assert.NotNull(application.Lease);
            Assert.True(application.Lease!.CoversDate(TestData.Today));
        }
    }

    [Fact]
    public async Task A_seeded_draft_has_nothing_in_its_history_and_a_decided_one_does()
    {
        await SeedAsync();

        await using var db = _database.CreateContext();

        var draft = await db.RentalApplications
            .Include(application => application.Events)
            .FirstAsync(application => application.Status == ApplicationStatus.Draft);

        var approved = await db.RentalApplications
            .Include(application => application.Events)
            .FirstAsync(application => application.Status == ApplicationStatus.Approved);

        Assert.Empty(draft.Events);
        Assert.NotEmpty(approved.Events);
    }

    [Fact]
    public async Task The_same_seed_produces_the_same_data_every_time()
    {
        await SeedAsync();

        await using var db = _database.CreateContext();
        var names = await db.Properties.OrderBy(property => property.Id)
            .Select(property => property.Name)
            .ToListAsync();

        // A second database, seeded from the same fixed random seed, comes back identical.
        using var other = new TestDatabase();

        await using var provider = ProviderOver(other);
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedDataAsync();
        }

        await using var otherDb = other.CreateContext();
        var otherNames = await otherDb.Properties.OrderBy(property => property.Id)
            .Select(property => property.Name)
            .ToListAsync();

        Assert.Equal(names, otherNames);
    }

    [Fact]
    public async Task Seeding_is_disabled_by_configuration_without_touching_the_database()
    {
        await using var provider = ProviderOver(_database, seedingEnabled: false);
        await using var scope = provider.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedDataAsync();

        var counts = await CountAsync();

        Assert.Equal(0, counts.Properties);
        Assert.Equal(0, counts.Users);
        Assert.Equal(0, counts.UnitTypes);
        Assert.Equal(0, counts.Applications);
    }
}
