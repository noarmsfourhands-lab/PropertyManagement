using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Infrastructure.Services;

/// <summary>A completed review, as posted from the review modal.</summary>
public record ReviewInput(int ApplicationId, ReviewOutcome Outcome, string? Comment);

public interface IReviewService
{
    Task<IReadOnlyList<ApplicationEvent>> GetHistoryAsync(
        int applicationId,
        CancellationToken cancellationToken = default);

    Task<DomainResult> ClaimAsync(int applicationId, Actor actor, CancellationToken cancellationToken = default);

    Task<DomainResult> ReleaseAsync(int applicationId, Actor actor, CancellationToken cancellationToken = default);

    Task<DomainResult> CompleteAsync(
        ReviewInput input,
        Actor actor,
        DateOnly asOf,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What a property manager does to a submitted application. Approving issues the twelve-month
/// lease; returning and denying require a comment the applicant will read.
/// </summary>
public class ReviewService(PropertyManagementDbContext db, TimeProvider timeProvider) : IReviewService
{
    /// <summary>The audit trail for one application, newest first.</summary>
    public async Task<IReadOnlyList<ApplicationEvent>> GetHistoryAsync(
        int applicationId,
        CancellationToken cancellationToken = default) =>
        await db.ApplicationEvents
            .AsNoTracking()
            .Where(entry => entry.RentalApplicationId == applicationId)
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .ToListAsync(cancellationToken);

    /// <summary>Takes a submitted application out of the queue so no two managers review it at once.</summary>
    public async Task<DomainResult> ClaimAsync(
        int applicationId,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var application = await db.RentalApplications
            .FirstOrDefaultAsync(entity => entity.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return DomainResult.Failure("That application no longer exists.");
        }

        var permitted = ApplicationWorkflow.CanClaim(application);

        if (permitted.Failed)
        {
            return permitted;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        application.Status = ApplicationStatus.UnderReview;
        application.ClaimedByUserId = actor.UserId;
        application.ClaimedAtUtc = now;

        application.Events.Add(new ApplicationEvent
        {
            FromStatus = ApplicationStatus.Submitted,
            ToStatus = ApplicationStatus.UnderReview,
            ActorUserId = actor.UserId,
            ActorName = actor.Name,
            Comment = "Claimed from the review queue.",
            OccurredAtUtc = now
        });

        await db.SaveChangesAsync(cancellationToken);
        return DomainResult.Success();
    }

    /// <summary>Puts a claimed application back in the queue for anyone to pick up.</summary>
    public async Task<DomainResult> ReleaseAsync(
        int applicationId,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var application = await db.RentalApplications
            .FirstOrDefaultAsync(entity => entity.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return DomainResult.Failure("That application no longer exists.");
        }

        var permitted = ApplicationWorkflow.CanRelease(application, actor.UserId);

        if (permitted.Failed)
        {
            return permitted;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        application.Status = ApplicationStatus.Submitted;
        application.ClaimedByUserId = null;
        application.ClaimedAtUtc = null;

        application.Events.Add(new ApplicationEvent
        {
            FromStatus = ApplicationStatus.UnderReview,
            ToStatus = ApplicationStatus.Submitted,
            ActorUserId = actor.UserId,
            ActorName = actor.Name,
            Comment = "Released back to the review queue.",
            OccurredAtUtc = now
        });

        await db.SaveChangesAsync(cancellationToken);
        return DomainResult.Success();
    }

    /// <summary>
    /// Records the outcome and moves the application to its resulting status. Approval re-checks
    /// the unit's availability, because a lease may have been issued since the application was
    /// submitted. Other open applications for the unit are deliberately left as they are.
    /// </summary>
    public async Task<DomainResult> CompleteAsync(
        ReviewInput input,
        Actor actor,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(actor);

        if (!Enum.IsDefined(input.Outcome))
        {
            return DomainResult.Failure("Choose an outcome.");
        }

        var application = await db.RentalApplications
            .Include(entity => entity.Unit)
                .ThenInclude(unit => unit.Leases)
            .FirstOrDefaultAsync(entity => entity.Id == input.ApplicationId, cancellationToken);

        if (application is null)
        {
            return DomainResult.Failure("That application no longer exists.");
        }

        var permitted = ApplicationWorkflow.CanReview(application, actor.UserId);

        if (permitted.Failed)
        {
            return permitted;
        }

        var comment = ReviewRules.ValidateComment(input.Outcome, input.Comment);

        if (comment.Failed)
        {
            return comment;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var from = application.Status;
        var to = ReviewRules.ResultingStatus(input.Outcome);

        if (ReviewRules.IssuesLease(input.Outcome))
        {
            if (LeaseTerm.HasActiveLease(application.Unit.Leases, asOf))
            {
                return DomainResult.Failure(
                    "This unit already has an active lease, so this application cannot be approved.");
            }

            application.Lease = LeaseTerm.Issue(application.Unit, application, asOf);
        }

        application.Status = to;
        application.ClaimedByUserId = null;
        application.ClaimedAtUtc = null;

        if (ApplicationWorkflow.IsTerminal(to))
        {
            application.DecidedAtUtc = now;
        }

        application.Events.Add(new ApplicationEvent
        {
            FromStatus = from,
            ToStatus = to,
            Outcome = input.Outcome,
            Comment = string.IsNullOrWhiteSpace(input.Comment) ? null : input.Comment.Trim(),
            ActorUserId = actor.UserId,
            ActorName = actor.Name,
            OccurredAtUtc = now
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return DomainResult.Success();
        }
        catch (DbUpdateException exception) when (IsDuplicateLease(exception))
        {
            // Two approvals raced: both read the unit's leases before either wrote, so both got
            // past the availability check above. The unique index on the unit and the lease start
            // date is what actually stops the second insert, and the loser is told the same thing
            // the check would have told them.
            return DomainResult.Failure(
                "This unit already has an active lease, so this application cannot be approved.");
        }
    }

    /// <summary>SQL Server's codes for a violated unique index and a violated unique constraint.</summary>
    private const int DuplicateKeyError = 2601;

    private const int UniqueConstraintError = 2627;

    /// <summary>
    /// Whether the failure was another approval getting to this unit first.
    ///
    /// Matched on the error number rather than the message. SQL Server localises its messages, so
    /// reading them would work on an English installation and quietly stop working on any other,
    /// turning a handled race into an unhandled error. The message fallback covers providers that
    /// report no number, which is what the test database does.
    /// </summary>
    private static bool IsDuplicateLease(DbUpdateException exception) => exception.InnerException switch
    {
        SqlException sql => sql.Number is DuplicateKeyError or UniqueConstraintError,
        { } inner => inner.Message.Contains("Leases", StringComparison.OrdinalIgnoreCase)
            && inner.Message.Contains("unique", StringComparison.OrdinalIgnoreCase),
        _ => false
    };
}
