using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;

namespace PropertyManagement.Application.Services;

/// <summary>Input for creating or updating a property. Id is zero when creating.</summary>
public record PropertyInput(
    int Id,
    string Name,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode);

/// <summary>A page of units together with the total the filter matched.</summary>
public record UnitPage(IReadOnlyList<Unit> Units, int TotalCount);

/// <summary>Input for creating or updating a unit. Id is zero when creating.</summary>
public record UnitInput(
    int Id,
    int PropertyId,
    string UnitNumber,
    int Bedrooms,
    decimal MonthlyRent,
    int UnitTypeId);

/// <summary>
/// What already depends on a unit, so a manager editing it is told before the edit lands.
///
/// A rental application stores only which unit it is for, never a copy of that unit's rent, type or
/// bedroom count. Every page that shows an application reads those from the unit as it stands now,
/// so changing a unit rewrites what every application against it appears to say, including ones
/// already submitted, under review or decided. That is not always wrong, and it is never something
/// to discover afterwards.
/// </summary>
/// <param name="ByStatus">Counts by application status, highest count first, zeroes omitted.</param>
/// <param name="HasLease">
/// Whether a lease has been issued on the unit. A lease copies its rent when it is issued, so it is
/// unaffected by the edit; managers ask, so the answer is carried here to be shown.
/// </param>
public record UnitApplicationImpact(
    IReadOnlyList<UnitApplicationCount> ByStatus,
    bool HasLease)
{
    public int Total => ByStatus.Sum(entry => entry.Count);

    /// <summary>Applications still moving, which is where an edit changes what happens next.</summary>
    public int OpenCount => ByStatus
        .Where(entry => !ApplicationWorkflow.TerminalStatuses.Contains(entry.Status))
        .Sum(entry => entry.Count);

    /// <summary>Decided, withdrawn or denied: historical records the edit still rewrites.</summary>
    public int ClosedCount => Total - OpenCount;

    public bool Any => Total > 0;

    public static UnitApplicationImpact None { get; } = new([], false);
}

/// <param name="Status">The status these applications are in.</param>
/// <param name="Count">How many are in it.</param>
public record UnitApplicationCount(ApplicationStatus Status, int Count);

public interface IPropertyService
{
    Task<IReadOnlyList<Property>> GetPropertiesAsync(CancellationToken cancellationToken = default);

    Task<Property?> GetPropertyAsync(int id, CancellationToken cancellationToken = default);

    Task<Unit?> GetUnitAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UnitType>> GetSelectableUnitTypesAsync(
        int? currentUnitTypeId,
        CancellationToken cancellationToken = default);

    Task<UnitPage> GetAvailableUnitsAsync(
        DateOnly asOf,
        int take,
        int skip = 0,
        CancellationToken cancellationToken = default);

    Task<DomainResult> SavePropertyAsync(PropertyInput input, CancellationToken cancellationToken = default);

    Task<DomainResult> DeletePropertyAsync(int id, CancellationToken cancellationToken = default);

    Task<DomainResult> SaveUnitAsync(UnitInput input, CancellationToken cancellationToken = default);

    /// <summary>What already references this unit. Empty for a unit that does not exist yet.</summary>
    Task<UnitApplicationImpact> GetUnitImpactAsync(int unitId, CancellationToken cancellationToken = default);

    Task<DomainResult> DeleteUnitAsync(int id, CancellationToken cancellationToken = default);
}
