namespace PropertyManagement.Domain.Entities;

/// <summary>
/// Bonus 5. Joins an application to each applicant who may view and edit it.
/// Ownership checks run against this set rather than a single owner column.
/// </summary>
public class RentalApplicationApplicant
{
    public int RentalApplicationId { get; set; }

    public RentalApplication RentalApplication { get; set; } = null!;

    public string ApplicantUserId { get; set; } = string.Empty;

    /// <summary>The applicant who started the application. Used for display, not for permissions.</summary>
    public bool IsPrimary { get; set; }

    public DateTimeOffset AddedAtUtc { get; set; }
}
