using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Infrastructure.Services;
using PropertyManagement.Web.ViewModels.Applications;

namespace PropertyManagement.Web.ViewComponents;

/// <summary>
/// Who is on an application, and the way to add or remove someone.
///
/// Like the other components on this page it decides for itself who may see it and what they may
/// do: a property manager reads the list, an applicant on an editable application is also offered
/// the controls, and anyone else gets nothing.
/// </summary>
public class ApplicationApplicantsViewComponent(
    IApplicationApplicantService applicants,
    IRentalApplicationService applications) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int applicationId)
    {
        var application = await applications.GetAsync(applicationId);

        if (application is null)
        {
            return Content(string.Empty);
        }

        var userId = UserClaimsPrincipal.GetUserId();
        var isApplicantOn = application.Applicants.Any(link => link.ApplicantUserId == userId);

        if (!isApplicantOn && !UserClaimsPrincipal.IsInRole(UserRole.PropertyManager))
        {
            return Content(string.Empty);
        }

        return View(new ApplicantListViewModel
        {
            ApplicationId = applicationId,
            Applicants = await applicants.GetApplicantsAsync(applicationId),
            CurrentUserId = userId,
            CanManage = ApplicationWorkflow.CanEdit(application.Status, isApplicantOn)
        });
    }
}
