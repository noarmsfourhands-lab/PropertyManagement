using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyManagement.Application.Services;
using PropertyManagement.Web.ViewModels.Applications;

namespace PropertyManagement.Web.Controllers;

/// <summary>
/// Internal notes on an application, kept by and for property managers.
///
/// The whole controller sits behind the property manager policy, so there is no action here an
/// applicant can reach, whether or not a page offers them a link to it.
/// </summary>
[Authorize(Policy = AuthorizationPolicies.PropertyManager)]
public class NotesController(INoteService notes) : ModalController
{
    private const string NoteFormPartial = "_NoteForm";
    private const string NoteListView = "_NoteList";

    /// <summary>The notes region on its own, re-fetched after a modal saves.</summary>
    [HttpGet]
    public async Task<IActionResult> ListPartial(int id, CancellationToken cancellationToken)
    {
        var model = new NoteListViewModel
        {
            ApplicationId = id,
            Notes = await notes.GetNotesAsync(id, cancellationToken)
        };

        return PartialView(NoteListView, model);
    }

    [HttpGet]
    public async Task<IActionResult> Form(int id, int applicationId, CancellationToken cancellationToken)
    {
        if (id == 0)
        {
            return PartialView(NoteFormPartial, new NoteFormViewModel { ApplicationId = applicationId });
        }

        var note = await notes.GetNoteAsync(id, cancellationToken);

        return note is null
            ? NotFound()
            : PartialView(NoteFormPartial, NoteFormViewModel.From(note));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(NoteFormViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!ModelState.IsValid)
        {
            return ModalValidationFailed(NoteFormPartial, model);
        }

        var result = await notes.SaveNoteAsync(model.ToInput(), User.ToActor(), cancellationToken);

        if (result.Failed)
        {
            AddError(nameof(model.Body), result.Error!);
            return ModalValidationFailed(NoteFormPartial, model);
        }

        return ModalSucceeded(
            Url.Action(nameof(ListPartial), new { id = model.ApplicationId })!,
            "#note-list",
            model.IsNew ? "Note added." : "Note saved.");
    }

    [HttpGet]
    public async Task<IActionResult> ConfirmDelete(int id, CancellationToken cancellationToken)
    {
        var note = await notes.GetNoteAsync(id, cancellationToken);

        return note is null
            ? NotFound()
            : PartialView("_ConfirmDeleteNote", note);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var note = await notes.GetNoteAsync(id, cancellationToken);

        if (note is null)
        {
            return NotFound();
        }

        var result = await notes.DeleteNoteAsync(id, cancellationToken);

        if (result.Failed)
        {
            AddError(null, result.Error!);
            return ModalValidationFailed("_ConfirmDeleteNote", note);
        }

        return ModalSucceeded(
            Url.Action(nameof(ListPartial), new { id = note.RentalApplicationId })!,
            "#note-list",
            "Note removed.");
    }
}
