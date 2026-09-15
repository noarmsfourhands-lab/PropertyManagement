using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Application.Services;

namespace PropertyManagement.Web.ViewComponents;

/// <summary>
/// The status and review history of one application: who changed it, when, and with what comment.
///
/// It is a view component rather than a partial because it runs its own query and makes its own
/// access decision, and because it appears on a page that is otherwise about the application's
/// contents. The role check happens here rather than in the calling view: a component that decides
/// for itself cannot be dropped onto a page that forgets to guard it.
/// </summary>
public class ApplicationHistoryViewComponent(IReviewService review) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(int applicationId)
    {
        // History is for property managers. An applicant gets nothing at all, not an empty panel.
        if (!UserClaimsPrincipal.IsInRole(UserRole.PropertyManager))
        {
            return Content(string.Empty);
        }

        var entries = await review.GetHistoryAsync(applicationId, HttpContext.RequestAborted);

        return View(new ApplicationHistoryModel { Entries = entries });
    }
}

public class ApplicationHistoryModel
{
    public required IReadOnlyList<ApplicationEvent> Entries { get; init; }
}
