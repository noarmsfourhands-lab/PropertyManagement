using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Application.Services;
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

        var model = new ReviewFormViewModel { ApplicationId = application.Id };
        Describe(model, application);

        return PartialView(ReviewFormPartial, model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(ReviewFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!ModelState.IsValid)
        {
            await DescribeAsync(model, cancellationToken);
            return ModalValidationFailed(ReviewFormPartial, model);
        }

        var result = await review.CompleteAsync(model.ToInput(), User.ToActor(), Today, cancellationToken);

        if (result.Failed)
        {
            // A missing comment lands on the comment box because the rule said so, not because
            // this read the word "comment" out of the sentence it produced.
            AddError(result.Field, result.Error!);

            await DescribeAsync(model, cancellationToken);
            return ModalValidationFailed(ReviewFormPartial, model);
        }

        // The decision changes the whole screen, so the page reloads rather than a single region.
        // A reload discards anything announced in the browser, so the message goes in TempData.
        TempData["StatusMessage"] = $"Review recorded: {model.Outcome}.";

        return ModalSucceeded(
            Url.Action("Edit", "RentalApplications", new { id = model.ApplicationId })!,
            target: null);
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

    /// <summary>
    /// Fills in the line naming what is being reviewed. Those fields are shown, not edited, so a
    /// post does not carry them and a re-render has to look them up again.
    /// </summary>
    private async Task DescribeAsync(ReviewFormViewModel model, CancellationToken cancellationToken)
    {
        var application = await applications.GetAsync(model.ApplicationId, cancellationToken);

        if (application is not null)
        {
            Describe(model, application);
        }
    }

    private static void Describe(ReviewFormViewModel model, Domain.Entities.RentalApplication application)
    {
        model.PropertyName = application.Unit.Property?.Name ?? string.Empty;
        model.UnitNumber = application.Unit.UnitNumber;
        model.ApplicantName = application.ApplicantInformation.FullName;
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
