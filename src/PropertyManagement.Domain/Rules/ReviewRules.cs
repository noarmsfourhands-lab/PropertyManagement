using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Enums;

namespace PropertyManagement.Domain.Rules;

/// <summary>Rules for completing a review: the comment requirement and the status each outcome produces.</summary>
public static class ReviewRules
{
    /// <summary>Outcomes that cannot be recorded without an explanation for the applicant.</summary>
    public static readonly IReadOnlySet<ReviewOutcome> OutcomesRequiringComment =
        new HashSet<ReviewOutcome> { ReviewOutcome.Return, ReviewOutcome.Deny };

    public static bool RequiresComment(ReviewOutcome outcome) => OutcomesRequiringComment.Contains(outcome);

    public static DomainResult ValidateComment(ReviewOutcome outcome, string? comment)
    {
        if (!RequiresComment(outcome))
        {
            return DomainResult.Success();
        }

        return string.IsNullOrWhiteSpace(comment)
            ? DomainResult.Failure($"A comment is required when the outcome is {outcome}.", field: "Comment")
            : DomainResult.Success();
    }

    /// <summary>The status an application moves to when the review is completed.</summary>
    public static ApplicationStatus ResultingStatus(ReviewOutcome outcome) => outcome switch
    {
        ReviewOutcome.Approve => ApplicationStatus.Approved,
        ReviewOutcome.Return => ApplicationStatus.Returned,
        ReviewOutcome.Deny => ApplicationStatus.Denied,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unhandled review outcome.")
    };

    /// <summary>Approval is the only outcome that issues a lease.</summary>
    public static bool IssuesLease(ReviewOutcome outcome) => outcome == ReviewOutcome.Approve;
}
