using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Infrastructure.Services;

namespace PropertyManagement.Infrastructure.Tests;

public class RentalApplicationServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private RentalApplicationService ServiceOver(Persistence.PropertyManagementDbContext db) =>
        new(db, new FixedTimeProvider(TestData.Now));

    [Fact]
    public async Task The_model_produces_a_working_schema()
    {
        await using var db = _database.CreateContext();

        // EnsureCreated in the fixture already built the schema; this proves every set is queryable,
        // which is what catches a mapping that compiles but cannot be read back.
        Assert.Empty(await db.Properties.ToListAsync());
        Assert.Empty(await db.RentalApplications.ToListAsync());
        Assert.Empty(await db.Leases.ToListAsync());
        Assert.Empty(await db.ApplicationEvents.ToListAsync());
        Assert.Empty(await db.Users.ToListAsync());
    }

    [Fact]
    public async Task Starting_an_application_creates_a_draft_owned_by_the_applicant()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var unitId = property.Units.First().Id;

        var result = await ServiceOver(db).StartAsync(
            unitId,
            new Actor(TestData.Applicant, "Robin Alvarez"),
            TestData.Today);

        Assert.True(result.Succeeded);

        var application = await db.RentalApplications
            .Include(entity => entity.Applicants)
            .FirstAsync(entity => entity.Id == result.Value);

        Assert.Equal(ApplicationStatus.Draft, application.Status);
        Assert.Equal(unitId, application.UnitId);
        Assert.Equal(TestData.Applicant, application.Applicants.Single().ApplicantUserId);
        Assert.True(application.Applicants.Single().IsPrimary);
    }

    [Fact]
    public async Task Starting_twice_for_the_same_unit_returns_the_application_already_open()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var unitId = property.Units.First().Id;
        var service = ServiceOver(db);
        var actor = new Actor(TestData.Applicant, "Robin Alvarez");

        var first = await service.StartAsync(unitId, actor, TestData.Today);
        var second = await service.StartAsync(unitId, actor, TestData.Today);

        Assert.Equal(first.Value, second.Value);
        Assert.Equal(1, await db.RentalApplications.CountAsync());
    }

    [Fact]
    public async Task An_application_cannot_be_started_for_a_unit_that_is_leased()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var unit = property.Units.First();

        db.Leases.Add(new Domain.Entities.Lease
        {
            UnitId = unit.Id,
            RentalApplication = (await TestData.AddCompleteDraftAsync(db, property.Units.Last().Id)),
            StartDate = TestData.Today.AddMonths(-1),
            EndDate = LeaseTerm.EndDateFor(TestData.Today.AddMonths(-1)),
            MonthlyRent = unit.MonthlyRent
        });
        await db.SaveChangesAsync();

        var result = await ServiceOver(db).StartAsync(
            unit.Id,
            new Actor(TestData.OtherApplicant, "Sam Reyes"),
            TestData.Today);

        Assert.True(result.Failed);
        Assert.Contains("leased", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Saving_applicant_information_stores_it_and_marks_the_section_saved()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);

        var started = await ServiceOver(db).StartAsync(
            property.Units.First().Id,
            new Actor(TestData.Applicant, "Robin Alvarez"),
            TestData.Today);

        var application = await db.RentalApplications.FirstAsync(entity => entity.Id == started.Value);
        Assert.False(application.ApplicantInformationSaved);

        var result = await ServiceOver(db).SaveApplicantInformationAsync(
            new ApplicantInformationInput(
                application.Id,
                application.ApplicantInformationVersion,
                "Robin", "Alvarez", "555-0100", "robin@example.com",
                "4 Cedar Lane", null, "Portland", "OR", "97202"),
            TestData.Applicant);

        Assert.True(result.Succeeded);

        await using var check = _database.CreateContext();
        var saved = await check.RentalApplications.FirstAsync(entity => entity.Id == application.Id);

        Assert.True(saved.ApplicantInformationSaved);
        Assert.Equal("Robin", saved.ApplicantInformation.FirstName);
        Assert.Equal("Portland", saved.ApplicantInformation.City);
    }

    [Fact]
    public async Task A_user_who_is_not_on_the_application_cannot_save_it()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var application = await TestData.AddCompleteDraftAsync(db, property.Units.First().Id);

        var result = await ServiceOver(db).SaveApplicantInformationAsync(
            new ApplicantInformationInput(
                application.Id, application.ApplicantInformationVersion,
                "Mallory", "Stone", "555-0999", "mallory@example.com",
                "1 Elsewhere", null, "Portland", "OR", "97203"),
            TestData.OtherApplicant);

        Assert.True(result.Failed);
    }

    [Fact]
    public async Task A_save_built_on_a_stale_copy_of_a_section_is_rejected()
    {
        await using var setup = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(setup);
        var application = await TestData.AddCompleteDraftAsync(setup, property.Units.First().Id);
        var originalVersion = application.ApplicantInformationVersion;

        // Two people opened the page, so both are holding the same version.
        await using var firstSession = _database.CreateContext();
        await using var secondSession = _database.CreateContext();

        var firstSave = await ServiceOver(firstSession).SaveApplicantInformationAsync(
            new ApplicantInformationInput(
                application.Id, originalVersion,
                "Robin", "Alvarez", "555-0100", "robin@example.com",
                "First wins", null, "Portland", "OR", "97202"),
            TestData.Applicant);

        var secondSave = await ServiceOver(secondSession).SaveApplicantInformationAsync(
            new ApplicantInformationInput(
                application.Id, originalVersion,
                "Robin", "Alvarez", "555-0100", "robin@example.com",
                "Second overwrites", null, "Portland", "OR", "97202"),
            TestData.Applicant);

        Assert.True(firstSave.Succeeded);
        Assert.True(secondSave.Failed);
        Assert.Contains("Reload", secondSave.Error!, StringComparison.OrdinalIgnoreCase);

        // The first person's work survived rather than being silently replaced.
        await using var check = _database.CreateContext();
        var stored = await check.RentalApplications.FirstAsync(entity => entity.Id == application.Id);
        Assert.Equal("First wins", stored.ApplicantInformation.AddressLine1);
    }

    [Fact]
    public async Task Saves_to_different_sections_do_not_collide()
    {
        await using var setup = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(setup);
        var application = await TestData.AddCompleteDraftAsync(setup, property.Units.First().Id);
        var informationVersion = application.ApplicantInformationVersion;
        var historyVersion = application.ResidenceHistoryVersion;

        await using var firstSession = _database.CreateContext();
        await using var secondSession = _database.CreateContext();

        var information = await ServiceOver(firstSession).SaveApplicantInformationAsync(
            new ApplicantInformationInput(
                application.Id, informationVersion,
                "Robin", "Alvarez", "555-0100", "robin@example.com",
                "4 Cedar Lane", null, "Portland", "OR", "97202"),
            TestData.Applicant);

        var history = await ServiceOver(secondSession).SaveResidenceHistoryAsync(
            application.Id,
            historyVersion,
            TestData.Applicant);

        // Each section has its own token, so neither save invalidates the other.
        Assert.True(information.Succeeded);
        Assert.True(history.Succeeded);
    }

    [Fact]
    public async Task Residence_history_cannot_be_completed_while_it_is_empty()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);

        var started = await ServiceOver(db).StartAsync(
            property.Units.First().Id,
            new Actor(TestData.Applicant, "Robin Alvarez"),
            TestData.Today);

        var application = await db.RentalApplications.FirstAsync(entity => entity.Id == started.Value);

        var result = await ServiceOver(db).SaveResidenceHistoryAsync(
            application.Id,
            application.ResidenceHistoryVersion,
            TestData.Applicant);

        Assert.True(result.Failed);
        Assert.Contains("at least one", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_residence_with_dates_in_the_wrong_order_is_rejected()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var application = await TestData.AddCompleteDraftAsync(db, property.Units.First().Id);

        var result = await ServiceOver(db).SaveResidenceAsync(
            new ResidenceInput(
                0, application.Id, "3 Maple Way", null, "Eugene", "OR", "97401",
                "Jo Marsh", "555-0122",
                new DateOnly(2024, 6, 1),
                new DateOnly(2024, 1, 1)),
            TestData.Applicant,
            TestData.Today);

        Assert.True(result.Failed);
        Assert.Contains("Move-out", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submitting_moves_the_application_and_records_who_did_it()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var application = await TestData.AddCompleteDraftAsync(db, property.Units.First().Id);

        var result = await ServiceOver(db).SubmitAsync(
            application.Id,
            new Actor(TestData.Applicant, "Robin Alvarez"),
            TestData.Today);

        Assert.True(result.Succeeded);

        await using var check = _database.CreateContext();
        var stored = await check.RentalApplications
            .Include(entity => entity.Events)
            .FirstAsync(entity => entity.Id == application.Id);

        Assert.Equal(ApplicationStatus.Submitted, stored.Status);
        Assert.Equal(TestData.Now, stored.SubmittedAtUtc);

        var entry = stored.Events.Single();
        Assert.Equal(ApplicationStatus.Draft, entry.FromStatus);
        Assert.Equal(ApplicationStatus.Submitted, entry.ToStatus);
        Assert.Equal("Robin Alvarez", entry.ActorName);
    }

    [Fact]
    public async Task Submitting_is_refused_once_the_unit_has_been_leased()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var unit = property.Units.First();

        var winner = await TestData.AddCompleteDraftAsync(db, unit.Id, TestData.OtherApplicant);
        var loser = await TestData.AddCompleteDraftAsync(db, unit.Id);

        db.Leases.Add(new Domain.Entities.Lease
        {
            UnitId = unit.Id,
            RentalApplicationId = winner.Id,
            StartDate = TestData.Today.AddMonths(-1),
            EndDate = LeaseTerm.EndDateFor(TestData.Today.AddMonths(-1)),
            MonthlyRent = unit.MonthlyRent
        });
        await db.SaveChangesAsync();

        var result = await ServiceOver(db).SubmitAsync(
            loser.Id,
            new Actor(TestData.Applicant, "Robin Alvarez"),
            TestData.Today);

        Assert.True(result.Failed);
        Assert.Contains("leased", result.Error!, StringComparison.OrdinalIgnoreCase);

        // The other open application is left exactly as it was.
        await using var check = _database.CreateContext();
        var untouched = await check.RentalApplications.FirstAsync(entity => entity.Id == loser.Id);
        Assert.Equal(ApplicationStatus.Draft, untouched.Status);
    }

    [Fact]
    public async Task A_submitted_application_can_no_longer_be_edited()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var application = await TestData.AddCompleteDraftAsync(db, property.Units.First().Id);

        await ServiceOver(db).SubmitAsync(
            application.Id,
            new Actor(TestData.Applicant, "Robin Alvarez"),
            TestData.Today);

        await using var second = _database.CreateContext();
        var stored = await second.RentalApplications.FirstAsync(entity => entity.Id == application.Id);

        var result = await ServiceOver(second).SaveApplicantInformationAsync(
            new ApplicantInformationInput(
                application.Id, stored.ApplicantInformationVersion,
                "Changed", "Name", "555-0100", "robin@example.com",
                "4 Cedar Lane", null, "Portland", "OR", "97202"),
            TestData.Applicant);

        Assert.True(result.Failed);
        Assert.Contains("Submitted", result.Error!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Withdrawing_is_terminal_and_leaves_a_trail()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var application = await TestData.AddCompleteDraftAsync(db, property.Units.First().Id);

        var result = await ServiceOver(db).WithdrawAsync(
            application.Id,
            new Actor(TestData.Applicant, "Robin Alvarez"));

        Assert.True(result.Succeeded);

        await using var check = _database.CreateContext();
        var stored = await check.RentalApplications
            .Include(entity => entity.Events)
            .FirstAsync(entity => entity.Id == application.Id);

        Assert.Equal(ApplicationStatus.Withdrawn, stored.Status);
        Assert.Single(stored.Events);

        // Terminal really is terminal.
        var again = await ServiceOver(check).WithdrawAsync(
            application.Id,
            new Actor(TestData.Applicant, "Robin Alvarez"));

        Assert.True(again.Failed);
    }

    [Fact]
    public async Task The_list_filters_by_status_and_property_in_the_database()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);

        var draft = await TestData.AddCompleteDraftAsync(db, property.Units.First().Id);
        var submitted = await TestData.AddCompleteDraftAsync(db, property.Units.Last().Id);

        await ServiceOver(db).SubmitAsync(
            submitted.Id,
            new Actor(TestData.Applicant, "Robin Alvarez"),
            TestData.Today);

        var service = ServiceOver(db);

        var all = await service.ListAsync(new ApplicationListFilter(), null);
        Assert.Equal(2, all.TotalCount);

        var drafts = await service.ListAsync(new ApplicationListFilter(ApplicationStatus.Draft), null);
        Assert.Equal(1, drafts.TotalCount);
        Assert.Equal(draft.Id, drafts.Rows.Single().Id);

        var byProperty = await service.ListAsync(new ApplicationListFilter(PropertyId: property.Id), null);
        Assert.Equal(2, byProperty.TotalCount);

        var otherProperty = await service.ListAsync(new ApplicationListFilter(PropertyId: property.Id + 999), null);
        Assert.Equal(0, otherProperty.TotalCount);
    }

    [Fact]
    public async Task An_applicant_sees_only_the_applications_they_are_on()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);

        await TestData.AddCompleteDraftAsync(db, property.Units.First().Id, TestData.Applicant);
        await TestData.AddCompleteDraftAsync(db, property.Units.Last().Id, TestData.OtherApplicant);

        var service = ServiceOver(db);

        var mine = await service.ListAsync(new ApplicationListFilter(), TestData.Applicant);
        var everyone = await service.ListAsync(new ApplicationListFilter(), null);

        Assert.Equal(1, mine.TotalCount);
        Assert.Equal(2, everyone.TotalCount);
    }

    [Fact]
    public async Task The_list_carries_the_property_and_unit_each_application_is_for()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        await TestData.AddCompleteDraftAsync(db, property.Units.First().Id);

        var page = await ServiceOver(db).ListAsync(new ApplicationListFilter(), null);
        var row = page.Rows.Single();

        Assert.Equal("Alder Court", row.PropertyName);
        Assert.Equal("101", row.UnitNumber);
        Assert.Equal("Robin Alvarez", row.ApplicantName);
    }

    [Fact]
    public async Task The_list_pages_in_the_database()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);

        await TestData.AddCompleteDraftAsync(db, property.Units.First().Id);
        await TestData.AddCompleteDraftAsync(db, property.Units.Last().Id);

        var firstPage = await ServiceOver(db).ListAsync(new ApplicationListFilter(Page: 1, PageSize: 1), null);

        Assert.Single(firstPage.Rows);
        Assert.Equal(2, firstPage.TotalCount);
        Assert.Equal(2, firstPage.PageCount);
        Assert.True(firstPage.HasNext);
        Assert.False(firstPage.HasPrevious);
    }
}

/// <summary>
/// Sorting is part of the list's contract, so it is checked against a real database rather than
/// assumed: an ordering that does not translate would otherwise only surface at runtime.
/// </summary>
public class ApplicationSortingTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private RentalApplicationService ServiceOver(Persistence.PropertyManagementDbContext db) =>
        new(db, new FixedTimeProvider(TestData.Now));

    /// <summary>Two properties, so ordering by property name has something to order.</summary>
    private async Task SeedAsync(Persistence.PropertyManagementDbContext db)
    {
        var first = await TestData.AddPropertyWithUnitsAsync(db);

        var second = new Domain.Entities.Property
        {
            Name = "Birch Commons",
            AddressLine1 = "2 Birch Way",
            City = "Portland",
            State = "OR",
            PostalCode = "97203",
            Units =
            [
                new Domain.Entities.Unit
                {
                    UnitNumber = "201",
                    Bedrooms = 1,
                    MonthlyRent = 1200m,
                    UnitTypeId = first.Units.First().UnitTypeId
                }
            ]
        };

        db.Properties.Add(second);
        await db.SaveChangesAsync();

        var alder = await TestData.AddCompleteDraftAsync(db, first.Units.First().Id);
        var birch = await TestData.AddCompleteDraftAsync(db, second.Units.First().Id, TestData.OtherApplicant);

        // Give them different names and submission times so every ordering is distinguishable.
        alder.ApplicantInformation.LastName = "Zeta";
        birch.ApplicantInformation.LastName = "Alpha";
        birch.Status = ApplicationStatus.Submitted;
        birch.SubmittedAtUtc = TestData.Now;
        alder.SubmittedAtUtc = TestData.Now.AddDays(-5);
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(ApplicationSort.Property, false, "Alder Court")]
    [InlineData(ApplicationSort.Property, true, "Birch Commons")]
    [InlineData(ApplicationSort.Unit, false, "Alder Court")]
    [InlineData(ApplicationSort.Unit, true, "Birch Commons")]
    [InlineData(ApplicationSort.Applicant, false, "Birch Commons")]
    [InlineData(ApplicationSort.Applicant, true, "Alder Court")]
    [InlineData(ApplicationSort.Submitted, true, "Birch Commons")]
    [InlineData(ApplicationSort.Submitted, false, "Alder Court")]
    [InlineData(ApplicationSort.Status, false, "Alder Court")]
    public async Task Each_ordering_puts_the_expected_row_first(
        ApplicationSort sort,
        bool descending,
        string expectedProperty)
    {
        await using var db = _database.CreateContext();
        await SeedAsync(db);

        var page = await ServiceOver(db).ListAsync(
            new ApplicationListFilter(Sort: sort, Descending: descending),
            null);

        Assert.Equal(expectedProperty, page.Rows[0].PropertyName);
    }

    [Fact]
    public async Task Sorting_and_filtering_combine()
    {
        await using var db = _database.CreateContext();
        await SeedAsync(db);

        var page = await ServiceOver(db).ListAsync(
            new ApplicationListFilter(Status: ApplicationStatus.Submitted, Sort: ApplicationSort.Property),
            null);

        Assert.Single(page.Rows);
        Assert.Equal("Birch Commons", page.Rows[0].PropertyName);
    }

    [Fact]
    public async Task An_unrecognised_sort_falls_back_rather_than_throwing()
    {
        await using var db = _database.CreateContext();
        await SeedAsync(db);

        var page = await ServiceOver(db).ListAsync(
            new ApplicationListFilter(Sort: (ApplicationSort)99),
            null);

        Assert.Equal(2, page.TotalCount);
    }
}
