namespace PropertyManagement.Domain.Enums;

/// <summary>
/// Lifecycle states for a rental application.
/// Approved, Denied and Withdrawn are terminal.
/// </summary>
public enum ApplicationStatus
{
    Draft = 0,
    Submitted = 1,
    UnderReview = 2,
    Returned = 3,
    Approved = 4,
    Denied = 5,
    Withdrawn = 6
}
