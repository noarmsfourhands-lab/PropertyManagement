using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Entities;

namespace PropertyManagement.Domain.Rules;

/// <summary>
/// An inactive unit type still displays on a unit that already uses it, but cannot be selected
/// for any other unit. Enforced here and called from the server before any unit is saved.
/// </summary>
public static class UnitTypeSelection
{
    /// <summary>
    /// Whether <paramref name="unitType"/> may be assigned to a unit.
    /// </summary>
    /// <param name="currentUnitTypeId">
    /// The type the unit already carries, or null when the unit is new. An inactive type is
    /// allowed only when it is the one already on the unit, so editing an unrelated field on an
    /// older unit does not force the manager to change its type.
    /// </param>
    public static bool CanAssign(UnitType unitType, int? currentUnitTypeId)
    {
        ArgumentNullException.ThrowIfNull(unitType);
        return unitType.IsActive || unitType.Id == currentUnitTypeId;
    }

    public static DomainResult Validate(UnitType unitType, int? currentUnitTypeId) =>
        CanAssign(unitType, currentUnitTypeId)
            ? DomainResult.Success()
            : DomainResult.Failure($"Unit type '{unitType.Name}' is inactive and cannot be selected.");

    /// <summary>
    /// The types to offer in a dropdown: every active type, plus the unit's current type when
    /// that type has since been deactivated.
    /// </summary>
    public static IReadOnlyList<UnitType> SelectableFor(
        IEnumerable<UnitType> allTypes,
        int? currentUnitTypeId)
    {
        ArgumentNullException.ThrowIfNull(allTypes);
        return allTypes
            .Where(type => CanAssign(type, currentUnitTypeId))
            .OrderBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
