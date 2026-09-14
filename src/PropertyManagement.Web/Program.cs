using Microsoft.AspNetCore.Mvc.Authorization;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Infrastructure;
using PropertyManagement.Infrastructure.Seeding;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

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

builder.Services.AddOpenApi();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

var app = builder.Build();

// Create the database, apply migrations and seed before the first request is served.
await using (var scope = app.Services.CreateAsyncScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
    await seeder.SeedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
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

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

/// <summary>Policy names used by the controllers.</summary>
public static class AuthorizationPolicies
{
    public const string PropertyManager = nameof(PropertyManager);

    public const string Applicant = nameof(Applicant);
}
