using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace PropertyManagement.Web.ViewComponents;

/// <summary>How a column's value is drawn.</summary>
public enum DataGridRender
{
    /// <summary>Plain text.</summary>
    Text = 0,

    /// <summary>A badge, coloured by the class named in <see cref="DataGridColumn.ClassField"/>.</summary>
    Badge = 1,

    /// <summary>A link, whose href is the field's value and whose text is <see cref="DataGridColumn.LinkText"/>.</summary>
    Link = 2,

    /// <summary>
    /// An instant, sent as ISO-8601 and formatted in the reader's own locale and timezone. The
    /// server cannot know either, so it must not be the one to format it.
    /// </summary>
    Date = 3
}

public enum DataGridAlign
{
    Start = 0,
    End = 1
}

/// <summary>
/// One column of a grid. <paramref name="Field"/> names a property on the JSON row, so the grid
/// never needs to know what the rows mean.
/// </summary>
/// <param name="Field">The row property to read.</param>
/// <param name="Header">The column heading.</param>
/// <param name="Sort">The value sent as the sort key when this column is clicked. Null means not sortable.</param>
/// <param name="Render">How to draw the value.</param>
/// <param name="Align">Which edge to align to.</param>
/// <param name="ClassField">For a badge, the row property holding its contextual class.</param>
/// <param name="LinkText">For a link, the text to show.</param>
/// <param name="EmptyText">What to show when the value is absent.</param>
public record DataGridColumn(
    string Field,
    string Header,
    string? Sort = null,
    DataGridRender Render = DataGridRender.Text,
    DataGridAlign Align = DataGridAlign.Start,
    string? ClassField = null,
    string? LinkText = null,
    string? EmptyText = null)
{
    public bool Sortable => !string.IsNullOrWhiteSpace(Sort);
}

/// <summary>A dropdown that narrows what the grid asks for.</summary>
/// <param name="Name">The query string parameter it sets.</param>
/// <param name="Label">The label shown above it.</param>
/// <param name="AnyLabel">The text of the "no filter" option.</param>
/// <param name="Options">The choices.</param>
/// <param name="Value">The current value, or null for no filter.</param>
public record DataGridFilter(
    string Name,
    string Label,
    string AnyLabel,
    IReadOnlyList<SelectListItem> Options,
    string? Value);

/// <summary>Everything a grid needs to render itself and then keep itself up to date.</summary>
public class DataGridModel
{
    /// <summary>Distinct per grid, so more than one can sit on a page.</summary>
    public required string Id { get; init; }

    /// <summary>The JSON endpoint returning { rows, total, page, pageSize, pageCount }.</summary>
    public required string Endpoint { get; init; }

    public required IReadOnlyList<DataGridColumn> Columns { get; init; }

    public IReadOnlyList<DataGridFilter> Filters { get; init; } = [];

    public int PageSize { get; init; } = 20;

    public string? Sort { get; init; }

    public bool Descending { get; init; } = true;

    public string EmptyMessage { get; init; } = "Nothing to show.";

    /// <summary>Shown while the first page is on its way, so the grid is never a blank rectangle.</summary>
    public string LoadingMessage { get; init; } = "Loading…";
}

/// <summary>
/// A table that fetches its own rows from a JSON endpoint and handles its own sorting, filtering
/// and paging.
///
/// It is deliberately ignorant of what it is showing: columns name properties on the returned
/// rows, and anything that needs a decision, such as a status label or the colour of its badge, is
/// decided by the endpoint and sent as a field. That is what lets the same component render a
/// different list without being changed.
/// </summary>
public class DataGridViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(DataGridModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return View(model);
    }
}
