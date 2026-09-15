using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Infrastructure.Persistence;
using PropertyManagement.Application.Services;
using PropertyManagement.Infrastructure.Services;

namespace PropertyManagement.Infrastructure.Tests;

/// <summary>
/// The dashboard is the one screen that asks about everything at once, so these pin the figures
/// rather than trusting that four separate queries happen to agree.
/// </summary>
public class DashboardServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static DashboardService ServiceOver(PropertyManagementDbContext db) => new(db);

    [Fact]
    public async Task An_empty_system_reports_zeroes_rather_than_failing()
    {
        await using var db = _database.CreateContext();

        var dashboard = await ServiceOver(db).GetManagerDashboardAsync(TestData.Manager, TestData.Today);

        Assert.Equal(0, dashboard.Workload.AwaitingReview);
        Assert.Equal(0, dashboard.TotalUnits);
        Assert.Equal(0, dashboard.PercentLeased);
        Assert.Empty(dashboard.Waiting);
        Assert.Empty(dashboard.Decisions);
        Assert.Equal(TestData.Today, dashboard.AsOf);
    }

    [Fact]
    public async Task The_workload_counts_each_status_and_separates_your_claims_from_everyone_elses()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var unitId = property.Units.First().Id;

        await AddAsync(db, unitId, ApplicationStatus.Submitted);
        await AddAsync(db, unitId, ApplicationStatus.Submitted);
        await AddAsync(db, unitId, ApplicationStatus.UnderReview, claimedBy: TestData.Manager);
        await AddAsync(db, unitId, ApplicationStatus.UnderReview, claimedBy: TestData.OtherManager);
        await AddAsync(db, unitId, ApplicationStatus.Returned);
        await AddAsync(db, unitId, ApplicationStatus.Draft);
        await AddAsync(db, unitId, ApplicationStatus.Approved);

        var dashboard = await ServiceOver(db).GetManagerDashboardAsync(TestData.Manager, TestData.Today);

        Assert.Equal(2, dashboard.Workload.AwaitingReview);
        Assert.Equal(1, dashboard.Workload.UnderReviewByMe);
        Assert.Equal(1, dashboard.Workload.UnderReviewByOthers);
        Assert.Equal(1, dashboard.Workload.Returned);
        Assert.Equal(1, dashboard.Workload.Drafts);

        // What this manager could pick up or finish right now.
        Assert.Equal(3, dashboard.Workload.Actionable);
    }

    [Fact]
    public async Task Occupancy_counts_a_unit_as_leased_only_while_a_lease_covers_the_date()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var unit = property.Units.First();
        var application = await TestData.AddCompleteDraftAsync(db, unit.Id);

        var start = TestData.Today.AddMonths(-1);
        db.Leases.Add(new Lease
        {
            UnitId = unit.Id,
            RentalApplicationId = application.Id,
            StartDate = start,
            EndDate = LeaseTerm.EndDateFor(start),
            MonthlyRent = unit.MonthlyRent
        });
        await db.SaveChangesAsync();

        await using var check = _database.CreateContext();
        var service = ServiceOver(check);

        var today = await service.GetManagerDashboardAsync(TestData.Manager, TestData.Today);
        Assert.Equal(2, today.TotalUnits);
        Assert.Equal(1, today.LeasedUnits);
        Assert.Equal(1, today.AvailableUnits);
        Assert.Equal(50, today.PercentLeased);

        // The same rule as availability, so the two screens can never disagree.
        var afterItEnds = await service.GetManagerDashboardAsync(
            TestData.Manager,
            LeaseTerm.EndDateFor(start).AddDays(1));

        Assert.Equal(0, afterItEnds.LeasedUnits);
        Assert.Equal(0, afterItEnds.PercentLeased);
    }

    [Fact]
    public async Task Occupancy_is_broken_down_by_property()
    {
        await using var db = _database.CreateContext();
        await TestData.AddPropertyWithUnitsAsync(db);

        db.Properties.Add(new Property
        {
            Name = "Birch Commons",
            AddressLine1 = "2 Birch Way",
            City = "Portland",
            State = "OR",
            PostalCode = "97203"
        });
        await db.SaveChangesAsync();

        var dashboard = await ServiceOver(db).GetManagerDashboardAsync(TestData.Manager, TestData.Today);

        // Alphabetical, and a property with no units reports zero rather than being left out.
        Assert.Equal(["Alder Court", "Birch Commons"], dashboard.Properties.Select(p => p.Name));
        Assert.Equal(2, dashboard.Properties[0].Units);
        Assert.Equal(0, dashboard.Properties[1].Units);
        Assert.Equal(0, dashboard.Properties[1].PercentLeased);
    }

    [Fact]
    public async Task The_waiting_list_is_oldest_first_and_counts_the_days()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var unitId = property.Units.First().Id;

        await AddAsync(db, unitId, ApplicationStatus.Submitted, submittedDaysAgo: 2);
        await AddAsync(db, unitId, ApplicationStatus.Submitted, submittedDaysAgo: 11);
        await AddAsync(db, unitId, ApplicationStatus.Submitted, submittedDaysAgo: 0);

        // Not submitted, so not waiting on anyone here.
        await AddAsync(db, unitId, ApplicationStatus.Draft);

        var dashboard = await ServiceOver(db).GetManagerDashboardAsync(TestData.Manager, TestData.Today);

        Assert.Equal([11, 2, 0], dashboard.Waiting.Select(row => row.DaysWaiting));
        Assert.All(dashboard.Waiting, row => Assert.Equal("Alder Court", row.PropertyName));
    }

    [Fact]
    public async Task A_submitted_application_cannot_have_its_applicant_details_blanked()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);

        var application = await AddAsync(db, property.Units.First().Id, ApplicationStatus.Submitted);

        application.ApplicantInformation = new ApplicantInformation();

        // This used to be possible, and the dashboard coped by showing a placeholder. Coping with an
        // impossible state is not the same as making it impossible: the rule that a submitted
        // application carries its applicant's details lived only in the submit path, so anything
        // that set a status another way could produce an approved application with nobody on it.
        // The check constraint is the guarantee; this is the test that it is really there.
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Recent_decisions_are_newest_first_and_ignore_movements_that_were_not_decisions()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var application = await AddAsync(db, property.Units.First().Id, ApplicationStatus.Approved);

        application.Events.Add(new ApplicationEvent
        {
            FromStatus = ApplicationStatus.Draft,
            ToStatus = ApplicationStatus.Submitted,
            ActorUserId = TestData.Applicant,
            ActorName = "Robin Alvarez",
            OccurredAtUtc = TestData.Now.AddDays(-3)
        });
        application.Events.Add(new ApplicationEvent
        {
            FromStatus = ApplicationStatus.Submitted,
            ToStatus = ApplicationStatus.Returned,
            Outcome = ReviewOutcome.Return,
            Comment = "Dates do not line up.",
            ActorUserId = TestData.Manager,
            ActorName = "Alex Chen",
            OccurredAtUtc = TestData.Now.AddDays(-2)
        });
        application.Events.Add(new ApplicationEvent
        {
            FromStatus = ApplicationStatus.Submitted,
            ToStatus = ApplicationStatus.Approved,
            Outcome = ReviewOutcome.Approve,
            ActorUserId = TestData.OtherManager,
            ActorName = "Jordan Poole",
            OccurredAtUtc = TestData.Now.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var dashboard = await ServiceOver(db).GetManagerDashboardAsync(TestData.Manager, TestData.Today);

        // The submission carries no outcome, so it is movement rather than a decision.
        Assert.Equal([ReviewOutcome.Approve, ReviewOutcome.Return], dashboard.Decisions.Select(d => d.Outcome));
        Assert.Equal("Jordan Poole", dashboard.Decisions[0].ActorName);
        Assert.Equal("Dates do not line up.", dashboard.Decisions[1].Comment);
        Assert.Equal("Alder Court", dashboard.Decisions[0].PropertyName);
    }

    [Fact]
    public async Task Both_activity_lists_are_capped()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var unitId = property.Units.First().Id;

        for (var i = 0; i < 9; i++)
        {
            await AddAsync(db, unitId, ApplicationStatus.Submitted, submittedDaysAgo: i);
        }

        var dashboard = await ServiceOver(db).GetManagerDashboardAsync(TestData.Manager, TestData.Today);

        Assert.Equal(9, dashboard.Workload.AwaitingReview);
        Assert.Equal(5, dashboard.Waiting.Count);
    }

    /// <summary>Adds an application in a given status without going through the workflow.</summary>
    private static async Task<RentalApplication> AddAsync(
        PropertyManagementDbContext db,
        int unitId,
        ApplicationStatus status,
        string? claimedBy = null,
        int submittedDaysAgo = 1)
    {
        var unit = await db.Units.Include(entity => entity.Property).FirstAsync(entity => entity.Id == unitId);

        var application = new RentalApplication
        {
            UnitId = unitId,
            Status = status,
            CreatedAtUtc = TestData.Now.AddDays(-submittedDaysAgo - 1),
            SubmittedAtUtc = status == ApplicationStatus.Draft ? null : TestData.Now.AddDays(-submittedDaysAgo),
            ClaimedByUserId = claimedBy,
            PropertyName = unit.Property?.Name ?? string.Empty,
            UnitNumber = unit.UnitNumber,

            // Complete, because the database now refuses a submitted or decided application that is
            // not. Skipping the workflow is fine; inventing a state the workflow cannot produce is
            // what this fixture must stop doing.
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
                    ApplicantUserId = TestData.Applicant,
                    IsPrimary = true,
                    AddedAtUtc = TestData.Now
                }
            ]
        };

        db.RentalApplications.Add(application);
        await db.SaveChangesAsync();

        return application;
    }
}
