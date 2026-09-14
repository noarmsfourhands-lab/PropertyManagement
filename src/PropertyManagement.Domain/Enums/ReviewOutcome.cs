namespace PropertyManagement.Domain.Enums;

/// <summary>
/// Outcome a property manager records when completing a review.
/// Return and Deny require a comment.
/// </summary>
public enum ReviewOutcome
{
    Approve = 0,
    Return = 1,
    Deny = 2
}
