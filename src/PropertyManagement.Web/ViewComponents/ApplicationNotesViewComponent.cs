using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Application.Services;
using PropertyManagement.Web.ViewModels.Applications;

namespace PropertyManagement.Web.ViewComponents;

/// <summary>
/// Internal notes on an application.
///
/// Like the history panel, the component makes its own access decision rather than trusting the
/// page that hosts it: an applicant gets nothing at all, not an empty panel that hints something
/// is being kept about them. That is the second of the three places the rule is enforced, the
/// others being the controller's policy and the fact that no applicant-facing model projects the
/// type.
/// </summary>
public class ApplicationNotesViewComponent(INoteService notes) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int applicationId)
    {
        if (!UserClaimsPrincipal.IsInRole(UserRole.PropertyManager))
        {
            return Content(string.Empty);
        }

        return View(new NoteListViewModel
        {
            ApplicationId = applicationId,
            Notes = await notes.GetNotesAsync(applicationId, HttpContext.RequestAborted)
        });
    }
}
