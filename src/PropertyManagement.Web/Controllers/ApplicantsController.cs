using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Infrastructure.Services;
using PropertyManagement.Web.ViewModels.Applications;

namespace PropertyManagement.Web.Controllers;

/// <summary>
/// Who is on an application. Only someone already on it may add or remove another, which the
/// service enforces rather than this controller assuming it.
/// </summary>
public class ApplicantsController(
    IApplicationApplicantService applicants,
    IRentalApplicationService applications) : ModalController
{
    private const string ApplicantFormPartial = "_ApplicantForm";
    private const string ApplicantListView = "_ApplicantList";

    /// <summary>The applicants region on its own, re-fetched after a modal saves.</summary>
    [HttpGet]
    public async Task<IActionResult> ListPartial(int id, CancellationToken cancellationToken)
    {
        var model = await BuildListAsync(id, cancellationToken);

        return model is null ? Forbid() : PartialView(ApplicantListView, model);
    }

    [HttpGet]
    public async Task<IActionResult> Form(int id, CancellationToken cancellationToken)
    {
        var model = await BuildListAsync(id, cancellationToken);

        return model is null || !model.CanManage
            ? Forbid()
            : PartialView(ApplicantFormPartial, new AddApplicantViewModel { ApplicationId = id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(AddApplicantViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!ModelState.IsValid)
        {
            return ModalValidationFailed(ApplicantFormPartial, model);
        }

        var result = await applicants.AddApplicantAsync(
            model.ApplicationId,
            model.Email,
            User.GetUserId(),
            cancellationToken);

        if (result.Failed)
        {
            AddError(nameof(model.Email), result.Error!);
            return ModalValidationFailed(ApplicantFormPartial, model);
        }

        return ModalSucceeded(
            Url.Action(nameof(ListPartial), new { id = model.ApplicationId })!,
            "#applicant-list",
            "Applicant added.");
    }

    [HttpGet]
    public async Task<IActionResult> ConfirmRemove(int id, string userId, CancellationToken cancellationToken)
    {
        var model = await BuildListAsync(id, cancellationToken);
        var applicant = model?.Applicants.FirstOrDefault(entry => entry.UserId == userId);

        return model is null || !model.CanManage || applicant is null
            ? Forbid()
            : PartialView("_ConfirmRemoveApplicant", new RemoveApplicantViewModel
            {
                ApplicationId = id,
                UserId = userId,
                Name = applicant.Name
            });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(RemoveApplicantViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        var result = await applicants.RemoveApplicantAsync(
            model.ApplicationId,
            model.UserId,
            User.GetUserId(),
            cancellationToken);

        if (result.Failed)
        {
            AddError(null, result.Error!);
            return ModalValidationFailed("_ConfirmRemoveApplicant", model);
        }

        return ModalSucceeded(
            Url.Action(nameof(ListPartial), new { id = model.ApplicationId })!,
            "#applicant-list",
            $"{model.Name} removed from this application.");
    }

    /// <summary>
    /// Builds the region's model, or null when this user has no business seeing the application
    /// at all. Managers may look; only an applicant on it may change who else is on it.
    /// </summary>
    private async Task<ApplicantListViewModel?> BuildListAsync(int id, CancellationToken cancellationToken)
    {
        var application = await applications.GetAsync(id, cancellationToken);

        if (application is null)
        {
            return null;
        }

        var userId = User.GetUserId();
        var isApplicantOn = application.Applicants.Any(link => link.ApplicantUserId == userId);

        if (!isApplicantOn && !User.IsPropertyManager())
        {
            return null;
        }

        return new ApplicantListViewModel
        {
            ApplicationId = id,
            Applicants = await applicants.GetApplicantsAsync(id, cancellationToken),
            CurrentUserId = userId,
            CanManage = Domain.Rules.ApplicationWorkflow.CanEdit(application.Status, isApplicantOn)
        };
    }
}
