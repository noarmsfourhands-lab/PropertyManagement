using PropertyManagement.Domain.Enums;

namespace PropertyManagement.Application.Services;

/// <summary>How much work is waiting, and where.</summary>
/// <param name="AwaitingReview">Submitted and unclaimed, so anyone can pick them up.</param>
/// <param name="UnderReviewByMe">Claimed by the manager looking at the page.</param>
/// <param name="UnderReviewByOthers">Claimed by someone else, shown so the total adds up.</param>
/// <param name="Returned">Sent back, now waiting on the applicant rather than on the office.</param>
/// <param name="Drafts">Started but never submitted. Not work, but worth knowing about.</param>
public record ReviewWorkload(
    int AwaitingReview,
    int UnderReviewByMe,
    int UnderReviewByOthers,
    int Returned,
    int Drafts)
{
    /// <summary>Everything a manager could act on right now.</summary>
    public int Actionable => AwaitingReview + UnderReviewByMe;
}

/// <summary>One property's occupancy as of a date.</summary>
public record PropertyOccupancy(int PropertyId, string Name, int Units, int Leased)
{
    public int Available => Units - Leased;

    public int PercentLeased => Units == 0 ? 0 : (int)Math.Round(Leased * 100d / Units);
}

/// <summary>A submitted application and how long it has been waiting.</summary>
public record WaitingApplication(
    int Id,
    string PropertyName,
    string UnitNumber,
    string ApplicantName,
    DateTime SubmittedAtUtc,
    int DaysWaiting);

/// <summary>A decision a manager recorded, for the recent-activity list.</summary>
public record RecentDecision(
    int ApplicationId,
    string PropertyName,
    string UnitNumber,
    ReviewOutcome Outcome,
    string ActorName,
    DateTime OccurredAtUtc,
    string? Comment);

/// <summary>Everything the property manager landing page shows.</summary>
public record ManagerDashboard(
    ReviewWorkload Workload,
    IReadOnlyList<PropertyOccupancy> Properties,
    IReadOnlyList<WaitingApplication> Waiting,
    IReadOnlyList<RecentDecision> Decisions,
    DateOnly AsOf)
{
    public int TotalUnits => Properties.Sum(property => property.Units);

    public int LeasedUnits => Properties.Sum(property => property.Leased);

    public int AvailableUnits => TotalUnits - LeasedUnits;

    public int PercentLeased => TotalUnits == 0 ? 0 : (int)Math.Round(LeasedUnits * 100d / TotalUnits);
}

public interface IDashboardService
{
    Task<ManagerDashboard> GetManagerDashboardAsync(
        string managerUserId,
        DateOnly asOf,
        CancellationToken cancellationToken = default);
}
