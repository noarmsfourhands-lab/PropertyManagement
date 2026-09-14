using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Infrastructure.Tests;

/// <summary>
/// A real relational database for one test, held in memory by SQLite.
///
/// These tests exist to check the things a rule test cannot: that the model actually produces a
/// schema, that the queries translate, and that the concurrency tokens do what they are configured
/// to do. SQLite is used because it enforces keys, unique indexes and concurrency the same way a
/// server does, while needing nothing installed. The application itself runs on SQL Server.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PropertyManagementDbContext> _options;

    public TestDatabase()
    {
        // The in-memory database lives exactly as long as this connection.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<PropertyManagementDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    /// <summary>
    /// A fresh context over the same database. Two of these stand in for two people working at
    /// once, each with their own change tracker.
    /// </summary>
    public PropertyManagementDbContext CreateContext() => new(_options);

    /// <summary>The open connection this database lives on, for tests that build their own container.</summary>
    public SqliteConnection Connection => _connection;

    public void Dispose() => _connection.Dispose();
}

/// <summary>A clock that does not move, so date-sensitive behaviour is decided by the test.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Builds the small amount of reference data most tests need.</summary>
public static class TestData
{
    public const string Applicant = "applicant-1";
    public const string OtherApplicant = "applicant-2";
    public const string Manager = "manager-1";
    public const string OtherManager = "manager-2";

    public static readonly DateTime Now = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateOnly Today = new(2026, 9, 14);

    /// <summary>One property with two units, so a test can lease one and still have another free.</summary>
    public static async Task<Property> AddPropertyWithUnitsAsync(PropertyManagementDbContext db)
    {
        var studio = new UnitType { Name = "Studio", IsActive = true };
        var retired = new UnitType { Name = "Garden Flat", IsActive = false };

        var property = new Property
        {
            Name = "Alder Court",
            AddressLine1 = "10 Alder Street",
            City = "Portland",
            State = "OR",
            PostalCode = "97201",
            Units =
            [
                new Unit { UnitNumber = "101", Bedrooms = 1, MonthlyRent = 1500m, UnitType = studio },
                new Unit { UnitNumber = "102", Bedrooms = 2, MonthlyRent = 1900m, UnitType = studio }
            ]
        };

        db.UnitTypes.AddRange(studio, retired);
        db.Properties.Add(property);
        await db.SaveChangesAsync();

        return property;
    }

    /// <summary>A draft with both sections already saved, ready to submit.</summary>
    public static async Task<RentalApplication> AddCompleteDraftAsync(
        PropertyManagementDbContext db,
        int unitId,
        string applicantUserId = Applicant)
    {
        var application = new RentalApplication
        {
            UnitId = unitId,
            Status = Domain.Enums.ApplicationStatus.Draft,
            CreatedAtUtc = Now.AddDays(-2),
            ApplicantInformationSavedAtUtc = Now.AddDays(-2),
            ResidenceHistorySavedAtUtc = Now.AddDays(-1),
            ApplicantInformation = new ApplicantInformation
            {
                FirstName = "Robin",
                LastName = "Alvarez",
                Phone = "555-0100",
                Email = "robin@example.com",
                AddressLine1 = "4 Cedar Lane",
                City = "Portland",
                State = "OR",
                PostalCode = "97202"
            },
            Applicants =
            [
                new RentalApplicationApplicant
                {
                    ApplicantUserId = applicantUserId,
                    IsPrimary = true,
                    AddedAtUtc = Now.AddDays(-2)
                }
            ],
            Residences =
            [
                new Residence
                {
                    AddressLine1 = "9 Birch Road",
                    City = "Salem",
                    State = "OR",
                    PostalCode = "97301",
                    LandlordName = "Dana Fields",
                    LandlordPhone = "555-0111",
                    MoveInDate = new DateOnly(2022, 1, 1),
                    MoveOutDate = new DateOnly(2025, 1, 1)
                }
            ]
        };

        db.RentalApplications.Add(application);
        await db.SaveChangesAsync();

        return application;
    }
}
