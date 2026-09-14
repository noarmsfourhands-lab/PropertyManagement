using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Infrastructure.Services;
using PropertyManagement.Web.ViewModels.Properties;

namespace PropertyManagement.Web.Controllers;

/// <summary>
/// Property and unit maintenance, which only a property manager may perform. Every add, edit and
/// remove happens through a modal whose body is a partial view rendered by the actions below.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.PropertyManager)]
public class PropertiesController(IPropertyService properties) : ModalController
{
    private const string PropertyFormPartial = "_PropertyForm";
    private const string UnitFormPartial = "_UnitForm";
    private const string PropertyListPartial = "_PropertyList";
    private const string UnitListPartialName = "_UnitList";

    // ---------------------------------------------------------------- Properties

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var all = await properties.GetPropertiesAsync(cancellationToken);
        return View(all);
    }

    /// <summary>The list region on its own, re-fetched after a modal saves.</summary>
    [HttpGet]
    public async Task<IActionResult> ListPartial(CancellationToken cancellationToken)
    {
        var all = await properties.GetPropertiesAsync(cancellationToken);
        return PartialView(PropertyListPartial, all);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var property = await properties.GetPropertyAsync(id, cancellationToken);

        return property is null
            ? NotFound()
            : View(new PropertyDetailsViewModel { Property = property });
    }

    [HttpGet]
    public async Task<IActionResult> UnitListPartial(int id, CancellationToken cancellationToken)
    {
        var property = await properties.GetPropertyAsync(id, cancellationToken);

        return property is null
            ? NotFound()
            : PartialView(UnitListPartialName, new PropertyDetailsViewModel { Property = property });
    }

    /// <summary>Returns the modal body for adding or editing a property.</summary>
    [HttpGet]
    public async Task<IActionResult> Form(int id, CancellationToken cancellationToken)
    {
        if (id == 0)
        {
            return PartialView(PropertyFormPartial, new PropertyFormViewModel());
        }

        var property = await properties.GetPropertyAsync(id, cancellationToken);

        return property is null
            ? NotFound()
            : PartialView(PropertyFormPartial, PropertyFormViewModel.From(property));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(PropertyFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return ModalValidationFailed(PropertyFormPartial, model);
        }

        var result = await properties.SavePropertyAsync(model.ToInput(), cancellationToken);

        if (result.Failed)
        {
            AddError(null, result.Error!);
            return ModalValidationFailed(PropertyFormPartial, model);
        }

        return ModalSucceeded(
            Url.Action(nameof(ListPartial))!,
            "#property-list",
            model.IsNew ? $"Added {model.Name}." : $"Saved {model.Name}.");
    }

    /// <summary>Returns the confirmation modal body for removing a property.</summary>
    [HttpGet]
    public async Task<IActionResult> ConfirmDelete(int id, CancellationToken cancellationToken)
    {
        var property = await properties.GetPropertyAsync(id, cancellationToken);

        return property is null
            ? NotFound()
            : PartialView("_ConfirmDeleteProperty", property);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await properties.DeletePropertyAsync(id, cancellationToken);

        if (result.Failed)
        {
            var property = await properties.GetPropertyAsync(id, cancellationToken);

            if (property is null)
            {
                return NotFound();
            }

            AddError(null, result.Error!);
            return ModalValidationFailed("_ConfirmDeleteProperty", property);
        }

        return ModalSucceeded(Url.Action(nameof(ListPartial))!, "#property-list", "Property removed.");
    }

    // ---------------------------------------------------------------- Units

    /// <summary>
    /// Returns the modal body for adding or editing a unit, with the unit-type choices already
    /// filtered by the rule that governs retired types.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> UnitForm(int id, int propertyId, CancellationToken cancellationToken)
    {
        UnitFormViewModel model;

        if (id == 0)
        {
            var property = await properties.GetPropertyAsync(propertyId, cancellationToken);

            if (property is null)
            {
                return NotFound();
            }

            model = new UnitFormViewModel { PropertyId = propertyId, PropertyName = property.Name };
        }
        else
        {
            var unit = await properties.GetUnitAsync(id, cancellationToken);

            if (unit is null)
            {
                return NotFound();
            }

            model = UnitFormViewModel.From(unit);
        }

        await PopulateUnitTypesAsync(model, cancellationToken);
        return PartialView(UnitFormPartial, model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveUnit(UnitFormViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            await PopulateUnitTypesAsync(model, cancellationToken);
            return ModalValidationFailed(UnitFormPartial, model);
        }

        var result = await properties.SaveUnitAsync(model.ToInput(), cancellationToken);

        if (result.Failed)
        {
            // Rule failures about the type belong on the type field, not in the summary.
            AddError(result.Error!.Contains("Unit type", StringComparison.Ordinal)
                ? nameof(model.UnitTypeId)
                : null, result.Error);

            await PopulateUnitTypesAsync(model, cancellationToken);
            return ModalValidationFailed(UnitFormPartial, model);
        }

        return ModalSucceeded(
            Url.Action(nameof(UnitListPartial), new { id = model.PropertyId })!,
            "#unit-list",
            model.IsNew ? $"Added unit {model.UnitNumber}." : $"Saved unit {model.UnitNumber}.");
    }

    [HttpGet]
    public async Task<IActionResult> ConfirmDeleteUnit(int id, CancellationToken cancellationToken)
    {
        var unit = await properties.GetUnitAsync(id, cancellationToken);

        return unit is null
            ? NotFound()
            : PartialView("_ConfirmDeleteUnit", unit);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUnit(int id, CancellationToken cancellationToken)
    {
        var unit = await properties.GetUnitAsync(id, cancellationToken);

        if (unit is null)
        {
            return NotFound();
        }

        var result = await properties.DeleteUnitAsync(id, cancellationToken);

        if (result.Failed)
        {
            AddError(null, result.Error!);
            return ModalValidationFailed("_ConfirmDeleteUnit", unit);
        }

        return ModalSucceeded(
            Url.Action(nameof(UnitListPartial), new { id = unit.PropertyId })!,
            "#unit-list",
            $"Unit {unit.UnitNumber} removed.");
    }

    private async Task PopulateUnitTypesAsync(UnitFormViewModel model, CancellationToken cancellationToken)
    {
        // A new unit has no current type, so only active types are offered.
        var currentUnitTypeId = model.IsNew
            ? (int?)null
            : (await properties.GetUnitAsync(model.Id, cancellationToken))?.UnitTypeId;

        var choices = await properties.GetSelectableUnitTypesAsync(currentUnitTypeId, cancellationToken);
        model.SetUnitTypeChoices(choices);
    }
}
