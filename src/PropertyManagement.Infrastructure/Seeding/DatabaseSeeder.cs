using Bogus;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Infrastructure.Identity;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Infrastructure.Seeding;

/// <summary>
/// Applies migrations and fills an empty database with demo data.
///
/// Every step is idempotent: it looks for what it is about to create and skips the work when the
/// rows are already there, so the app can be restarted any number of times without duplicating
/// data or failing. Bogus runs from a fixed seed, so a rebuilt database comes back identical.
/// </summary>
public class DatabaseSeeder(
    PropertyManagementDbContext db,
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    TimeProvider timeProvider,
    IOptions<SeedOptions> options,
    ILogger<DatabaseSeeder> logger)
{
    private readonly SeedOptions _options = options.Value;

    /// <summary>Active unit types, plus one retired type to exercise the inactive-lookup rule.</summary>
    private static readonly (string Name, bool IsActive)[] UnitTypeSeed =
    [
        ("Studio", true),
        ("One Bedroom", true),
        ("Two Bedroom", true),
        ("Three Bedroom", true),
        ("Loft", true),
        ("Garden Flat", false)
    ];

    /// <summary>Brings the schema up to date, then fills it.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);
        await SeedDataAsync(cancellationToken);
    }

    /// <summary>
    /// The data half on its own, separate from migrating, because the two are different concerns:
    /// one changes the shape of the database and the other its contents. Keeping them apart also
    /// lets the idempotency of the seeding be tested against a schema created any other way.
    /// </summary>
    public async Task SeedDataAsync(CancellationToken cancellationToken = default)
    {
        // The switch guards the writing, not the migrating: a real deployment still wants its
        // schema brought up to date without demo data appearing in it.
        if (!_options.Enabled)
        {
            logger.LogInformation("Seeding is disabled; no demo data was written.");
            return;
        }

        Randomizer.Seed = new Random(_options.RandomSeed);

        await SeedRolesAsync();

        var managers = await SeedUsersAsync(UserRole.PropertyManager, _options.PropertyManagerCount, "manager");
        var applicants = await SeedUsersAsync(UserRole.Applicant, _options.ApplicantCount, "applicant");

        await SeedUnitTypesAsync(cancellationToken);
        await SeedPropertiesAndUnitsAsync(cancellationToken);
        await SeedApplicationsAsync(applicants, managers, cancellationToken);

        logger.LogInformation("Database seeding complete.");
    }

    private async Task SeedRolesAsync()
    {
        foreach (var role in UserRole.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
                logger.LogInformation("Created role {Role}.", role);
            }
        }
    }

    /// <summary>
    /// Creates accounts with predictable addresses (manager1@example.com, applicant1@example.com and
    /// so on) so the README can hand a reviewer a working login, while names come from Bogus.
    /// </summary>
    private async Task<IReadOnlyList<ApplicationUser>> SeedUsersAsync(string role, int count, string prefix)
    {
        var faker = new Faker();
        var users = new List<ApplicationUser>(count);

        for (var index = 1; index <= count; index++)
        {
            var email = $"{prefix}{index}@example.com";
            var existing = await userManager.FindByEmailAsync(email);

            if (existing is not null)
            {
                users.Add(existing);
                continue;
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = faker.Name.FirstName(),
                LastName = faker.Name.LastName(),
                PhoneNumber = faker.Phone.PhoneNumber("###-###-####")
            };

            var created = await userManager.CreateAsync(user, _options.DemoPassword);

            if (!created.Succeeded)
            {
                var errors = string.Join("; ", created.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Could not create the seeded user {email}: {errors}");
            }

            var assigned = await userManager.AddToRoleAsync(user, role);

            if (!assigned.Succeeded)
            {
                var roleErrors = string.Join("; ", assigned.Errors.Select(error => error.Description));
                throw new InvalidOperationException($"Could not put {email} in the {role} role: {roleErrors}");
            }

            users.Add(user);
            logger.LogInformation("Created {Role} {Email}.", role, email);
        }

        return users;
    }

    /// <summary>
    /// How many bedrooms a type implies. A loft is one open room, so it counts as a studio does.
    /// </summary>
    private static int BedroomsFor(string unitTypeName) => unitTypeName switch
    {
        "Studio" => 0,
        "Loft" => 0,
        "One Bedroom" => 1,
        "Two Bedroom" => 2,
        "Three Bedroom" => 3,
        _ => 1
    };

    private async Task SeedUnitTypesAsync(CancellationToken cancellationToken)
    {
        var existing = await db.UnitTypes
            .Select(unitType => unitType.Name)
            .ToListAsync(cancellationToken);

        var missing = UnitTypeSeed
            .Where(seed => !existing.Contains(seed.Name))
            .Select(seed => new UnitType { Name = seed.Name, IsActive = seed.IsActive })
            .ToList();

        if (missing.Count == 0)
        {
            return;
        }

        db.UnitTypes.AddRange(missing);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} unit types.", missing.Count);
    }

    private async Task SeedPropertiesAndUnitsAsync(CancellationToken cancellationToken)
    {
        if (await db.Properties.AnyAsync(cancellationToken))
        {
            return;
        }

        // Only active types are assigned, matching the rule the UI enforces for new units.
        var selectableTypes = await db.UnitTypes
            .Where(unitType => unitType.IsActive)
            .Select(unitType => new { unitType.Id, unitType.Name })
            .ToListAsync(cancellationToken);

        if (selectableTypes.Count == 0)
        {
            throw new InvalidOperationException("Unit types must be seeded before properties.");
        }

        var unitNumber = 0;

        // The type is picked first and the bedroom count follows from it. Drawing the two
        // independently produced units labelled "Two Bedroom" with nought bedrooms, which reads as
        // a bug on every screen that shows both and undermines the rest of the demo data.
        var unitFaker = new Faker<Unit>()
            .CustomInstantiator(faker =>
            {
                var type = faker.PickRandom(selectableTypes);

                return new Unit
                {
                    UnitTypeId = type.Id,
                    Bedrooms = BedroomsFor(type.Name)
                };
            })
            .RuleFor(unit => unit.UnitNumber, faker => $"{faker.Random.Int(1, 4)}{++unitNumber:D2}")
            .RuleFor(unit => unit.MonthlyRent, faker => Math.Round(faker.Random.Decimal(950, 4200), 2));

        var propertyFaker = new Faker<Property>()
            .RuleFor(property => property.Name, faker => $"{faker.Address.StreetName()} {faker.PickRandom("Court", "Residences", "Commons", "Place")}")
            .RuleFor(property => property.AddressLine1, faker => faker.Address.StreetAddress())
            .RuleFor(property => property.City, faker => faker.Address.City())
            .RuleFor(property => property.State, faker => faker.Address.StateAbbr())
            .RuleFor(property => property.PostalCode, faker => faker.Address.ZipCode("#####"));

        var properties = propertyFaker.Generate(_options.PropertyCount);
        var random = new Randomizer();

        foreach (var property in properties)
        {
            unitNumber = 0;
            var count = random.Int(_options.MinUnitsPerProperty, _options.MaxUnitsPerProperty);
            property.Units = unitFaker.Generate(count);
        }

        db.Properties.AddRange(properties);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded {Properties} properties with {Units} units.",
            properties.Count,
            properties.Sum(property => property.Units.Count));
    }

    /// <summary>
    /// Creates applications in every status. Each one gets its own unit so that the approved
    /// applications can hold a live lease without making another seeded unit unavailable.
    /// </summary>
    private async Task SeedApplicationsAsync(
        IReadOnlyList<ApplicationUser> applicants,
        IReadOnlyList<ApplicationUser> managers,
        CancellationToken cancellationToken)
    {
        if (await db.RentalApplications.AnyAsync(cancellationToken))
        {
            return;
        }

        if (applicants.Count == 0 || managers.Count == 0)
        {
            throw new InvalidOperationException("Users must be seeded before applications.");
        }

        var statuses = Enum.GetValues<ApplicationStatus>();
        var required = statuses.Length * _options.ApplicationsPerStatus;

        // The property comes with them: an application and a lease each record the names they were
        // created under, and those have to be read from somewhere to be recorded.
        var units = await db.Units
            .Include(unit => unit.Property)
            .OrderBy(unit => unit.Id)
            .Take(required)
            .ToListAsync(cancellationToken);

        if (units.Count < required)
        {
            throw new InvalidOperationException(
                $"Seeding needs {required} units to cover every status but found {units.Count}.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        var faker = new Faker();
        var cursor = 0;

        foreach (var status in statuses)
        {
            for (var copy = 0; copy < _options.ApplicationsPerStatus; copy++)
            {
                var unit = units[cursor];
                var applicant = applicants[cursor % applicants.Count];
                var manager = managers[cursor % managers.Count];
                cursor++;

                var application = BuildApplication(unit, applicant, status, now, faker);
                AddHistory(application, applicant, manager, status, now, faker);

                if (status == ApplicationStatus.Approved)
                {
                    // A live lease starting last month, so the unit reads as unavailable today.
                    var start = today.AddMonths(-1);
                    application.Lease = new Lease
                    {
                        UnitId = unit.Id,
                        StartDate = start,
                        EndDate = LeaseTerm.EndDateFor(start),
                        MonthlyRent = unit.MonthlyRent,
                        PropertyName = unit.Property?.Name ?? string.Empty,
                        UnitNumber = unit.UnitNumber
                    };
                }

                db.RentalApplications.Add(application);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} applications across {Statuses} statuses.", cursor, statuses.Length);
    }

    private RentalApplication BuildApplication(
        Unit unit,
        ApplicationUser applicant,
        ApplicationStatus status,
        DateTime now,
        Faker faker)
    {
        var createdAt = now.AddDays(-faker.Random.Int(10, 90));

        // A brand-new draft has saved nothing yet; every other status has been through both sections.
        var isUntouchedDraft = status == ApplicationStatus.Draft;

        var application = new RentalApplication
        {
            UnitId = unit.Id,
            Status = status,
            CreatedAtUtc = createdAt,

            // Recorded on the application the same way the real path records it.
            PropertyName = unit.Property?.Name ?? string.Empty,
            UnitNumber = unit.UnitNumber,
            ApplicantInformation = new ApplicantInformation
            {
                FirstName = applicant.FirstName,
                LastName = applicant.LastName,
                Phone = faker.Phone.PhoneNumber("###-###-####"),
                Email = applicant.Email,
                AddressLine1 = faker.Address.StreetAddress(),
                City = faker.Address.City(),
                State = faker.Address.StateAbbr(),
                PostalCode = faker.Address.ZipCode("#####")
            },
            ApplicantInformationSavedAtUtc = isUntouchedDraft ? null : createdAt.AddHours(1),
            ResidenceHistorySavedAtUtc = isUntouchedDraft ? null : createdAt.AddHours(2),
            Applicants =
            [
                new RentalApplicationApplicant
                {
                    ApplicantUserId = applicant.Id,
                    IsPrimary = true,
                    AddedAtUtc = createdAt
                }
            ]
        };

        if (!isUntouchedDraft)
        {
            application.SubmittedAtUtc = createdAt.AddHours(3);
            application.Residences = BuildResidences(faker, createdAt);
        }

        if (status is ApplicationStatus.Approved or ApplicationStatus.Denied or ApplicationStatus.Withdrawn)
        {
            application.DecidedAtUtc = createdAt.AddDays(faker.Random.Int(1, 5));
        }

        if (status == ApplicationStatus.UnderReview)
        {
            application.ClaimedAtUtc = createdAt.AddDays(1);
        }

        return application;
    }

    private static List<Residence> BuildResidences(Faker faker, DateTime createdAt)
    {
        var count = faker.Random.Int(1, 3);
        var residences = new List<Residence>(count);
        var moveOut = DateOnly.FromDateTime(createdAt).AddYears(-1);

        for (var index = 0; index < count; index++)
        {
            var moveIn = moveOut.AddYears(-faker.Random.Int(1, 3));

            residences.Add(new Residence
            {
                AddressLine1 = faker.Address.StreetAddress(),
                City = faker.Address.City(),
                State = faker.Address.StateAbbr(),
                PostalCode = faker.Address.ZipCode("#####"),
                LandlordName = faker.Name.FullName(),
                LandlordPhone = faker.Phone.PhoneNumber("###-###-####"),
                MoveInDate = moveIn,
                MoveOutDate = moveOut
            });

            moveOut = moveIn.AddDays(-1);
        }

        return residences;
    }

    /// <summary>
    /// Writes an audit trail that matches how the application actually reached its status, so the
    /// history panel has something realistic to render for every seeded row.
    /// </summary>
    private void AddHistory(
        RentalApplication application,
        ApplicationUser applicant,
        ApplicationUser manager,
        ApplicationStatus status,
        DateTime now,
        Faker faker)
    {
        if (status == ApplicationStatus.Draft)
        {
            return;
        }

        var submittedAt = application.SubmittedAtUtc ?? now;

        application.Events.Add(new ApplicationEvent
        {
            FromStatus = ApplicationStatus.Draft,
            ToStatus = ApplicationStatus.Submitted,
            ActorUserId = applicant.Id,
            ActorName = applicant.DisplayName,
            OccurredAtUtc = submittedAt
        });

        if (status == ApplicationStatus.Submitted)
        {
            return;
        }

        var decidedAt = application.DecidedAtUtc ?? application.ClaimedAtUtc ?? submittedAt.AddDays(1);

        switch (status)
        {
            case ApplicationStatus.UnderReview:
                application.ClaimedByUserId = manager.Id;
                application.Events.Add(new ApplicationEvent
                {
                    FromStatus = ApplicationStatus.Submitted,
                    ToStatus = ApplicationStatus.UnderReview,
                    ActorUserId = manager.Id,
                    ActorName = manager.DisplayName,
                    Comment = "Claimed from the review queue.",
                    OccurredAtUtc = decidedAt
                });
                break;

            case ApplicationStatus.Withdrawn:
                application.Events.Add(new ApplicationEvent
                {
                    FromStatus = ApplicationStatus.Submitted,
                    ToStatus = ApplicationStatus.Withdrawn,
                    ActorUserId = applicant.Id,
                    ActorName = applicant.DisplayName,
                    Comment = "Withdrawn by the applicant.",
                    OccurredAtUtc = decidedAt
                });
                break;

            case ApplicationStatus.Returned:
            case ApplicationStatus.Approved:
            case ApplicationStatus.Denied:
                var outcome = status switch
                {
                    ApplicationStatus.Returned => ReviewOutcome.Return,
                    ApplicationStatus.Approved => ReviewOutcome.Approve,
                    _ => ReviewOutcome.Deny
                };

                application.Events.Add(new ApplicationEvent
                {
                    FromStatus = ApplicationStatus.Submitted,
                    ToStatus = status,
                    Outcome = outcome,
                    ActorUserId = manager.Id,
                    ActorName = manager.DisplayName,
                    Comment = ReviewRules.RequiresComment(outcome)
                        ? faker.PickRandom(
                            "Residence history is missing a landlord phone number.",
                            "Move-out date overlaps the previous address.",
                            "Reported income does not meet the rent threshold.")
                        : null,
                    OccurredAtUtc = decidedAt
                });
                break;
        }
    }
}
