using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Application.Services;
using PropertyManagement.Web.ViewModels.Applications;

namespace PropertyManagement.Web.Controllers;

/// <summary>
/// Who is on an application. Only someone already on it may add or remove another, which the
/// service enforces rather than this controller assuming it.
/// </summary>
public class ApplicantsController(
    IApplicationApplicantService applicants,
    ApplicantListFactory factory) : ModalController
{
    private const string ApplicantFormPartial = "_ApplicantForm";
    private const string ApplicantListView = "_ApplicantList";

    /// <summary>The applicants region on its own, re-fetched after a modal saves.</summary>
    [HttpGet]
    public async Task<IActionResult> ListPartial(int id, CancellationToken cancellationToken)
    {
        var model = await factory.BuildAsync(id, User, cancellationToken);

        // Null means this user may not view the application at all, which is answered the same way
        // the application's own pages answer it: as though it does not exist.
        return model is null ? NotFound() : PartialView(ApplicantListView, model);
    }

    [HttpGet]
    public async Task<IActionResult> Form(int id, CancellationToken cancellationToken)
    {
        var model = await factory.BuildAsync(id, User, cancellationToken);

        if (model is null)
        {
            return NotFound();
        }

        // May view but not change who is on it: 403 says so, and tells them nothing new.
        return model.CanManage
            ? PartialView(ApplicantFormPartial, new AddApplicantViewModel { ApplicationId = id })
            : Forbid();
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
        var model = await factory.BuildAsync(id, User, cancellationToken);

        if (model is null)
        {
            return NotFound();
        }

        var applicant = model.Applicants.FirstOrDefault(entry => entry.UserId == userId);

        if (applicant is null)
        {
            return NotFound();
        }

        return model.CanManage
            ? PartialView("_ConfirmRemoveApplicant", new RemoveApplicantViewModel
            {
                ApplicationId = id,
                UserId = userId,
                Name = applicant.Name
            })
            : Forbid();
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
}
