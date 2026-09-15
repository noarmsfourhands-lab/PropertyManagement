using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Application.Services;
using PropertyManagement.Web.ViewModels.Applications;

namespace PropertyManagement.Web.Controllers.Api;

/// <summary>
/// A row of the application grid, already shaped for display.
///
/// The label and badge class are decided here rather than in the browser so that the grid
/// component stays generic: it renders whatever fields it is pointed at and owns no knowledge of
/// what an application status means.
/// </summary>
/// <param name="Id">The application's identifier.</param>
/// <param name="PropertyName">The property the unit belongs to.</param>
/// <param name="UnitNumber">The unit being applied for.</param>
/// <param name="ApplicantName">The name given on the application, empty until it has been filled in.</param>
/// <param name="Status">The status as a stable name, for callers that need the value rather than the label.</param>
/// <param name="StatusLabel">The status as it should be shown to a person.</param>
/// <param name="StatusClass">The class the chip is drawn with, from this application's own status palette.</param>
/// <param name="Submitted">
/// When the application was submitted, as an ISO-8601 instant, or empty when it has not been.
/// Deliberately not formatted here: the server knows neither the reader's timezone nor their
/// locale, so formatting it would give everyone the server's.
/// </param>
/// <param name="DetailUrl">Where to open the application.</param>
public record ApplicationRowDto(
    int Id,
    string PropertyName,
    string UnitNumber,
    string ApplicantName,
    string Status,
    string StatusLabel,
    string StatusClass,
    string Submitted,
    string DetailUrl);

/// <summary>One page of applications, together with the total the filter matched.</summary>
/// <param name="Rows">The applications on this page.</param>
/// <param name="Total">How many applications match the filter in total, ignoring paging.</param>
/// <param name="Page">The page number returned, starting at one.</param>
/// <param name="PageSize">How many rows a full page holds.</param>
/// <param name="PageCount">How many pages the filtered total covers.</param>
public record ApplicationPageDto(
    IReadOnlyList<ApplicationRowDto> Rows,
    int Total,
    int Page,
    int PageSize,
    int PageCount);

/// <summary>
/// Reads the rental application list as JSON, for the grid component on the applications page.
///
/// Filtering, sorting, counting and paging all happen in the database. The same ownership rule as
/// the page applies: an applicant sees only applications they are on, a property manager sees all
/// of them. Authorisation is not relaxed for being an API; the global authorize filter covers this
/// controller exactly as it covers the others.
/// </summary>
[ApiController]
[Route("api/applications")]
[Produces("application/json")]
public class ApplicationsApiController(IRentalApplicationService applications) : ControllerBase
{
    /// <summary>Returns one page of rental applications.</summary>
    /// <param name="status">Only applications in this status. Omit for any status.</param>
    /// <param name="propertyId">Only applications for units in this property. Omit for any property.</param>
    /// <param name="page">Page number, starting at one.</param>
    /// <param name="pageSize">Rows per page, from 1 to 100.</param>
    /// <param name="sort">Which column to order by.</param>
    /// <param name="descending">True for descending order, false for ascending.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <response code="200">The page of rows and the filtered total.</response>
    /// <response code="401">The request was not signed in.</response>
    [HttpGet(Name = "GetApplications")]
    [ProducesResponseType<ApplicationPageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApplicationPageDto>> Get(
        [FromQuery] ApplicationStatus? status = null,
        [FromQuery] int? propertyId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ApplicationSort sort = ApplicationSort.Submitted,
        [FromQuery] bool descending = true,
        CancellationToken cancellationToken = default)
    {
        var isManager = User.IsPropertyManager();

        var results = await applications.ListAsync(
            new ApplicationListFilter(status, propertyId, page, pageSize, sort, descending),
            isManager ? null : User.GetUserId(),
            cancellationToken);

        var rows = results.Rows
            .Select(row => new ApplicationRowDto(
                row.Id,
                row.PropertyName,
                row.UnitNumber,
                string.IsNullOrWhiteSpace(row.ApplicantName) ? "Not yet provided" : row.ApplicantName,
                row.Status.ToString(),
                ApplicationListViewModel.DisplayNameFor(row.Status),
                ApplicationListViewModel.BadgeClassFor(row.Status),
                row.SubmittedAtUtc is { } submitted
                    ? DateTime.SpecifyKind(submitted, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture)
                    : string.Empty,
                Url.Action("Edit", "RentalApplications", new { id = row.Id }) ?? string.Empty))
            .ToList();

        return Ok(new ApplicationPageDto(
            rows,
            results.TotalCount,
            results.Page,
            results.PageSize,
            results.PageCount));
    }
}
