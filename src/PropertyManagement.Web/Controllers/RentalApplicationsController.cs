using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Application.Services;
using PropertyManagement.Web.ViewModels.Applications;

namespace PropertyManagement.Web.Controllers;

/// <summary>
/// The rental application: the list, the single-page wizard, and withdrawal. The residences inside
/// the second section are their own resource and have their own controller.
///
/// The wizard is one page showing one section at a time. One view model drives it and one action
/// receives every post; the button that was pressed decides what happens. Only the section on
/// screen is validated, and everything the page displays is rebuilt from storage on each request
/// rather than trusted from hidden fields.
/// </summary>
public class RentalApplicationsController(
    IRentalApplicationService applications,
    ApplicationContextFactory contexts,
    TimeProvider timeProvider) : ModalController
{
    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    // ---------------------------------------------------------------- List

    /// <summary>
    /// The applications page. It renders the grid's shell and its starting filters; the rows
    /// themselves are fetched by the grid from the JSON endpoint, so this deliberately does not
    /// query for them. Filtering, sorting, counting and paging all happen in the database there.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(
        ApplicationStatus? status,
        int? propertyId,
        CancellationToken cancellationToken = default)
    {
        var model = new ApplicationListViewModel
        {
            Status = status,
            PropertyId = propertyId,
            ShowsEveryApplicant = User.IsPropertyManager()
        };

        model.SetChoices(await applications.GetFilterPropertiesAsync(cancellationToken));

        return View(model);
    }

    // ---------------------------------------------------------------- Starting an application

    /// <summary>
    /// Starts, or reopens, this applicant's application for a unit. Property managers do not apply
    /// for units, which the policy states rather than an inline role check.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.Applicant)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(int unitId, CancellationToken cancellationToken)
    {
        var result = await applications.StartAsync(unitId, User.ToActor(), Today, cancellationToken);

        if (result.Failed)
        {
            TempData["ErrorMessage"] = result.Error;
            return RedirectToAction("Index", "Home");
        }

        return RedirectToAction(nameof(Edit), new { id = result.Value });
    }

    // ---------------------------------------------------------------- The wizard

    [HttpGet]
    public async Task<IActionResult> Edit(
        int id,
        ApplicationSection? section,
        CancellationToken cancellationToken = default)
    {
        var context = await contexts.LoadAsync(id, User, cancellationToken);

        if (context is null)
        {
            return NotFound();
        }

        if (!context.CanView)
        {
            return Hidden();
        }

        var model = BuildModel(context, ApplicationSection.Summary);

        var target = section ?? ApplicationWizardViewModel.DefaultSectionFor(
            context.Application,
            context.IsApplicantOn);

        // A section only reachable by editing is not shown to someone who cannot edit, and a
        // section the server has not accepted yet is not reachable by typing its name into the
        // address bar either. Both fall back to the Summary, which shows everything.
        if (!context.CanEdit || !model.IsSectionReachable(target))
        {
            target = ApplicationSection.Summary;
        }

        return View(BuildModel(context, target));
    }

    /// <summary>
    /// The single action behind the single form. Continue validates and saves the current section
    /// before moving on, Back moves without saving, and Submit is only honoured from the Summary.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Section(
        ApplicationWizardViewModel model,
        WizardCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        var context = await contexts.LoadAsync(model.ApplicationId, User, cancellationToken);

        if (context is null)
        {
            return NotFound();
        }

        if (!context.CanView)
        {
            return Hidden();
        }

        // Back never saves and never validates, so a redirect simply discards what was posted.
        if (command == WizardCommand.Back)
        {
            var previous = ApplicationWizard.Previous(model.CurrentSection) ?? model.CurrentSection;
            return RedirectToAction(nameof(Edit), new { id = model.ApplicationId, section = previous });
        }

        // Every remaining command changes something, so an application that is not editable by
        // this user rejects the post regardless of what the page offered.
        if (!context.CanEdit)
        {
            TempData["ErrorMessage"] = $"An application in {context.Application.Status} status cannot be changed.";
            return RedirectToAction(nameof(Edit), new { id = model.ApplicationId });
        }

        // Which section is being written comes from a posted field, so the post has to look like
        // it came from that section's form. Without this, a post from the residence history page
        // claiming to be applicant information would blank every applicant field, and one claiming
        // the reverse would silently drop what the user had just typed and advance anyway.
        if (command != WizardCommand.Submit && !PostCameFromSection(model.CurrentSection))
        {
            return BadRequest();
        }

        return command switch
        {
            WizardCommand.Continue => await ContinueAsync(model, context, cancellationToken),
            WizardCommand.SaveDraft => await SaveDraftAsync(model, context, cancellationToken),
            WizardCommand.Submit => await SubmitAsync(model, context, cancellationToken),
            _ => BadRequest()
        };
    }

    private async Task<IActionResult> ContinueAsync(
        ApplicationWizardViewModel model,
        ApplicationContext context,
        CancellationToken cancellationToken)
    {
        ValidateOnlyCurrentSection(model.CurrentSection);

        if (!ModelState.IsValid)
        {
            return View(nameof(Edit), Rehydrate(model, context));
        }

        var saved = model.CurrentSection switch
        {
            ApplicationSection.ApplicantInformation => await applications.SaveApplicantInformationAsync(
                model.ApplicantInformation.ToInput(model.ApplicationId, model.ApplicantInformationVersion),
                context.UserId,
                cancellationToken),

            ApplicationSection.ResidenceHistory => await applications.SaveResidenceHistoryAsync(
                model.ApplicationId,
                model.ResidenceHistoryVersion,
                context.UserId,
                requireComplete: true,
                cancellationToken),

            // The Summary holds nothing to save; Continue is not offered there.
            _ => Domain.Common.DomainResult.Success()
        };

        if (saved.Failed)
        {
            // A rule rejected the save, so what is on screen is no longer the useful thing to
            // show. This matters most for a stale save: the page comes back with what is actually
            // stored, and with a token matching it, rather than the user's copy and a fresh token
            // that would let a second Continue overwrite the other person's work after all.
            var reloaded = await contexts.LoadAsync(model.ApplicationId, User, cancellationToken) ?? context;

            // Model state still holds the posted values, and the tag helpers prefer those over
            // the model, so it is cleared before the message is put back.
            ModelState.Clear();
            AddError(null, saved.Error!);

            return View(nameof(Edit), BuildModel(reloaded, model.CurrentSection));
        }

        var next = ApplicationWizard.Next(model.CurrentSection) ?? ApplicationSection.Summary;
        return RedirectToAction(nameof(Edit), new { id = model.ApplicationId, section = next });
    }

    /// <summary>
    /// Saves the section exactly as it stands, even while it is still wrong, and stays on it with
    /// the errors shown. What the applicant typed is kept, because the point of the action is to
    /// put work down and come back to it.
    /// </summary>
    private async Task<IActionResult> SaveDraftAsync(
        ApplicationWizardViewModel model,
        ApplicationContext context,
        CancellationToken cancellationToken)
    {
        var saved = model.CurrentSection switch
        {
            ApplicationSection.ApplicantInformation => await applications.SaveApplicantInformationAsync(
                model.ApplicantInformation.ToInput(model.ApplicationId, model.ApplicantInformationVersion),
                context.UserId,
                cancellationToken),

            ApplicationSection.ResidenceHistory => await applications.SaveResidenceHistoryAsync(
                model.ApplicationId,
                model.ResidenceHistoryVersion,
                context.UserId,
                requireComplete: false,
                cancellationToken),

            _ => Domain.Common.DomainResult.Success()
        };

        var reloaded = await contexts.LoadAsync(model.ApplicationId, User, cancellationToken) ?? context;

        if (saved.Failed)
        {
            ModelState.Clear();
            AddError(null, saved.Error!);
            return View(nameof(Edit), BuildModel(reloaded, model.CurrentSection));
        }

        // Only the current section's rules are reported, so the reader is not told about a section
        // they are not looking at. The Summary is where the whole list belongs.
        ValidateOnlyCurrentSection(model.CurrentSection);
        ReportProblems(reloaded, model.CurrentSection);

        TempData["StatusMessage"] = ModelState.IsValid
            ? "Saved."
            : "Saved. There is still something to fix before you can submit.";

        return View(nameof(Edit), Rehydrate(model, reloaded));
    }

    /// <summary>
    /// Copies a section's outstanding problems onto model state, each against the field it belongs
    /// to, so they render exactly as a failed post would render them.
    /// </summary>
    private void ReportProblems(ApplicationContext context, ApplicationSection? only = null)
    {
        var problems = SectionValidation.ProblemsWith(context.Application)
            .Where(problem => only is null || problem.Section == only);

        foreach (var problem in problems)
        {
            ModelState.AddModelError(problem.ModelStateKey, problem.Message);
        }
    }

    private async Task<IActionResult> SubmitAsync(
        ApplicationWizardViewModel model,
        ApplicationContext context,
        CancellationToken cancellationToken)
    {
        // Submit belongs to the Summary alone, whatever a crafted post claims.
        if (model.CurrentSection != ApplicationSection.Summary)
        {
            return BadRequest();
        }

        // A draft save can leave a section stored but incomplete, so submission is refused here
        // rather than only being hidden in the page. This is the server-side half of the rule.
        var outstanding = SectionValidation.ProblemsWith(context.Application);

        if (outstanding.Count > 0)
        {
            var reloaded = await contexts.LoadAsync(model.ApplicationId, User, cancellationToken) ?? context;

            ModelState.Clear();
            AddError(null, "This application still has errors and cannot be submitted.");
            ReportProblems(reloaded);

            return View(nameof(Edit), BuildModel(reloaded, ApplicationSection.Summary));
        }

        var result = await applications.SubmitAsync(
            model.ApplicationId,
            User.ToActor(),
            Today,
            cancellationToken);

        if (result.Failed)
        {
            var reloaded = await contexts.LoadAsync(model.ApplicationId, User, cancellationToken) ?? context;

            ModelState.Clear();
            AddError(null, result.Error!);

            return View(nameof(Edit), BuildModel(reloaded, ApplicationSection.Summary));
        }

        TempData["StatusMessage"] = "Your application has been submitted.";
        return RedirectToAction(nameof(Edit), new { id = model.ApplicationId });
    }

    // ---------------------------------------------------------------- Withdraw

    [HttpGet]
    public async Task<IActionResult> ConfirmWithdraw(int id, CancellationToken cancellationToken)
    {
        var context = await contexts.LoadAsync(id, User, cancellationToken);

        if (context is null)
        {
            return NotFound();
        }

        if (!context.CanView)
        {
            return Hidden();
        }

        if (!context.IsApplicantOn)
        {
            return Forbid();
        }

        return PartialView("_ConfirmWithdraw", context.Application);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(int id, CancellationToken cancellationToken)
    {
        // Checked before the service is called, not only after it refuses. The rejection is
        // rendered from the application itself, so reaching that path at all has to be earned.
        var context = await contexts.LoadAsync(id, User, cancellationToken);

        if (context is null || !context.IsApplicantOn)
        {
            return Hidden();
        }

        var result = await applications.WithdrawAsync(id, User.ToActor(), cancellationToken);

        if (result.Failed)
        {
            AddError(null, result.Error!);
            return ModalValidationFailed("_ConfirmWithdraw", context.Application);
        }

        // No region named, so the page reloads: withdrawing changes the whole screen. The message
        // goes in TempData because a reload discards anything announced in the browser.
        TempData["StatusMessage"] = "Application withdrawn.";

        return ModalSucceeded(Url.Action(nameof(Edit), new { id })!, target: null);
    }

    // ---------------------------------------------------------------- Helpers

    /// <summary>
    /// Whether the posted form is the one the claimed section renders.
    ///
    /// Applicant information is the only section with fields of its own, so its form is the only
    /// one that carries keys under that prefix. A post claiming to be that section without them,
    /// or claiming to be another section while carrying them, did not come from the page it says
    /// it did.
    /// </summary>
    private bool PostCameFromSection(ApplicationSection section)
    {
        var prefix = $"{nameof(ApplicationWizardViewModel.ApplicantInformation)}.";

        var carriesApplicantInformation = Request.HasFormContentType
            && Request.Form.Keys.Any(key => key.StartsWith(prefix, StringComparison.Ordinal));

        return section == ApplicationSection.ApplicantInformation
            ? carriesApplicantInformation
            : !carriesApplicantInformation;
    }

    /// <summary>
    /// Only the section on screen is validated. The others were never posted, so their required
    /// fields would otherwise fail on values the user was never shown.
    /// </summary>
    private void ValidateOnlyCurrentSection(ApplicationSection section)
    {
        var prefix = section == ApplicationSection.ApplicantInformation
            ? $"{nameof(ApplicationWizardViewModel.ApplicantInformation)}."
            : null;

        foreach (var key in ModelState.Keys.ToList())
        {
            if (prefix is null || !key.StartsWith(prefix, StringComparison.Ordinal))
            {
                ModelState.Remove(key);
            }
        }
    }

    private ApplicationWizardViewModel BuildModel(ApplicationContext context, ApplicationSection section) =>
        ApplicationWizardViewModel.FromStorage(
            context.Application,
            section,
            context.IsApplicantOn,
            context.IsManager,
            context.UserId);

    private static ApplicationWizardViewModel Rehydrate(
        ApplicationWizardViewModel model,
        ApplicationContext context)
    {
        model.Rehydrate(context.Application, context.IsApplicantOn, context.IsManager, context.UserId);
        return model;
    }
}
