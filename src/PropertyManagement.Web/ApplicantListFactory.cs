using System.Security.Claims;
using PropertyManagement.Application.Services;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Web.ViewModels.Applications;

namespace PropertyManagement.Web;

/// <summary>
/// Builds the "who is on this application" region, and decides who may see it.
///
/// Both the view component that renders the region on the edit page and the controller that
/// re-renders it after a change need the same answer to the same question. Asking it in two places
/// means a rule can be tightened in one and left alone in the other, which is how a permission
/// check quietly stops covering one route. It is asked here once instead.
/// </summary>
public class ApplicantListFactory(
    IApplicationApplicantService applicants,
    IRentalApplicationService applications)
{
    /// <summary>
    /// The region's model, or null when this user has no business seeing the application at all.
    /// A property manager may look; only an applicant on it may change who else is on it.
    /// </summary>
    public async Task<ApplicantListViewModel?> BuildAsync(
        int applicationId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var userId = user.GetUserId();
        var access = await applications.GetAccessAsync(applicationId, userId, cancellationToken);

        if (access is null || (!access.IsApplicantOn && !user.IsPropertyManager()))
        {
            return null;
        }

        return new ApplicantListViewModel
        {
            ApplicationId = applicationId,
            Applicants = await applicants.GetApplicantsAsync(applicationId, cancellationToken),
            CurrentUserId = userId,
            CanManage = ApplicationWorkflow.CanEdit(access.Status, access.IsApplicantOn)
        };
    }
}
