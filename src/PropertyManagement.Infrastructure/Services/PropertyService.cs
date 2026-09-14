using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Infrastructure.Services;

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
        CancellationToken cancellationToken = default);

    Task<DomainResult> SavePropertyAsync(PropertyInput input, CancellationToken cancellationToken = default);

    Task<DomainResult> DeletePropertyAsync(int id, CancellationToken cancellationToken = default);

    Task<DomainResult> SaveUnitAsync(UnitInput input, CancellationToken cancellationToken = default);

    Task<DomainResult> DeleteUnitAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Maintains properties and their units. Every rule the UI relies on is re-checked here, because
/// a request can arrive with any values regardless of what the form offered.
/// </summary>
public class PropertyService(PropertyManagementDbContext db) : IPropertyService
{
    public async Task<IReadOnlyList<Property>> GetPropertiesAsync(CancellationToken cancellationToken = default) =>
        await db.Properties
            .AsNoTracking()
            .Include(property => property.Units)
            .OrderBy(property => property.Name)
            .ToListAsync(cancellationToken);

    public async Task<Property?> GetPropertyAsync(int id, CancellationToken cancellationToken = default) =>
        await db.Properties
            .AsNoTracking()
            .Include(property => property.Units)
                .ThenInclude(unit => unit.UnitType)
            .FirstOrDefaultAsync(property => property.Id == id, cancellationToken);

    public async Task<Unit?> GetUnitAsync(int id, CancellationToken cancellationToken = default) =>
        await db.Units
            .AsNoTracking()
            .Include(unit => unit.UnitType)
            .Include(unit => unit.Property)
            .FirstOrDefaultAsync(unit => unit.Id == id, cancellationToken);

    /// <summary>
    /// The types a dropdown may offer: every active type, plus the unit's current type when that
    /// type has since been retired, so editing an older unit does not force a type change.
    /// </summary>
    public async Task<IReadOnlyList<UnitType>> GetSelectableUnitTypesAsync(
        int? currentUnitTypeId,
        CancellationToken cancellationToken = default)
    {
        var types = await db.UnitTypes
            .AsNoTracking()
            .Where(unitType => unitType.IsActive || unitType.Id == currentUnitTypeId)
            .ToListAsync(cancellationToken);

        return UnitTypeSelection.SelectableFor(types, currentUnitTypeId);
    }

    /// <summary>
    /// Units with no lease term covering <paramref name="asOf"/>. The availability rule is
    /// expressed as a query so the filtering happens in the database rather than in memory;
    /// <see cref="LeaseTerm.HasActiveLease"/> is the same rule stated for objects already loaded.
    ///
    /// The count and the page are two queries against the same filter, so showing "8 of 37" never
    /// means loading 37 rows to display 8.
    /// </summary>
    public async Task<UnitPage> GetAvailableUnitsAsync(
        DateOnly asOf,
        int take,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);

        var available = db.Units
            .AsNoTracking()
            .Where(unit => !unit.Leases.Any(lease => lease.StartDate <= asOf && asOf <= lease.EndDate));

        var total = await available.CountAsync(cancellationToken);

        var page = await available
            .Include(unit => unit.Property)
            .Include(unit => unit.UnitType)
            .OrderBy(unit => unit.Property.Name)
            .ThenBy(unit => unit.UnitNumber)
            .Take(take)
            .ToListAsync(cancellationToken);

        return new UnitPage(page, total);
    }

    public async Task<DomainResult> SavePropertyAsync(
        PropertyInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var property = input.Id == 0
            ? new Property()
            : await db.Properties.FirstOrDefaultAsync(entity => entity.Id == input.Id, cancellationToken);

        if (property is null)
        {
            return DomainResult.Failure("That property no longer exists.");
        }

        property.Name = input.Name.Trim();
        property.AddressLine1 = input.AddressLine1.Trim();
        property.AddressLine2 = string.IsNullOrWhiteSpace(input.AddressLine2) ? null : input.AddressLine2.Trim();
        property.City = input.City.Trim();
        property.State = input.State.Trim();
        property.PostalCode = input.PostalCode.Trim();

        if (input.Id == 0)
        {
            db.Properties.Add(property);
        }

        await db.SaveChangesAsync(cancellationToken);
        return DomainResult.Success();
    }

    /// <summary>
    /// Removing a property takes its units with it, so it is refused once any unit carries history
    /// worth keeping. Deactivating a unit type is the soft path; properties have no such flag.
    /// </summary>
    public async Task<DomainResult> DeletePropertyAsync(int id, CancellationToken cancellationToken = default)
    {
        var property = await db.Properties
            .Include(entity => entity.Units)
            .FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);

        if (property is null)
        {
            return DomainResult.Failure("That property no longer exists.");
        }

        var unitIds = property.Units.Select(unit => unit.Id).ToList();

        if (await db.Leases.AnyAsync(lease => unitIds.Contains(lease.UnitId), cancellationToken))
        {
            return DomainResult.Failure("This property has units under lease and cannot be removed.");
        }

        if (await db.RentalApplications.AnyAsync(app => unitIds.Contains(app.UnitId), cancellationToken))
        {
            return DomainResult.Failure("This property has units with rental applications and cannot be removed.");
        }

        db.Properties.Remove(property);
        await db.SaveChangesAsync(cancellationToken);
        return DomainResult.Success();
    }

    public async Task<DomainResult> SaveUnitAsync(UnitInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var unit = input.Id == 0
            ? new Unit { PropertyId = input.PropertyId }
            : await db.Units.FirstOrDefaultAsync(entity => entity.Id == input.Id, cancellationToken);

        if (unit is null)
        {
            return DomainResult.Failure("That unit no longer exists.");
        }

        if (!await db.Properties.AnyAsync(property => property.Id == unit.PropertyId, cancellationToken))
        {
            return DomainResult.Failure("That property no longer exists.");
        }

        var unitType = await db.UnitTypes
            .FirstOrDefaultAsync(entity => entity.Id == input.UnitTypeId, cancellationToken);

        if (unitType is null)
        {
            return DomainResult.Failure("Select a unit type.");
        }

        // The inactive-lookup rule, enforced on the server rather than trusted from the dropdown.
        var currentUnitTypeId = input.Id == 0 ? (int?)null : unit.UnitTypeId;
        var typeCheck = UnitTypeSelection.Validate(unitType, currentUnitTypeId);

        if (typeCheck.Failed)
        {
            return typeCheck;
        }

        var unitNumber = input.UnitNumber.Trim();

        var duplicate = await db.Units.AnyAsync(
            entity => entity.PropertyId == unit.PropertyId
                && entity.UnitNumber == unitNumber
                && entity.Id != input.Id,
            cancellationToken);

        if (duplicate)
        {
            return DomainResult.Failure($"Unit {unitNumber} already exists in this property.");
        }

        unit.UnitNumber = unitNumber;
        unit.Bedrooms = input.Bedrooms;
        unit.MonthlyRent = input.MonthlyRent;
        unit.UnitTypeId = input.UnitTypeId;

        if (input.Id == 0)
        {
            db.Units.Add(unit);
        }

        await db.SaveChangesAsync(cancellationToken);
        return DomainResult.Success();
    }

    public async Task<DomainResult> DeleteUnitAsync(int id, CancellationToken cancellationToken = default)
    {
        var unit = await db.Units.FirstOrDefaultAsync(entity => entity.Id == id, cancellationToken);

        if (unit is null)
        {
            return DomainResult.Failure("That unit no longer exists.");
        }

        if (await db.Leases.AnyAsync(lease => lease.UnitId == id, cancellationToken))
        {
            return DomainResult.Failure("This unit has a lease and cannot be removed.");
        }

        if (await db.RentalApplications.AnyAsync(app => app.UnitId == id, cancellationToken))
        {
            return DomainResult.Failure("This unit has rental applications and cannot be removed.");
        }

        db.Units.Remove(unit);
        await db.SaveChangesAsync(cancellationToken);
        return DomainResult.Success();
    }
}
