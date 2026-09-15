using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Application.Services;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Web.ViewModels.Applications;

namespace PropertyManagement.Web.Controllers;

/// <summary>
/// The residences on a rental application: the list, and the modal that adds, edits and removes one.
///
/// A separate controller from the wizard because it is a separate resource. The wizard owns the
/// page and its sections; this owns the rows inside one of them, each reached by its own id and
/// each edited through the modal contract rather than the page's single form.
///
/// Every action loads the application first and decides from it. A residence is only ever found
/// through the application it belongs to, so posting somebody else's residence id reaches nothing,
/// and the permission answer is made before the service is called rather than inferred from its
/// refusal afterwards.
/// </summary>
public class ResidencesController(
    IRentalApplicationService applications,
    ApplicationContextFactory context,
    TimeProvider timeProvider) : ModalController
{
    private const string FormPartial = "_ResidenceForm";
    private const string ConfirmDeletePartial = "_ConfirmDeleteResidence";
    private const string ListPartialView = "_ResidenceList";

    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    /// <summary>The add or edit modal. An id of zero is a new residence.</summary>
    [HttpGet]
    public async Task<IActionResult> Form(
        int id,
        int applicationId,
        CancellationToken cancellationToken)
    {
        var application = await context.LoadAsync(applicationId, User, cancellationToken);
        var refused = Refuse(application, requireEdit: true);

        if (refused is not null)
        {
            return refused;
        }

        if (id == 0)
        {
            return PartialView(FormPartial, new ResidenceFormViewModel { ApplicationId = applicationId });
        }

        var residence = application!.Application.Residences.FirstOrDefault(entity => entity.Id == id);

        return residence is null
            ? NotFound()
            : PartialView(FormPartial, ResidenceFormViewModel.From(residence));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(ResidenceFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!ModelState.IsValid)
        {
            return ModalValidationFailed(FormPartial, model);
        }

        var result = await applications.SaveResidenceAsync(
            model.ToInput(),
            User.GetUserId(),
            Today,
            cancellationToken);

        if (result.Failed)
        {
            // The rule names the date it refused. The domain entity and this form spell those
            // properties the same way, so the name carries straight onto the input.
            AddError(result.Field, result.Error!);
            return ModalValidationFailed(FormPartial, model);
        }

        return ModalSucceeded(
            Url.Action(nameof(ListPartial), new { id = model.ApplicationId })!,
            "#residence-list",
            model.IsNew ? "Residence added." : "Residence saved.");
    }

    [HttpGet]
    public async Task<IActionResult> ConfirmDelete(
        int id,
        int applicationId,
        CancellationToken cancellationToken)
    {
        var application = await context.LoadAsync(applicationId, User, cancellationToken);
        var refused = Refuse(application, requireEdit: true);

        if (refused is not null)
        {
            return refused;
        }

        var residence = application!.Application.Residences.FirstOrDefault(entity => entity.Id == id);

        return residence is null
            ? NotFound()
            : PartialView(ConfirmDeletePartial, residence);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(
        int id,
        int applicationId,
        CancellationToken cancellationToken)
    {
        // Guarded before the service call, matching the GET twin. Without this a signed-in
        // applicant could post another applicant's residence id and read their address back out
        // of the rejection that the service correctly produced.
        var application = await context.LoadAsync(applicationId, User, cancellationToken);
        var refused = Refuse(application, requireEdit: true);

        if (refused is not null)
        {
            return refused;
        }

        var residence = application!.Application.Residences.FirstOrDefault(entity => entity.Id == id);

        if (residence is null)
        {
            return NotFound();
        }

        var result = await applications.DeleteResidenceAsync(id, User.GetUserId(), cancellationToken);

        if (result.Failed)
        {
            AddError(null, result.Error!);
            return ModalValidationFailed(ConfirmDeletePartial, residence);
        }

        return ModalSucceeded(
            Url.Action(nameof(ListPartial), new { id = applicationId })!,
            "#residence-list",
            "Residence removed.");
    }

    /// <summary>The residence list on its own, re-fetched after the modal saves.</summary>
    [HttpGet]
    public async Task<IActionResult> ListPartial(int id, CancellationToken cancellationToken)
    {
        var application = await context.LoadAsync(id, User, cancellationToken);
        var refused = Refuse(application, requireEdit: false);

        if (refused is not null)
        {
            return refused;
        }

        return PartialView(ListPartialView, new ResidenceListViewModel
        {
            ApplicationId = id,
            Residences = ResidenceRules.InReviewOrder(application!.Application.Residences),
            Editable = application.CanEdit
        });
    }

    /// <summary>
    /// The refusal for this context, or null when the action may go ahead.
    ///
    /// Five actions ask the same two questions in the same order, and the order is the point: the
    /// view check runs first so somebody who may not see the application gets "not found" rather
    /// than a "forbidden" that confirms the id for them.
    /// </summary>
    private IActionResult? Refuse(ApplicationContext? application, bool requireEdit)
    {
        if (application is null || !application.CanView)
        {
            return Hidden();
        }

        return requireEdit && !application.CanEdit ? Forbid() : null;
    }
}
