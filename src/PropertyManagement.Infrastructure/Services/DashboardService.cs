using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Services;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Infrastructure.Services;

/// <summary>
/// Builds the property manager's landing page.
///
/// Every figure is counted or projected in the database; nothing here loads a list of entities to
/// count it in memory. That matters more on this page than anywhere else, because it is the one
/// screen that asks about everything at once and it is the first thing loaded after signing in.
/// </summary>
public class DashboardService(PropertyManagementDbContext db) : IDashboardService
{
    /// <summary>How many rows the two activity lists show.</summary>
    private const int ListSize = 5;

    public async Task<ManagerDashboard> GetManagerDashboardAsync(
        string managerUserId,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        var workload = await CountWorkAsync(managerUserId, cancellationToken);
        var properties = await CountOccupancyAsync(asOf, cancellationToken);
        var waiting = await OldestWaitingAsync(asOf, cancellationToken);
        var decisions = await RecentDecisionsAsync(cancellationToken);

        return new ManagerDashboard(workload, properties, waiting, decisions, asOf);
    }

    /// <summary>
    /// Every tile's figure from one grouped count.
    ///
    /// The claim is counted inside the same group rather than by a second query, because "under
    /// review by others" is the difference between the two. Taken as separate reads, a manager
    /// claiming something between them makes that difference negative: the first read sees the
    /// application as Submitted, the second counts it as theirs, and the subtraction goes below
    /// zero. Counting both over one snapshot makes that unrepresentable.
    /// </summary>
    private async Task<ReviewWorkload> CountWorkAsync(
        string managerUserId,
        CancellationToken cancellationToken)
    {
        var byStatus = await db.RentalApplications
            .AsNoTracking()
            .GroupBy(application => application.Status)
            .Select(group => new
            {
                Status = group.Key,
                Count = group.Count(),
                Mine = group.Count(application => application.ClaimedByUserId == managerUserId)
            })
            .ToDictionaryAsync(row => row.Status, row => row, cancellationToken);

        var underReview = byStatus.GetValueOrDefault(ApplicationStatus.UnderReview);

        return new ReviewWorkload(
            AwaitingReview: byStatus.GetValueOrDefault(ApplicationStatus.Submitted)?.Count ?? 0,
            UnderReviewByMe: underReview?.Mine ?? 0,
            UnderReviewByOthers: (underReview?.Count ?? 0) - (underReview?.Mine ?? 0),
            Returned: byStatus.GetValueOrDefault(ApplicationStatus.Returned)?.Count ?? 0,
            Drafts: byStatus.GetValueOrDefault(ApplicationStatus.Draft)?.Count ?? 0);
    }

    /// <summary>
    /// Occupancy per property. The leased count uses the same rule as availability: a unit is
    /// occupied when one of its leases covers the date being asked about.
    /// </summary>
    private async Task<IReadOnlyList<PropertyOccupancy>> CountOccupancyAsync(
        DateOnly asOf,
        CancellationToken cancellationToken) =>
        await db.Properties
            .AsNoTracking()
            .OrderBy(property => property.Name)
            .Select(property => new PropertyOccupancy(
                property.Id,
                property.Name,
                property.Units.Count,
                property.Units.Count(unit =>
                    unit.Leases.Any(lease => lease.StartDate <= asOf && asOf <= lease.EndDate))))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The applications that have waited longest, oldest first.
    ///
    /// Submitted only. Claiming moves an application to Under review, so anything a manager has
    /// already picked up is by definition not in this list and does not need excluding.
    /// </summary>
    private async Task<IReadOnlyList<WaitingApplication>> OldestWaitingAsync(
        DateOnly asOf,
        CancellationToken cancellationToken)
    {
        var rows = await db.RentalApplications
            .AsNoTracking()
            // Nulls sort first ascending, so a Submitted row with no submission time would lead
            // this list while reporting that it had waited no time at all.
            .Where(application => application.Status == ApplicationStatus.Submitted)
            .Where(application => application.SubmittedAtUtc != null)
            .OrderBy(application => application.SubmittedAtUtc)
            .Take(ListSize)
            .Select(application => new
            {
                application.Id,
                PropertyName = application.Unit.Property.Name,
                application.Unit.UnitNumber,
                First = application.ApplicantInformation.FirstName,
                Last = application.ApplicantInformation.LastName,
                application.SubmittedAtUtc
            })
            .ToListAsync(cancellationToken);

        // The day count is arithmetic on values already fetched, so it stays out of the query.
        return
        [
            .. rows.Select(row => new WaitingApplication(
                row.Id,
                row.PropertyName,
                row.UnitNumber,
                NameOrPlaceholder(row.First, row.Last),
                row.SubmittedAtUtc!.Value,
                Math.Max(asOf.DayNumber - DateOnly.FromDateTime(row.SubmittedAtUtc.Value).DayNumber, 0)))
        ];
    }

    private async Task<IReadOnlyList<RecentDecision>> RecentDecisionsAsync(
        CancellationToken cancellationToken) =>
        await db.ApplicationEvents
            .AsNoTracking()
            .Where(entry => entry.Outcome != null)
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Take(ListSize)
            // As the application recorded them. The actor's name beside these is already a
            // snapshot, and one record should not mix a name captured then with a name read now.
            .Select(entry => new RecentDecision(
                entry.RentalApplicationId,
                entry.RentalApplication.PropertyName,
                entry.RentalApplication.UnitNumber,
                entry.Outcome!.Value,
                entry.ActorName,
                entry.OccurredAtUtc,
                entry.Comment))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// A submitted application is now guaranteed by a check constraint to carry a full name, so
    /// this should never fire. It stays because the columns are nullable in the model, which is
    /// correct for drafts, and a dashboard is not the place to throw if that ever stops holding.
    /// </summary>
    private static string NameOrPlaceholder(string? first, string? last)
    {
        var name = $"{first} {last}".Trim();
        return string.IsNullOrWhiteSpace(name) ? "Not yet provided" : name;
    }
}
