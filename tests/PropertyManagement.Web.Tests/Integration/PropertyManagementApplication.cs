using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Infrastructure.Identity;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Web.Tests.Integration;

/// <summary>
/// The real application, booted in process against SQLite held in memory.
///
/// These tests exist because the unit tests cannot see the thing most likely to go wrong. A service
/// can refuse an operation correctly while the route that reaches it forgets to ask, and a rule
/// tested directly is not the same as a rule reached through routing, authentication, the global
/// authorize filter and a controller. Everything here goes through an HTTP request to the pipeline
/// in Program.cs, so what is asserted is what a browser would actually get.
///
/// Only the database is substituted. The connection is opened once and held for the fixture's life,
/// because a SQLite in-memory database exists only as long as a connection to it is open.
/// </summary>
public class PropertyManagementApplication : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public const string Password = "Passw0rd!";

    /// <summary>The applicant who owns <see cref="ApplicationId"/>.</summary>
    public const string OwnerEmail = "owner@example.test";

    /// <summary>A second applicant, on nothing, used to probe for what leaks.</summary>
    public const string StrangerEmail = "stranger@example.test";

    public const string ManagerEmail = "manager@example.test";

    public int ApplicationId { get; private set; }

    public int UnitId { get; private set; }

    /// <summary>The residence seeded on <see cref="ApplicationId"/>, for the edit and remove routes.</summary>
    public int ResidenceId { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Testing rather than Development: appsettings.Development.json turns the demo seeder on,
        // and these tests want a database holding exactly what they put in it.
        builder.UseEnvironment("Testing");

        // The migrations are SQL Server flavoured and this runs on SQLite. The schema is created
        // from the model instead, which is what those migrations are generated from anyway.
        builder.UseSetting("Database:MigrateOnStartup", "false");

        builder.ConfigureServices(services =>
        {
            // The real registration brings a SQL Server context and everything that configures it.
            // All of it goes: leaving the options behind means the provider registered first wins
            // and the replacement below is silently ignored.
            foreach (var registration in services
                         .Where(service => service.ServiceType.FullName?.Contains(
                             nameof(PropertyManagementDbContext), StringComparison.Ordinal) == true
                             || service.ServiceType == typeof(DbContextOptions))
                         .ToList())
            {
                services.Remove(registration);
            }

            services.AddDbContext<PropertyManagementDbContext>(options =>
                options.UseSqlite(_connection));

            services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        });
    }

    // Implemented explicitly: xUnit's lifetime interface is Task-shaped, while the factory this
    // derives from disposes through ValueTask, and one class cannot satisfy both with one method.
    Task IAsyncLifetime.InitializeAsync() => SeedAsync();

    Task IAsyncLifetime.DisposeAsync() => DisposeAsync().AsTask();

    private async Task SeedAsync()
    {
        await _connection.OpenAsync();

        using var scope = Services.CreateScope();
        var provider = scope.ServiceProvider;

        var db = provider.GetRequiredService<PropertyManagementDbContext>();
        await db.Database.EnsureCreatedAsync();

        var roles = provider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var role in new[] { UserRole.Applicant, UserRole.PropertyManager })
        {
            await roles.CreateAsync(new IdentityRole(role));
        }

        var owner = await CreateUserAsync(provider, OwnerEmail, "Ada", "Owner", UserRole.Applicant);
        await CreateUserAsync(provider, StrangerEmail, "Sam", "Stranger", UserRole.Applicant);
        await CreateUserAsync(provider, ManagerEmail, "Mel", "Manager", UserRole.PropertyManager);

        var unitType = new UnitType { Name = "Two bedroom" };
        var property = new Property
        {
            Name = "Canal Square",
            AddressLine1 = "1 Canal Square",
            City = "Troy",
            State = "NY",
            PostalCode = "12180"
        };

        var unit = new Unit
        {
            Property = property,
            UnitType = unitType,
            UnitNumber = "2A",
            Bedrooms = 2,
            MonthlyRent = 1450m
        };

        var application = new RentalApplication
        {
            Unit = unit,
            Status = ApplicationStatus.Draft,
            CreatedAtUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),

            // Applicant information is filled in and marked saved, because the wizard only lets a
            // reader reach the residence section once it has been. A draft with nothing in it
            // cannot show that section at all, so tests about it would silently examine the
            // Summary instead and pass without asserting anything.
            ApplicantInformation = new ApplicantInformation
            {
                FirstName = "Ada",
                LastName = "Owner",
                Phone = "555-0100",
                Email = OwnerEmail,
                AddressLine1 = "12 Second Street",
                City = "Troy",
                State = "NY",
                PostalCode = "12180"
            },
            ApplicantInformationSavedAtUtc = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
            Residences =
            {
                new Residence
                {
                    AddressLine1 = "12 Second Street",
                    City = "Troy",
                    State = "NY",
                    PostalCode = "12180",
                    LandlordName = "Ida Vance",
                    LandlordPhone = "555-0188",
                    MoveInDate = new DateOnly(2023, 5, 1)
                }
            },
            Applicants =
            {
                new RentalApplicationApplicant { ApplicantUserId = owner.Id }
            }
        };

        db.UnitTypes.Add(unitType);
        db.Properties.Add(property);
        db.Units.Add(unit);
        db.RentalApplications.Add(application);
        await db.SaveChangesAsync();

        ApplicationId = application.Id;
        UnitId = unit.Id;
        ResidenceId = application.Residences.First().Id;
    }

    private static async Task<ApplicationUser> CreateUserAsync(
        IServiceProvider provider,
        string email,
        string firstName,
        string lastName,
        string role)
    {
        var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FirstName = firstName,
            LastName = lastName
        };

        var created = await users.CreateAsync(user, Password);

        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));

        var assigned = await users.AddToRoleAsync(user, role);

        Assert.True(assigned.Succeeded, string.Join("; ", assigned.Errors.Select(error => error.Description)));

        return user;
    }

    /// <summary>
    /// A client that has signed in the way a person does: a GET of the login form, the antiforgery
    /// token taken from that form, and a POST of the real credentials. Forging a principal instead
    /// would skip the half of the pipeline most worth testing.
    /// </summary>
    public async Task<HttpClient> SignInAsync(string email)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var form = await client.GetStringAsync("/Account/Login");

        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["Email"] = email,
                ["Password"] = Password,
                ["RememberMe"] = "false",
                ["__RequestVerificationToken"] = AntiforgeryToken(form)
            }));

        // A failed sign-in re-renders the form with a 200, so the redirect is the assertion that
        // the cookie was actually issued. Without it every later test would fail as unauthorised
        // and look like an authorisation bug.
        Assert.Equal(System.Net.HttpStatusCode.Redirect, response.StatusCode);

        return client;
    }

    /// <summary>An anonymous client that does not chase the redirect to the login page.</summary>
    public HttpClient SignedOut() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string AntiforgeryToken(string html)
    {
        const string Marker = "name=\"__RequestVerificationToken\"";

        var field = html.IndexOf(Marker, StringComparison.Ordinal);

        Assert.True(field >= 0, "The login form did not render an antiforgery field.");

        var value = html.IndexOf("value=\"", field, StringComparison.Ordinal) + "value=\"".Length;
        var end = html.IndexOf('"', value);

        return html[value..end];
    }

    public override async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
