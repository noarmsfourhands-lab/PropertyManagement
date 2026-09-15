using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Infrastructure.Identity;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Infrastructure.Tests;

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

    /// <summary>
    /// The four well-known accounts, created once per database and safe to call again.
    ///
    /// These have to exist. The applicant link and the claim column both carry real foreign keys to
    /// the accounts table, because both are read as live authority: an id pointing at nothing would
    /// leave an application nobody can open, edit or repair. That is the behaviour under test, so
    /// the fixtures set up rows that satisfy it rather than working around it.
    /// </summary>
    public static async Task EnsureAccountsAsync(PropertyManagementDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);

        foreach (var (id, first, last) in new[]
                 {
                     (Applicant, "Robin", "Alvarez"),
                     (OtherApplicant, "Sam", "Okafor"),
                     (Manager, "Mel", "Brandt"),
                     (OtherManager, "Nima", "Rossi")
                 })
        {
            if (!await db.Users.AnyAsync(user => user.Id == id))
            {
                await AddAccountAsync(db, id, first, last, $"{id}@example.test");
            }
        }
    }

    /// <summary>
    /// An account row for one of the ids above. Starting an application also copies the account's
    /// name and contact details onto the new application, so what is here is what ends up there.
    /// </summary>
    public static async Task<ApplicationUser> AddAccountAsync(
        PropertyManagementDbContext db,
        string userId,
        string firstName = "Robin",
        string lastName = "Alvarez",
        string email = "robin@example.test",
        string? phone = "555-0170")
    {
        var user = new ApplicationUser
        {
            Id = userId,
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            FirstName = firstName,
            LastName = lastName,
            PhoneNumber = phone,
            SecurityStamp = Guid.NewGuid().ToString()
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user;
    }

    /// <summary>One property with two units, so a test can lease one and still have another free.</summary>
    public static async Task<Property> AddPropertyWithUnitsAsync(PropertyManagementDbContext db)
    {
        await EnsureAccountsAsync(db);

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
        await EnsureAccountsAsync(db);

        // The names are recorded on the application the same way the service records them, so a
        // fixture-built application describes itself exactly like a real one does.
        var unit = await db.Units
            .Include(entity => entity.Property)
            .FirstAsync(entity => entity.Id == unitId);

        var application = new RentalApplication
        {
            UnitId = unitId,
            Status = Domain.Enums.ApplicationStatus.Draft,
            CreatedAtUtc = Now.AddDays(-2),
            PropertyName = unit.Property?.Name ?? string.Empty,
            UnitNumber = unit.UnitNumber,
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
