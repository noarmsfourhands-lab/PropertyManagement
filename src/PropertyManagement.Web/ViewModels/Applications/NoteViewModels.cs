using System.ComponentModel.DataAnnotations;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Application.Services;

namespace PropertyManagement.Web.ViewModels.Applications;

/// <summary>Backs the add and edit note modal. Id is zero when adding.</summary>
public class NoteFormViewModel
{
    public int Id { get; set; }

    public int ApplicationId { get; set; }

    [Required(ErrorMessage = "Write the note before saving.")]
    [StringLength(4000)]
    [Display(Name = "Note")]
    public string Body { get; set; } = string.Empty;

    public bool IsNew => Id == 0;

    public string Title => IsNew ? "Add note" : "Edit note";

    public static NoteFormViewModel From(PropertyManagerNote note) => new()
    {
        Id = note.Id,
        ApplicationId = note.RentalApplicationId,
        Body = note.Body
    };

    public NoteInput ToInput() => new(Id, ApplicationId, Body);
}

/// <summary>
/// The notes region. Carries its own application id so the partial can be returned on its own
/// when the modal refreshes it.
/// </summary>
public class NoteListViewModel
{
    public required int ApplicationId { get; init; }

    public required IReadOnlyList<PropertyManagerNote> Notes { get; init; }

    public bool IsEmpty => Notes.Count == 0;
}
