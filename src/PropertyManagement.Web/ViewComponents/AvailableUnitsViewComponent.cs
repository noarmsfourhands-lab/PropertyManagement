using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Infrastructure.Services;

namespace PropertyManagement.Web.ViewComponents;

/// <summary>
/// The units an applicant can apply for right now.
///
/// A view component rather than a partial view because it has work of its own to do: it resolves
/// the current date, asks the database which units are free, and decides how many to show. A
/// partial view only renders a model it is handed, so anything that needs its own dependencies
/// and its own query belongs here. It can then be dropped into any page without that page's
/// controller having to know about units at all.
/// </summary>
public class AvailableUnitsViewComponent(IPropertyService properties, TimeProvider timeProvider)
    : ViewComponent
{
    /// <param name="maximum">
    /// How many units to render. A dashboard shows a handful; a browse page passes a larger number.
    /// </param>
    public async Task<IViewComponentResult> InvokeAsync(int maximum = 6)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        // Availability is decided in the database, and only the requested slice is materialised.
        var available = await properties.GetAvailableUnitsAsync(today, maximum);

        var model = new AvailableUnitsModel
        {
            Units = available.Units,
            TotalAvailable = available.TotalCount,
            AsOf = today
        };

        return View(model);
    }
}

/// <summary>What the available-units view component renders.</summary>
public class AvailableUnitsModel
{
    public required IReadOnlyList<Unit> Units { get; init; }

    public required int TotalAvailable { get; init; }

    public required DateOnly AsOf { get; init; }

    public bool HasMore => TotalAvailable > Units.Count;
}
