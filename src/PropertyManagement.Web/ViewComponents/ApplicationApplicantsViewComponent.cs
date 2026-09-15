using Microsoft.AspNetCore.Mvc;

namespace PropertyManagement.Web.ViewComponents;

/// <summary>
/// Who is on an application, and the way to add or remove someone.
///
/// Like the other components on this page it decides for itself who may see it and what they may
/// do, rather than trusting the page that hosts it. The decision itself lives in
/// <see cref="ApplicantListFactory"/>, shared with the controller that re-renders this same region
/// after a change, so there is one rule rather than two copies of one.
/// </summary>
public class ApplicationApplicantsViewComponent(ApplicantListFactory factory) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int applicationId)
    {
        var model = await factory.BuildAsync(applicationId, UserClaimsPrincipal, HttpContext.RequestAborted);

        return model is null ? Content(string.Empty) : View(model);
    }
}
