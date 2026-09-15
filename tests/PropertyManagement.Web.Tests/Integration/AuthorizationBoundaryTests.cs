using System.Net;

namespace PropertyManagement.Web.Tests.Integration;

/// <summary>
/// What each kind of visitor actually gets back from the pipeline.
///
/// These are the tests that would have caught the two direct-reference holes: a service refusing an
/// operation is not the same as a route refusing it, and only a request through the real pipeline
/// shows which one the browser is talking to.
/// </summary>
public class AuthorizationBoundaryTests(PropertyManagementApplication app)
    : IClassFixture<PropertyManagementApplication>
{
    [Fact]
    public async Task An_anonymous_visitor_is_sent_to_the_login_page()
    {
        var response = await app.SignedOut().GetAsync($"/RentalApplications/Edit/{app.ApplicationId}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_applicant_on_an_application_may_open_it()
    {
        var client = await app.SignInAsync(PropertyManagementApplication.OwnerEmail);

        var response = await client.GetAsync($"/RentalApplications/Edit/{app.ApplicationId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Another_applicant_cannot_tell_that_the_application_exists()
    {
        var client = await app.SignInAsync(PropertyManagementApplication.StrangerEmail);

        var response = await client.GetAsync($"/RentalApplications/Edit/{app.ApplicationId}");

        // 404 rather than 403 on purpose: a 403 confirms the id is real, which is the whole of
        // what someone walking the numbers is trying to learn.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_property_manager_may_open_any_application()
    {
        var client = await app.SignInAsync(PropertyManagementApplication.ManagerEmail);

        var response = await client.GetAsync($"/RentalApplications/Edit/{app.ApplicationId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Another_applicant_cannot_read_who_is_on_the_application()
    {
        var client = await app.SignInAsync(PropertyManagementApplication.StrangerEmail);

        var response = await client.GetAsync($"/Applicants/ListPartial/{app.ApplicationId}");

        // The same answer the application's own pages give, rather than a 403 from this one route
        // quietly confirming what the others refuse to.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task An_applicant_cannot_reach_the_review_screen_for_their_own_application()
    {
        var client = await app.SignInAsync(PropertyManagementApplication.OwnerEmail);

        var response = await client.GetAsync($"/Review/Form/{app.ApplicationId}");

        // Being on the application is not the same as being allowed to decide it. The policy on
        // the controller refuses before any of its own code runs.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("AccessDenied", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_applicant_cannot_read_an_applications_internal_notes()
    {
        var client = await app.SignInAsync(PropertyManagementApplication.OwnerEmail);

        var response = await client.GetAsync($"/Notes/ListPartial/{app.ApplicationId}");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_applicant_cannot_edit_the_property_catalogue()
    {
        var client = await app.SignInAsync(PropertyManagementApplication.OwnerEmail);

        var response = await client.GetAsync("/Properties/Form");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task A_post_without_an_antiforgery_token_is_rejected()
    {
        var client = await app.SignInAsync(PropertyManagementApplication.OwnerEmail);

        var response = await client.PostAsync(
            "/RentalApplications/Withdraw",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["id"] = app.ApplicationId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_grid_endpoint_shows_an_applicant_only_what_they_are_on()
    {
        var owner = await app.SignInAsync(PropertyManagementApplication.OwnerEmail);
        var stranger = await app.SignInAsync(PropertyManagementApplication.StrangerEmail);

        var mine = await owner.GetStringAsync("/api/applications?page=1");
        var theirs = await stranger.GetStringAsync("/api/applications?page=1");

        // The endpoint is open to both roles and scopes its rows instead, so the security property
        // to assert is which rows come back, not the status code.
        Assert.Contains($"\"id\":{app.ApplicationId}", mine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"\"id\":{app.ApplicationId}", theirs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_grid_endpoint_answers_an_anonymous_caller_with_a_status_code()
    {
        var response = await app.SignedOut().GetAsync("/api/applications?page=1");

        // Not a redirect to the login page: a fetch() following that would parse an HTML sign-in
        // form as the grid's JSON, and the grid would report a parse error instead of "signed out".
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
