using System.Security.Claims;
using PropertyManagement.Application.Services;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Rules;

namespace PropertyManagement.Web;

/// <summary>
/// One load of an application together with the permission answers every action needs, so no action
/// re-derives them and no two of them can disagree.
///
/// This is the heavyweight answer: it carries the whole application because the actions that ask for
/// it go on to render it. Where only the permission question matters and nothing will be drawn,
/// <see cref="IRentalApplicationService.GetAccessAsync"/> answers it in one query instead.
/// </summary>
public sealed record ApplicationContext(
    RentalApplication Application,
    string UserId,
    bool IsApplicantOn,
    bool IsManager)
{
    /// <summary>A manager may read any application; an applicant only one they are on.</summary>
    public bool CanView => IsApplicantOn || IsManager;

    public bool CanEdit => ApplicationWorkflow.CanEdit(Application.Status, IsApplicantOn);
}

/// <summary>
/// Builds an <see cref="ApplicationContext"/> for whoever is asking.
///
/// The wizard and the residence modal are separate controllers over the same application, and both
/// have to answer the same two questions before doing anything: may this person see it, and may they
/// change it. Asking that in two places is how one of them quietly stops matching the other.
/// </summary>
public class ApplicationContextFactory(IRentalApplicationService applications)
{
    public async Task<ApplicationContext?> LoadAsync(
        int applicationId,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        var application = await applications.GetAsync(applicationId, cancellationToken);

        if (application is null)
        {
            return null;
        }

        var userId = user.GetUserId();

        return new ApplicationContext(
            application,
            userId,
            application.Applicants.Any(link => link.ApplicantUserId == userId),
            user.IsPropertyManager());
    }
}
