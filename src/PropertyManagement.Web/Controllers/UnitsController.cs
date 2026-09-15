using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Application.Services;
using PropertyManagement.Web.ViewModels.Properties;

namespace PropertyManagement.Web.Controllers;

/// <summary>
/// Browsing what is available to rent.
///
/// The home page shows a handful through the available-units view component; this is the full
/// list, paged, for an applicant deciding what to apply for. Property managers can read it too,
/// since seeing what is standing empty is useful to them as well.
/// </summary>
public class UnitsController(IPropertyService properties, TimeProvider timeProvider) : Controller
{
    private const int PageSize = 12;

    [HttpGet]
    public async Task<IActionResult> Browse(int page = 1, CancellationToken cancellationToken = default)
    {
        var current = Math.Max(page, 1);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        // Availability, the count and the page are all decided by the database.
        var results = await properties.GetAvailableUnitsAsync(
            today,
            PageSize,
            (current - 1) * PageSize,
            cancellationToken);

        // A page number past the end lands on an empty list rather than an error; sending the
        // reader back to the first page would silently discard what they asked for.
        return View(new AvailableUnitsBrowseViewModel
        {
            Units = results.Units,
            TotalCount = results.TotalCount,
            Page = current,
            PageSize = PageSize,
            AsOf = today,
            CanApply = User.IsApplicant()
        });
    }
}
