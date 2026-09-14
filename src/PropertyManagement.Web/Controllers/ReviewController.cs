using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Infrastructure.Services;
using PropertyManagement.Web.ViewModels.Applications;

namespace PropertyManagement.Web.Controllers;

/// <summary>
/// What a property manager does to a submitted application: claim it, release it, or complete the
/// review through a modal. Applicants never reach any of this, by policy rather than by the page
/// simply not offering it.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.PropertyManager)]
public class ReviewController(
    IReviewService review,
    IRentalApplicationService applications,
    TimeProvider timeProvider) : ModalController
{
    private const string ReviewFormPartial = "_ReviewForm";

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>Returns the modal body for completing a review.</summary>
    [HttpGet]
    public async Task<IActionResult> Form(int id, CancellationToken cancellationToken)
    {
        var application = await applications.GetAsync(id, cancellationToken);

        if (application is null)
        {
            return NotFound();
        }

        var permitted = ApplicationWorkflow.CanReview(application, User.GetUserId());

        if (permitted.Failed)
        {
            return PartialView("_ReviewUnavailable", permitted.Error);
        }

        var model = new ReviewFormViewModel
        {
            ApplicationId = application.Id,
            PropertyName = application.Unit.Property?.Name ?? string.Empty,
            UnitNumber = application.Unit.UnitNumber,
            ApplicantName = application.ApplicantInformation.FullName
        };

        return PartialView(ReviewFormPartial, model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(ReviewFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!ModelState.IsValid)
        {
            return ModalValidationFailed(ReviewFormPartial, model);
        }

        var result = await review.CompleteAsync(model.ToInput(), User.ToActor(), Today, cancellationToken);

        if (result.Failed)
        {
            // A missing comment belongs on the comment box; anything else is about the decision.
            AddError(
                result.Error!.Contains("comment", StringComparison.OrdinalIgnoreCase)
                    ? nameof(model.Comment)
                    : null,
                result.Error);

            return ModalValidationFailed(ReviewFormPartial, model);
        }

        // The decision changes the whole screen, so the page reloads rather than a single region.
        return ModalSucceeded(
            Url.Action("Edit", "RentalApplications", new { id = model.ApplicationId })!,
            target: null,
            $"Review completed: {model.Outcome}.");
    }

    /// <summary>Takes a submitted application out of the queue before reviewing it.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Claim(int id, CancellationToken cancellationToken)
    {
        var result = await review.ClaimAsync(id, User.ToActor(), cancellationToken);

        SetMessage(result, "You have claimed this application.");

        return RedirectToAction("Edit", "RentalApplications", new { id });
    }

    /// <summary>Puts a claimed application back for anyone to pick up.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Release(int id, CancellationToken cancellationToken)
    {
        var result = await review.ReleaseAsync(id, User.ToActor(), cancellationToken);

        SetMessage(result, "Application released back to the queue.");

        return RedirectToAction("Edit", "RentalApplications", new { id });
    }

    private void SetMessage(Domain.Common.DomainResult result, string success)
    {
        if (result.Succeeded)
        {
            TempData["StatusMessage"] = success;
        }
        else
        {
            TempData["ErrorMessage"] = result.Error;
        }
    }
}
