using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Entities;

namespace PropertyManagement.Application.Services;

/// <summary>A note as posted from the modal. Id is zero when adding.</summary>
public record NoteInput(int Id, int ApplicationId, string Body);

public interface INoteService
{
    Task<IReadOnlyList<PropertyManagerNote>> GetNotesAsync(
        int applicationId,
        CancellationToken cancellationToken = default);

    Task<PropertyManagerNote?> GetNoteAsync(int noteId, CancellationToken cancellationToken = default);

    Task<DomainResult> SaveNoteAsync(NoteInput input, Actor actor, CancellationToken cancellationToken = default);

    Task<DomainResult> DeleteNoteAsync(int noteId, CancellationToken cancellationToken = default);
}
