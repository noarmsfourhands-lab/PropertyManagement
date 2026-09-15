using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Web.Models;

namespace PropertyManagement.Web.Controllers;

public class HomeController : Controller
{
    /// <summary>
    /// The landing page. Property managers are pointed at their properties; applicants see the
    /// units available right now.
    /// </summary>
    public IActionResult Index() => View();

    /// <summary>
    /// Anonymous, because the whole application is authenticated by default and an error page that
    /// redirects to sign-in would hide the very failure it exists to report.
    /// </summary>
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
