using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;

namespace PropertyManagement.Infrastructure.Tests;

/// <summary>
/// Pins the numbers written into the check constraint to the statuses they are meant to name.
///
/// <c>CK_RentalApplications_SubmittedHasApplicantInformation</c> is raw SQL reading
/// <c>[Status] NOT IN (1, 2, 4, 5)</c>. SQL cannot see a C# enum, so those four numbers are a copy
/// of something that lives somewhere else, and nothing about renumbering the enum would break a
/// build, fail a test or write a log line. The constraint would simply carry on guarding four
/// statuses, the wrong four, forever.
///
/// Concretely: insert a member before <see cref="ApplicationStatus.Returned"/> and the constraint
/// starts demanding complete applicant details from a Returned application, which is legitimately
/// incomplete while its applicant fixes it, and stops demanding them from an Approved one, which is
/// the case it exists for.
/// </summary>
public class CheckConstraintStatusTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    /// <summary>The statuses the constraint's literal numbers are intended to name.</summary>
    private static readonly (ApplicationStatus Status, int Number)[] GuardedByTheConstraint =
    [
        (ApplicationStatus.Submitted, 1),
        (ApplicationStatus.UnderReview, 2),
        (ApplicationStatus.Approved, 4),
        (ApplicationStatus.Denied, 5)
    ];

    /// <summary>The statuses deliberately left out, and why each one belongs out.</summary>
    private static readonly (ApplicationStatus Status, int Number)[] DeliberatelyNotGuarded =
    [
        (ApplicationStatus.Draft, 0),      // nothing has been filled in yet
        (ApplicationStatus.Returned, 3),   // the applicant is mid-correction and may have blanked a field
        (ApplicationStatus.Withdrawn, 6)   // a draft can be given up before anything is typed
    ];

    [Fact]
    public void The_numbers_in_the_constraint_still_name_the_statuses_they_were_written_for()
    {
        foreach (var (status, number) in GuardedByTheConstraint)
        {
            Assert.Equal(number, (int)status);
        }

        foreach (var (status, number) in DeliberatelyNotGuarded)
        {
            Assert.Equal(number, (int)status);
        }

        // Every status is accounted for, so adding one to the enum fails here and forces a decision
        // about which side of the constraint it belongs on.
        Assert.Equal(
            Enum.GetValues<ApplicationStatus>().Length,
            GuardedByTheConstraint.Length + DeliberatelyNotGuarded.Length);
    }

    [Fact]
    public void The_guarded_statuses_are_exactly_the_ones_an_applicant_can_no_longer_edit()
    {
        // This is the reasoning the four numbers encode: a status is guarded when the application
        // is frozen, because that is when its details can no longer be corrected. If the editable
        // set ever changes, the constraint has to change with it.
        foreach (var (status, _) in GuardedByTheConstraint)
        {
            Assert.False(
                ApplicationWorkflow.ApplicantCanEdit(status),
                $"{status} is guarded by the constraint but is still editable.");
        }

        Assert.True(ApplicationWorkflow.ApplicantCanEdit(ApplicationStatus.Draft));
        Assert.True(ApplicationWorkflow.ApplicantCanEdit(ApplicationStatus.Returned));
    }

    [Theory]
    [InlineData(ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.UnderReview)]
    [InlineData(ApplicationStatus.Approved)]
    [InlineData(ApplicationStatus.Denied)]
    public async Task The_database_really_refuses_each_guarded_status_without_applicant_details(
        ApplicationStatus status)
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);

        db.RentalApplications.Add(new RentalApplication
        {
            UnitId = property.Units.First().Id,
            Status = status,
            CreatedAtUtc = TestData.Now,
            PropertyName = property.Name,
            UnitNumber = property.Units.First().UnitNumber,
            ApplicantInformation = new ApplicantInformation()
        });

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(ApplicationStatus.Draft)]
    [InlineData(ApplicationStatus.Returned)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public async Task The_database_allows_each_unguarded_status_to_be_incomplete(
        ApplicationStatus status)
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);

        db.RentalApplications.Add(new RentalApplication
        {
            UnitId = property.Units.First().Id,
            Status = status,
            CreatedAtUtc = TestData.Now,
            PropertyName = property.Name,
            UnitNumber = property.Units.First().UnitNumber,
            ApplicantInformation = new ApplicantInformation()
        });

        // No exception: these three are allowed to be blank, and the constraint must not be widened
        // into them by accident.
        await db.SaveChangesAsync();
    }
}
