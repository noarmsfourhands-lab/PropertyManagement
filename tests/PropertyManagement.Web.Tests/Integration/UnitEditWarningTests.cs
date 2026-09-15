using System.Net;

namespace PropertyManagement.Web.Tests.Integration;

/// <summary>
/// Editing a unit that applications already point at.
///
/// A rental application stores which unit it is for, not a copy of that unit, so every page showing
/// an application reads the rent, type and bedroom count from the unit as it stands now. Changing a
/// unit therefore rewrites what every application against it appears to say, submitted and decided
/// ones included. The manager is told what they are about to affect, and has to say they read it.
///
/// The check is here rather than only in the page, because a post that simply omitted the field
/// would otherwise sail through the one guard that exists.
/// </summary>
public class UnitEditWarningTests(PropertyManagementApplication app)
    : IClassFixture<PropertyManagementApplication>
{
    private static Dictionary<string, string> UnitForm(int unitId, string token, bool acknowledged) =>
        new()
        {
            ["Id"] = unitId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["PropertyId"] = "1",
            ["UnitNumber"] = "2A",
            ["Bedrooms"] = "2",
            ["MonthlyRent"] = "1600",
            ["UnitTypeId"] = "1",
            ["Acknowledged"] = acknowledged ? "true" : "false",
            ["__RequestVerificationToken"] = token
        };

    [Fact]
    public async Task The_form_warns_about_the_applications_already_on_the_unit()
    {
        var client = await app.SignInAsync(PropertyManagementApplication.ManagerEmail);

        var html = await client.GetStringAsync($"/Properties/UnitForm/{app.UnitId}?propertyId=1");

        Assert.Contains("already reference", html, StringComparison.Ordinal);
        Assert.Contains("Acknowledged", html, StringComparison.Ordinal);

        // The fixture's one application is a draft, so that is the status the count sits against.
        Assert.Contains("Draft", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_without_reading_the_warning_is_refused()
    {
        var client = await app.SignInAsync(PropertyManagementApplication.ManagerEmail);

        var form = await client.GetStringAsync($"/Properties/UnitForm/{app.UnitId}?propertyId=1");
        var response = await client.PostAsync(
            "/Properties/SaveUnit",
            new FormUrlEncodedContent(UnitForm(app.UnitId, Antiforgery(form), acknowledged: false)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(
            "Confirm you have read what this changes",
            await response.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Saving_after_reading_it_goes_through()
    {
        var client = await app.SignInAsync(PropertyManagementApplication.ManagerEmail);

        var form = await client.GetStringAsync($"/Properties/UnitForm/{app.UnitId}?propertyId=1");
        var response = await client.PostAsync(
            "/Properties/SaveUnit",
            new FormUrlEncodedContent(UnitForm(app.UnitId, Antiforgery(form), acknowledged: true)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Adding_a_unit_is_never_asked_to_acknowledge_anything()
    {
        var client = await app.SignInAsync(PropertyManagementApplication.ManagerEmail);

        var html = await client.GetStringAsync("/Properties/UnitForm/0?propertyId=1");

        // Nothing can reference a unit that does not exist, so asking would be noise. More to the
        // point, the server-side check must not refuse a new unit for failing to tick a box that
        // was never rendered.
        Assert.DoesNotContain("already reference", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Acknowledged", html, StringComparison.Ordinal);
    }

    private static string Antiforgery(string html)
    {
        const string Marker = "name=\"__RequestVerificationToken\"";

        var field = html.IndexOf(Marker, StringComparison.Ordinal);

        Assert.True(field >= 0, "The unit form did not render an antiforgery field.");

        var value = html.IndexOf("value=\"", field, StringComparison.Ordinal) + "value=\"".Length;

        return html[value..html.IndexOf('"', value)];
    }
}
