using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Application.Services;
using PropertyManagement.Web.Models;

namespace PropertyManagement.Web.Controllers;

/// <summary>
/// The landing page. One route, two audiences: a property manager arrives at a dashboard of what
/// needs doing, an applicant at the units they could apply for. Both are what that person opened
/// the application to find out, which is the job of a landing page.
/// </summary>
public class HomeController(IDashboardService dashboard, TimeProvider timeProvider) : Controller
{
    /// <remarks>
    /// Never cached. One URL serves two different people's content here, so anything caching on
    /// the URL alone, a reverse proxy or a CDN, would serve a manager's dashboard to an applicant.
    /// </remarks>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!User.IsPropertyManager())
        {
            return View();
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var model = await dashboard.GetManagerDashboardAsync(
            User.GetUserId(),
            today,
            cancellationToken);

        return View("Dashboard", model);
    }

    /// <summary>
    /// Anonymous, because the whole application is authenticated by default and an error page that
    /// redirects to sign-in would hide the very failure it exists to report.
    /// </summary>
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
