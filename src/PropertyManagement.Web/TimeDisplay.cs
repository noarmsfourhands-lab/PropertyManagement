using System.Globalization;
using Microsoft.AspNetCore.Html;

namespace PropertyManagement.Web;

/// <summary>
/// Renders a stored instant for a reader.
///
/// The obvious spelling is <c>value.ToLocalTime()</c>, and it is wrong on a server: it converts to
/// the <em>server's</em> timezone, not the reader's, so the same audit entry reads differently
/// depending on which machine rendered it, and identically for two people in different countries.
///
/// This emits the instant twice instead. The machine-readable copy in the <c>datetime</c>
/// attribute is the UTC value; the text is a UTC fallback that a browser with scripting turned off
/// still reads correctly, labelled as UTC so it is not mistaken for local. The small script in the
/// layout then rewrites the text in whatever locale and zone the reader's browser actually has.
/// </summary>
public static class TimeDisplay
{
    /// <summary>A date and time, such as an audit entry.</summary>
    public static IHtmlContent Moment(DateTime utc) => Render(utc, "d MMM yyyy, HH:mm 'UTC'", "moment");

    /// <summary>A date on its own, where the time of day is not the point.</summary>
    public static IHtmlContent Day(DateTime utc) => Render(utc, "d MMM yyyy", "day");

    private static IHtmlContent Render(DateTime utc, string fallbackFormat, string style)
    {
        var instant = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        var builder = new HtmlContentBuilder();
        builder.AppendHtml("<time datetime=\"");
        builder.Append(instant.ToString("O", CultureInfo.InvariantCulture));
        builder.AppendHtml("\" data-local=\"");
        builder.Append(style);
        builder.AppendHtml("\">");
        builder.Append(instant.ToString(fallbackFormat, CultureInfo.InvariantCulture));
        builder.AppendHtml("</time>");

        return builder;
    }
}
