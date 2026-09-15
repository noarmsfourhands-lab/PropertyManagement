using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Application.Services;
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

    /// <summary>
    /// A submitted application already claimed by <see cref="TheManager"/>, which is the state a
    /// review can actually be completed from. Claiming is mandatory, so almost every test about an
    /// outcome starts here rather than at Submitted.
    /// </summary>
    private async Task<(int ApplicationId, int UnitId)> ClaimedApplicationAsync(
        Persistence.PropertyManagementDbContext db,
        int unitIndex = 0)
    {
        var ids = await SubmittedApplicationAsync(db, unitIndex);
        var claimed = await ReviewOver(db).ClaimAsync(ids.ApplicationId, TheManager);

        Assert.True(claimed.Succeeded, claimed.Error);

        return ids;
    }

    [Fact]
    public async Task A_submitted_application_must_be_claimed_before_it_can_be_reviewed()
    {
        await using var db = _database.CreateContext();
        var (applicationId, _) = await SubmittedApplicationAsync(db);

        // Without this the queue is advisory: two managers can both open the outcome form on the
        // same application, and the second to press the button has their decision refused by the
        // status check after the fact, having already typed a comment.
        var straightToOutcome = await ReviewOver(db).CompleteAsync(
            new ReviewInput(applicationId, ReviewOutcome.Approve, null), TheManager, TestData.Today);

        Assert.True(straightToOutcome.Failed);
        Assert.Equal("Claim this application before reviewing it.", straightToOutcome.Error);

        await ReviewOver(db).ClaimAsync(applicationId, TheManager);

        var afterClaiming = await ReviewOver(db).CompleteAsync(
            new ReviewInput(applicationId, ReviewOutcome.Approve, null), TheManager, TestData.Today);

        Assert.True(afterClaiming.Succeeded, afterClaiming.Error);
    }

    [Fact]
    public async Task Another_manager_can_release_a_claim_and_the_history_says_so()
    {
        await using var db = _database.CreateContext();
        var (applicationId, _) = await SubmittedApplicationAsync(db);

        await ReviewOver(db).ClaimAsync(applicationId, TheManager);

        // The manager holding it is gone, away or locked out. Restricting release to them left the
        // application stuck for everyone else, because claiming only works from Submitted.
        var released = await ReviewOver(db).ReleaseAsync(applicationId, AnotherManager);

        Assert.True(released.Succeeded, released.Error);

        await using var check = _database.CreateContext();
        var stored = await check.RentalApplications
            .Include(entity => entity.Events)
            .FirstAsync(entity => entity.Id == applicationId);

        Assert.Equal(ApplicationStatus.Submitted, stored.Status);
        Assert.Null(stored.ClaimedByUserId);

        // Taking work off a colleague is recorded as exactly that, under the name of whoever did it.
        var release = stored.Events.OrderByDescending(entry => entry.Id).First();

        Assert.Equal(AnotherManager.Name, release.ActorName);
        Assert.Contains("from another manager", release.Comment!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Releasing_your_own_claim_is_not_recorded_as_taking_it_from_anyone()
    {
        await using var db = _database.CreateContext();
        var (applicationId, _) = await SubmittedApplicationAsync(db);

        await ReviewOver(db).ClaimAsync(applicationId, TheManager);
        await ReviewOver(db).ReleaseAsync(applicationId, TheManager);

        await using var check = _database.CreateContext();
        var stored = await check.RentalApplications
            .Include(entity => entity.Events)
            .FirstAsync(entity => entity.Id == applicationId);

        var release = stored.Events.OrderByDescending(entry => entry.Id).First();

        Assert.DoesNotContain("another manager", release.Comment!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_issued_lease_records_what_it_was_issued_for()
    {
        await using var db = _database.CreateContext();
        var (applicationId, unitId) = await ClaimedApplicationAsync(db);

        var approved = await ReviewOver(db).CompleteAsync(
            new ReviewInput(applicationId, ReviewOutcome.Approve, "Looks good."),
            TheManager,
            TestData.Today);

        Assert.True(approved.Succeeded, approved.Error);

        // The unit is renumbered and the property renamed afterwards, which a manager may do.
        await using var edit = _database.CreateContext();
        var unit = await edit.Units.Include(entity => entity.Property).FirstAsync(entity => entity.Id == unitId);
        var originalNumber = unit.UnitNumber;
        var originalProperty = unit.Property.Name;
        unit.UnitNumber = "999";
        unit.Property.Name = "Renamed Court";
        await edit.SaveChangesAsync();

        await using var check = _database.CreateContext();
        var lease = await check.Leases.FirstAsync(entity => entity.RentalApplicationId == applicationId);

        // A lease is the contractual record here, so it keeps describing what it was issued for,
        // the same way it already kept the rent it was issued at.
        Assert.Equal(originalProperty, lease.PropertyName);
        Assert.Equal(originalNumber, lease.UnitNumber);
    }

    [Fact]
    public async Task A_claimed_application_cannot_be_reviewed_by_another_manager()
    {
        await using var db = _database.CreateContext();
        var (applicationId, _) = await ClaimedApplicationAsync(db);

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
        var (applicationId, _) = await ClaimedApplicationAsync(db);

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
        var (applicationId, _) = await ClaimedApplicationAsync(db);

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
        var (applicationId, _) = await ClaimedApplicationAsync(db);

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

    [Fact]
    public async Task Approving_issues_a_twelve_month_lease_for_the_unit()
    {
        await using var db = _database.CreateContext();
        var (applicationId, unitId) = await ClaimedApplicationAsync(db);

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
        var (applicationId, _) = await ClaimedApplicationAsync(db);

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

        // One manager works both of them in turn, claiming each before deciding it.
        await review.ClaimAsync(first.Id, TheManager);

        var approved = await review.CompleteAsync(
            new ReviewInput(first.Id, ReviewOutcome.Approve, null), TheManager, TestData.Today);

        await review.ClaimAsync(second.Id, TheManager);

        var refused = await review.CompleteAsync(
            new ReviewInput(second.Id, ReviewOutcome.Approve, null), TheManager, TestData.Today);

        Assert.True(approved.Succeeded);
        Assert.True(refused.Failed);
        Assert.Contains("already has an active lease", refused.Error!, StringComparison.OrdinalIgnoreCase);

        // Exactly one lease exists, and the refused application is left where it was rather than
        // being cascaded to Denied. Where it was is Under review, because the manager had to claim
        // it to reach the outcome form at all; the point is that the refusal decided nothing.
        await using var check = _database.CreateContext();
        Assert.Equal(1, await check.Leases.CountAsync());

        var untouched = await check.RentalApplications.FirstAsync(entity => entity.Id == second.Id);

        Assert.Equal(ApplicationStatus.UnderReview, untouched.Status);
        Assert.Null(untouched.DecidedAtUtc);
        Assert.Null(untouched.Lease);
    }

    [Fact]
    public async Task Returning_without_a_comment_is_refused()
    {
        await using var db = _database.CreateContext();
        var (applicationId, _) = await ClaimedApplicationAsync(db);

        var result = await ReviewOver(db).CompleteAsync(
            new ReviewInput(applicationId, ReviewOutcome.Return, "   "),
            TheManager,
            TestData.Today);

        Assert.True(result.Failed);
        Assert.Contains("comment is required", result.Error!, StringComparison.OrdinalIgnoreCase);

        await using var check = _database.CreateContext();
        var unchanged = await check.RentalApplications.FirstAsync(entity => entity.Id == applicationId);

        // Still where it was. That is Under review rather than Submitted now, because reaching the
        // outcome form at all requires claiming it first.
        Assert.Equal(ApplicationStatus.UnderReview, unchanged.Status);
    }

    [Fact]
    public async Task Returning_with_a_comment_sends_the_application_back_for_correction()
    {
        await using var db = _database.CreateContext();
        var (applicationId, _) = await ClaimedApplicationAsync(db);

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
        var (applicationId, _) = await ClaimedApplicationAsync(db);

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
        var (applicationId, _) = await ClaimedApplicationAsync(db);

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
}
