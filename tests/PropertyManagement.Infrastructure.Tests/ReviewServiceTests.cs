using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Infrastructure.Services;

namespace PropertyManagement.Infrastructure.Tests;

public class ReviewServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();

    private static readonly Actor TheManager = new(TestData.Manager, "Alex Chen");
    private static readonly Actor AnotherManager = new(TestData.OtherManager, "Jordan Poole");
    private static readonly Actor TheApplicant = new(TestData.Applicant, "Robin Alvarez");

    public void Dispose() => _database.Dispose();

    private ReviewService ReviewOver(Persistence.PropertyManagementDbContext db) =>
        new(db, new FixedTimeProvider(TestData.Now));

    private RentalApplicationService ApplicationsOver(Persistence.PropertyManagementDbContext db) =>
        new(db, new FixedTimeProvider(TestData.Now));

    /// <summary>A submitted application on its own unit, which is what review acts on.</summary>
    private async Task<(int ApplicationId, int UnitId)> SubmittedApplicationAsync(
        Persistence.PropertyManagementDbContext db,
        int unitIndex = 0)
    {
        var property = await db.Properties.Include(entity => entity.Units).FirstOrDefaultAsync()
            ?? await TestData.AddPropertyWithUnitsAsync(db);

        var unit = property.Units.OrderBy(entity => entity.UnitNumber).ElementAt(unitIndex);
        var application = await TestData.AddCompleteDraftAsync(db, unit.Id);

        await ApplicationsOver(db).SubmitAsync(application.Id, TheApplicant, TestData.Today);

        return (application.Id, unit.Id);
    }

    [Fact]
    public async Task Approving_issues_a_twelve_month_lease_for_the_unit()
    {
        await using var db = _database.CreateContext();
        var (applicationId, unitId) = await SubmittedApplicationAsync(db);

        var result = await ReviewOver(db).CompleteAsync(
            new ReviewInput(applicationId, ReviewOutcome.Approve, null),
            TheManager,
            TestData.Today);

        Assert.True(result.Succeeded);

        await using var check = _database.CreateContext();
        var lease = await check.Leases.SingleAsync();

        Assert.Equal(unitId, lease.UnitId);
        Assert.Equal(applicationId, lease.RentalApplicationId);
        Assert.Equal(TestData.Today, lease.StartDate);
        Assert.Equal(LeaseTerm.EndDateFor(TestData.Today), lease.EndDate);
        Assert.True(lease.CoversDate(TestData.Today));

        var application = await check.RentalApplications.FirstAsync(entity => entity.Id == applicationId);
        Assert.Equal(ApplicationStatus.Approved, application.Status);
        Assert.Equal(TestData.Now, application.DecidedAtUtc);
    }

    [Fact]
    public async Task An_approved_unit_stops_being_available()
    {
        await using var db = _database.CreateContext();
        var (applicationId, _) = await SubmittedApplicationAsync(db);

        var before = await new PropertyService(db).GetAvailableUnitsAsync(TestData.Today, 10);
        Assert.Equal(2, before.TotalCount);

        await ReviewOver(db).CompleteAsync(
            new ReviewInput(applicationId, ReviewOutcome.Approve, null),
            TheManager,
            TestData.Today);

        await using var check = _database.CreateContext();
        var after = await new PropertyService(check).GetAvailableUnitsAsync(TestData.Today, 10);

        Assert.Equal(1, after.TotalCount);
    }

    [Fact]
    public async Task A_second_application_for_a_leased_unit_cannot_be_approved()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var unit = property.Units.First();

        var first = await TestData.AddCompleteDraftAsync(db, unit.Id, TestData.Applicant);
        var second = await TestData.AddCompleteDraftAsync(db, unit.Id, TestData.OtherApplicant);

        var applications = ApplicationsOver(db);
        await applications.SubmitAsync(first.Id, TheApplicant, TestData.Today);
        await applications.SubmitAsync(second.Id, new Actor(TestData.OtherApplicant, "Sam Reyes"), TestData.Today);

        var review = ReviewOver(db);

        var approved = await review.CompleteAsync(
            new ReviewInput(first.Id, ReviewOutcome.Approve, null), TheManager, TestData.Today);

        var refused = await review.CompleteAsync(
            new ReviewInput(second.Id, ReviewOutcome.Approve, null), TheManager, TestData.Today);

        Assert.True(approved.Succeeded);
        Assert.True(refused.Failed);
        Assert.Contains("already has an active lease", refused.Error!, StringComparison.OrdinalIgnoreCase);

        // Exactly one lease exists, and the refused application is untouched rather than cascaded.
        await using var check = _database.CreateContext();
        Assert.Equal(1, await check.Leases.CountAsync());

        var untouched = await check.RentalApplications.FirstAsync(entity => entity.Id == second.Id);
        Assert.Equal(ApplicationStatus.Submitted, untouched.Status);
    }

    [Fact]
    public async Task Returning_without_a_comment_is_refused()
    {
        await using var db = _database.CreateContext();
        var (applicationId, _) = await SubmittedApplicationAsync(db);

        var result = await ReviewOver(db).CompleteAsync(
            new ReviewInput(applicationId, ReviewOutcome.Return, "   "),
            TheManager,
            TestData.Today);

        Assert.True(result.Failed);
        Assert.Contains("comment is required", result.Error!, StringComparison.OrdinalIgnoreCase);

        await using var check = _database.CreateContext();
        var unchanged = await check.RentalApplications.FirstAsync(entity => entity.Id == applicationId);
        Assert.Equal(ApplicationStatus.Submitted, unchanged.Status);
    }

    [Fact]
    public async Task Returning_with_a_comment_sends_the_application_back_for_correction()
    {
        await using var db = _database.CreateContext();
        var (applicationId, _) = await SubmittedApplicationAsync(db);

        var result = await ReviewOver(db).CompleteAsync(
            new ReviewInput(applicationId, ReviewOutcome.Return, "Landlord phone is missing."),
            TheManager,
            TestData.Today);

        Assert.True(result.Succeeded);

        await using var check = _database.CreateContext();
        var application = await check.RentalApplications
            .Include(entity => entity.Events)
            .FirstAsync(entity => entity.Id == applicationId);

        Assert.Equal(ApplicationStatus.Returned, application.Status);
        Assert.Null(application.DecidedAtUtc);

        var entry = application.Events.OrderByDescending(item => item.Id).First();
        Assert.Equal(ReviewOutcome.Return, entry.Outcome);
        Assert.Equal("Landlord phone is missing.", entry.Comment);
        Assert.Equal("Alex Chen", entry.ActorName);

        // A returned application is editable again, which is what lets it be corrected and resubmitted.
        Assert.True(ApplicationWorkflow.ApplicantCanEdit(application.Status));
    }

    [Fact]
    public async Task A_returned_application_can_be_corrected_and_resubmitted()
    {
        await using var db = _database.CreateContext();
        var (applicationId, _) = await SubmittedApplicationAsync(db);

        await ReviewOver(db).CompleteAsync(
            new ReviewInput(applicationId, ReviewOutcome.Return, "Please correct the dates."),
            TheManager,
            TestData.Today);

        await using var correcting = _database.CreateContext();
        var application = await correcting.RentalApplications.FirstAsync(entity => entity.Id == applicationId);

        var saved = await ApplicationsOver(correcting).SaveApplicantInformationAsync(
            new ApplicantInformationInput(
                applicationId, application.ApplicantInformationVersion,
                "Robin", "Alvarez", "555-0101", "robin@example.com",
                "4 Cedar Lane", null, "Portland", "OR", "97202"),
            TestData.Applicant);

        var resubmitted = await ApplicationsOver(correcting).SubmitAsync(
            applicationId, TheApplicant, TestData.Today);

        Assert.True(saved.Succeeded);
        Assert.True(resubmitted.Succeeded);

        await using var check = _database.CreateContext();
        var stored = await check.RentalApplications.FirstAsync(entity => entity.Id == applicationId);
        Assert.Equal(ApplicationStatus.Submitted, stored.Status);
    }

    [Fact]
    public async Task Denying_requires_a_comment_and_is_terminal()
    {
        await using var db = _database.CreateContext();
        var (applicationId, _) = await SubmittedApplicationAsync(db);

        var withoutComment = await ReviewOver(db).CompleteAsync(
            new ReviewInput(applicationId, ReviewOutcome.Deny, null), TheManager, TestData.Today);

        Assert.True(withoutComment.Failed);

        var denied = await ReviewOver(db).CompleteAsync(
            new ReviewInput(applicationId, ReviewOutcome.Deny, "Income does not meet the threshold."),
            TheManager,
            TestData.Today);

        Assert.True(denied.Succeeded);

        await using var check = _database.CreateContext();
        var application = await check.RentalApplications.FirstAsync(entity => entity.Id == applicationId);

        Assert.Equal(ApplicationStatus.Denied, application.Status);
        Assert.Empty(await check.Leases.ToListAsync());

        // Nothing further can happen to it.
        var again = await ReviewOver(check).CompleteAsync(
            new ReviewInput(applicationId, ReviewOutcome.Approve, null), TheManager, TestData.Today);

        Assert.True(again.Failed);
    }

    [Fact]
    public async Task Claiming_takes_an_application_out_of_the_queue()
    {
        await using var db = _database.CreateContext();
        var (applicationId, _) = await SubmittedApplicationAsync(db);

        var result = await ReviewOver(db).ClaimAsync(applicationId, TheManager);

        Assert.True(result.Succeeded);

        await using var check = _database.CreateContext();
        var application = await check.RentalApplications.FirstAsync(entity => entity.Id == applicationId);

        Assert.Equal(ApplicationStatus.UnderReview, application.Status);
        Assert.Equal(TestData.Manager, application.ClaimedByUserId);
        Assert.Equal(TestData.Now, application.ClaimedAtUtc);
    }

    [Fact]
    public async Task A_claimed_application_cannot_be_reviewed_by_another_manager()
    {
        await using var db = _database.CreateContext();
        var (applicationId, _) = await SubmittedApplicationAsync(db);

        await ReviewOver(db).ClaimAsync(applicationId, TheManager);

        await using var other = _database.CreateContext();
        var refused = await ReviewOver(other).CompleteAsync(
            new ReviewInput(applicationId, ReviewOutcome.Approve, null), AnotherManager, TestData.Today);

        Assert.True(refused.Failed);
        Assert.Contains("another property manager", refused.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Releasing_puts_the_application_back_and_clears_the_claim()
    {
        await using var db = _database.CreateContext();
        var (applicationId, _) = await SubmittedApplicationAsync(db);

        await ReviewOver(db).ClaimAsync(applicationId, TheManager);
        var released = await ReviewOver(db).ReleaseAsync(applicationId, TheManager);

        Assert.True(released.Succeeded);

        await using var check = _database.CreateContext();
        var application = await check.RentalApplications.FirstAsync(entity => entity.Id == applicationId);

        Assert.Equal(ApplicationStatus.Submitted, application.Status);
        Assert.Null(application.ClaimedByUserId);
        Assert.Null(application.ClaimedAtUtc);

        // Anyone can pick it up again.
        Assert.True(await ReviewOver(check).ClaimAsync(applicationId, AnotherManager) is { Succeeded: true });
    }

    [Fact]
    public async Task The_history_reads_newest_first_and_names_who_acted()
    {
        await using var db = _database.CreateContext();
        var (applicationId, _) = await SubmittedApplicationAsync(db);

        await ReviewOver(db).ClaimAsync(applicationId, TheManager);
        await ReviewOver(db).CompleteAsync(
            new ReviewInput(applicationId, ReviewOutcome.Approve, "Approved."),
            TheManager,
            TestData.Today);

        await using var check = _database.CreateContext();
        var history = await ReviewOver(check).GetHistoryAsync(applicationId);

        Assert.Equal(3, history.Count);
        Assert.Equal(ApplicationStatus.Approved, history[0].ToStatus);
        Assert.Equal(ApplicationStatus.UnderReview, history[1].ToStatus);
        Assert.Equal(ApplicationStatus.Submitted, history[2].ToStatus);

        Assert.Equal("Alex Chen", history[0].ActorName);
        Assert.Equal("Robin Alvarez", history[2].ActorName);
    }

    [Fact]
    public async Task Timestamps_come_back_from_the_database_as_utc()
    {
        await using var db = _database.CreateContext();
        var (applicationId, _) = await SubmittedApplicationAsync(db);

        await using var check = _database.CreateContext();
        var application = await check.RentalApplications
            .Include(entity => entity.Events)
            .FirstAsync(entity => entity.Id == applicationId);

        // Without the UTC convention these would read back as Unspecified and any later
        // conversion to local time would be wrong by the machine's offset.
        Assert.Equal(DateTimeKind.Utc, application.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, application.SubmittedAtUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, application.Events.First().OccurredAtUtc.Kind);
    }
}

/// <summary>
/// The service checks a unit's availability and then writes a lease in a separate statement, so
/// two approvals that interleave can both pass the check. These pin the constraint that stops the
/// second write, which is the part the check cannot do on its own.
/// </summary>
public class LeaseConstraintTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task One_unit_cannot_hold_two_leases_starting_the_same_day()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var unit = property.Units.First();

        var first = await TestData.AddCompleteDraftAsync(db, unit.Id, TestData.Applicant);
        var second = await TestData.AddCompleteDraftAsync(db, unit.Id, TestData.OtherApplicant);

        db.Leases.Add(LeaseFor(unit.Id, first.Id, TestData.Today));
        await db.SaveChangesAsync();

        // Stands in for the losing half of a race: both requests read the unit's leases before
        // either wrote, so both got past the availability check.
        db.Leases.Add(LeaseFor(unit.Id, second.Id, TestData.Today));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());

        await using var check = _database.CreateContext();
        Assert.Equal(1, await check.Leases.CountAsync());
    }

    [Fact]
    public async Task Different_units_may_of_course_start_leases_on_the_same_day()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);

        var first = await TestData.AddCompleteDraftAsync(db, property.Units.First().Id);
        var second = await TestData.AddCompleteDraftAsync(db, property.Units.Last().Id);

        db.Leases.Add(LeaseFor(property.Units.First().Id, first.Id, TestData.Today));
        db.Leases.Add(LeaseFor(property.Units.Last().Id, second.Id, TestData.Today));

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Leases.CountAsync());
    }

    private static Domain.Entities.Lease LeaseFor(int unitId, int applicationId, DateOnly start) => new()
    {
        UnitId = unitId,
        RentalApplicationId = applicationId,
        StartDate = start,
        EndDate = LeaseTerm.EndDateFor(start),
        MonthlyRent = 1500m
    };
}
