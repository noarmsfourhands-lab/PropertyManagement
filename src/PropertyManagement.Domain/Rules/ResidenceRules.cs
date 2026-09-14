using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Entities;

namespace PropertyManagement.Domain.Rules;

/// <summary>
/// Rules for the residence history section: what makes a single residence coherent, and what
/// makes the section as a whole complete enough to continue past.
/// </summary>
public static class ResidenceRules
{
    /// <summary>An application needs at least this many prior residences before it can continue.</summary>
    public const int MinimumResidences = 1;

    /// <summary>
    /// Checks one residence's dates. A missing move-out date means the applicant has not left yet,
    /// which is allowed; a supplied one must fall on or after the move-in date, and neither date
    /// may be in the future.
    /// </summary>
    public static DomainResult ValidateDates(DateOnly moveInDate, DateOnly? moveOutDate, DateOnly asOf)
    {
        if (moveInDate > asOf)
        {
            return DomainResult.Failure("Move-in date cannot be in the future.");
        }

        if (moveOutDate is null)
        {
            return DomainResult.Success();
        }

        if (moveOutDate < moveInDate)
        {
            return DomainResult.Failure("Move-out date cannot be before the move-in date.");
        }

        return moveOutDate > asOf
            ? DomainResult.Failure("Move-out date cannot be in the future.")
            : DomainResult.Success();
    }

    /// <summary>
    /// Whether the section holds enough to continue. Continue saves a section only when it is
    /// valid, and an empty residence history is not something an application can be judged on.
    /// </summary>
    public static DomainResult ValidateHistory(IReadOnlyCollection<Residence> residences)
    {
        ArgumentNullException.ThrowIfNull(residences);

        return residences.Count >= MinimumResidences
            ? DomainResult.Success()
            : DomainResult.Failure("Add at least one previous residence before continuing.");
    }

    /// <summary>
    /// Residences newest first, which is the order a reviewer reads them in. A residence that has
    /// not been left yet sorts above every residence that has.
    /// </summary>
    public static IReadOnlyList<Residence> InReviewOrder(IEnumerable<Residence> residences)
    {
        ArgumentNullException.ThrowIfNull(residences);

        return residences
            .OrderByDescending(residence => residence.MoveOutDate is null)
            .ThenByDescending(residence => residence.MoveOutDate)
            .ThenByDescending(residence => residence.MoveInDate)
            .ToList();
    }
}
