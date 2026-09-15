using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Data.SqlClient;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Infrastructure.Seeding;
using PropertyManagement.Infrastructure;
using PropertyManagement.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

// Shared by the view component that renders the applicants region and the controller that
// re-renders it, so the rule about who may see and change it is written once.
builder.Services.AddScoped<ApplicantListFactory>();

// Shared by the wizard and the residence modal, which are separate controllers over the same
// application and must answer "may this person see it, may they change it" the same way.
builder.Services.AddScoped<ApplicationContextFactory>();

builder.Services.AddAuthorization(options =>
{
    // Named policies rather than role strings scattered through the controllers.
    options.AddPolicy(AuthorizationPolicies.PropertyManager, policy =>
        policy.RequireRole(UserRole.PropertyManager));

    options.AddPolicy(AuthorizationPolicies.Applicant, policy =>
        policy.RequireRole(UserRole.Applicant));
});

builder.Services.AddControllersWithViews(options =>
{
    // Authenticated by default; anything public opts out with [AllowAnonymous].
    options.Filters.Add(new AuthorizeFilter());
});

// Describes the JSON endpoints the application exposes. The XML comments on those endpoints are
// picked up here, which is why the web project generates a documentation file.
builder.Services.AddOpenApi();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;

    // A page that needs a sign-in should go to the sign-in page. A JSON request should be told
    // plainly that it is unauthorised, because redirecting it would hand a caller expecting data
    // a page of HTML and a success status.
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };

    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});

// Data Protection encrypts the authentication cookie and the antiforgery token. Left alone it
// writes its keys wherever the host suggests, which inside a container is the container's own
// filesystem: every restart issues a fresh key and signs everyone out, and a second replica cannot
// read the first one's cookies at all. Pointing it at a mounted directory fixes both. Unset, the
// default still applies, so nothing changes for someone running this from Visual Studio.
var keyPath = builder.Configuration["DataProtection:KeyPath"];

if (!string.IsNullOrWhiteSpace(keyPath))
{
    builder.Services
        .AddDataProtection()
        .SetApplicationName("PropertyManagement")
        .PersistKeysToFileSystem(new DirectoryInfo(keyPath));
}

var app = builder.Build();

// Create the database, apply migrations and seed before the first request is served.
// Starting without a usable database is treated as fatal, but the reason is reported plainly
// rather than as a wall of provider stack trace.
await using (var scope = app.Services.CreateAsyncScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();

        // Migrating from the web process is convenient for a single instance and wrong for several:
        // every replica racing the same migration is a known way to corrupt a deployment. The
        // switch lets a real deployment run migrations as its own step and start the app with this
        // off, and it is what the integration tests use to boot against a schema they built
        // themselves. It defaults on, so nothing changes for someone cloning this and pressing F5.
        if (builder.Configuration.GetValue("Database:MigrateOnStartup", defaultValue: true))
        {
            await seeder.SeedAsync();
        }
        else
        {
            await seeder.SeedDataAsync();
        }
    }
    catch (SqlException exception)
    {
        logger.LogCritical(
            exception,
            "Could not reach SQL Server. Check the DefaultConnection connection string and that the "
            + "instance is running. The README covers SQL Server Express and LocalDB setup.");

        throw;
    }
}

if (app.Environment.IsDevelopment())
{
    // The OpenAPI document, served at /openapi/v1.json while developing.
    app.MapOpenApi().AllowAnonymous();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Attribute-routed controllers, which is how the JSON endpoints are reached.
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

/// <summary>
/// Named so the integration tests can boot this exact pipeline through WebApplicationFactory.
/// Top-level statements otherwise generate an internal Program that a test project cannot see,
/// and a test host built from a hand-written copy of the wiring would not be testing this app.
/// </summary>
public partial class Program;
