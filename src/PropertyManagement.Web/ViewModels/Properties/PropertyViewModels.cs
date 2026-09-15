using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Application.Services;

namespace PropertyManagement.Web.ViewModels.Properties;

/// <summary>Backs the add and edit property modal. Id is zero when adding.</summary>
public class PropertyFormViewModel
{
    public int Id { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "Property name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    [Display(Name = "Address")]
    public string AddressLine1 { get; set; } = string.Empty;

    [StringLength(200)]
    [Display(Name = "Address line 2")]
    public string? AddressLine2 { get; set; }

    [Required]
    [StringLength(100)]
    [Display(Name = "City")]
    public string City { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    [Display(Name = "State")]
    public string State { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    [Display(Name = "ZIP code")]
    public string PostalCode { get; set; } = string.Empty;

    public bool IsNew => Id == 0;

    public string Title => IsNew ? "Add property" : "Edit property";

    public static PropertyFormViewModel From(Property property) => new()
    {
        Id = property.Id,
        Name = property.Name,
        AddressLine1 = property.AddressLine1,
        AddressLine2 = property.AddressLine2,
        City = property.City,
        State = property.State,
        PostalCode = property.PostalCode
    };

    public PropertyInput ToInput() =>
        new(Id, Name, AddressLine1, AddressLine2, City, State, PostalCode);
}

/// <summary>Backs the add and edit unit modal.</summary>
public class UnitFormViewModel
{
    public int Id { get; set; }

    public int PropertyId { get; set; }

    public string PropertyName { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    [Display(Name = "Unit number")]
    public string UnitNumber { get; set; } = string.Empty;

    [Range(0, 10)]
    [Display(Name = "Bedrooms")]
    public int Bedrooms { get; set; }

    [Range(0.01, 1_000_000)]
    [DataType(DataType.Currency)]
    [Display(Name = "Monthly rent")]
    public decimal MonthlyRent { get; set; }

    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Select a unit type.")]
    [Display(Name = "Unit type")]
    public int UnitTypeId { get; set; }

    /// <summary>
    /// Built from the server-side rule, so a retired type appears only on the unit that already
    /// uses it. The same rule runs again on post; this list is a convenience, not the guard.
    /// </summary>
    public IEnumerable<SelectListItem> UnitTypeChoices { get; set; } = [];

    /// <summary>
    /// What already references this unit. Empty when adding one, and when nothing does.
    /// </summary>
    public UnitApplicationImpact Impact { get; set; } = UnitApplicationImpact.None;

    /// <summary>
    /// Whether the manager has read the warning. Only asked for when something references the unit,
    /// and checked on the server as well as rendered, because a post that simply omits the field
    /// would otherwise skip the whole thing.
    /// </summary>
    [Display(Name = "I understand this changes what those applications show")]
    public bool Acknowledged { get; set; }

    public bool IsNew => Id == 0;

    /// <summary>Whether to ask before saving. Never for a unit that does not exist yet.</summary>
    public bool NeedsAcknowledgement => !IsNew && Impact.Any;

    public string Title => IsNew ? "Add unit" : $"Edit unit {UnitNumber}";

    public static UnitFormViewModel From(Unit unit) => new()
    {
        Id = unit.Id,
        PropertyId = unit.PropertyId,
        PropertyName = unit.Property?.Name ?? string.Empty,
        UnitNumber = unit.UnitNumber,
        Bedrooms = unit.Bedrooms,
        MonthlyRent = unit.MonthlyRent,
        UnitTypeId = unit.UnitTypeId
    };

    public UnitInput ToInput() =>
        new(Id, PropertyId, UnitNumber, Bedrooms, MonthlyRent, UnitTypeId);

    public void SetUnitTypeChoices(IEnumerable<UnitType> unitTypes)
    {
        UnitTypeChoices = unitTypes.Select(unitType => new SelectListItem
        {
            Value = unitType.Id.ToString(),
            Text = unitType.IsActive ? unitType.Name : $"{unitType.Name} (retired)",
            Selected = unitType.Id == UnitTypeId
        }).ToList();
    }
}

/// <summary>A property and its units, as the details page shows them.</summary>
public class PropertyDetailsViewModel
{
    public required Property Property { get; init; }

    public IReadOnlyList<Unit> Units => Property.Units.OrderBy(unit => unit.UnitNumber).ToList();
}

/// <summary>The paged list of units an applicant can apply for right now.</summary>
public class AvailableUnitsBrowseViewModel
{
    public required IReadOnlyList<Unit> Units { get; init; }

    public required int TotalCount { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }

    public required DateOnly AsOf { get; init; }

    /// <summary>Only an applicant is offered the Apply button; a manager is here to look.</summary>
    public required bool CanApply { get; init; }

    public int PageCount => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < PageCount;
}
