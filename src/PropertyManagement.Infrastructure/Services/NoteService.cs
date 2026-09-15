using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Services;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Infrastructure.Services;

/// <summary>
/// Internal notes a property manager keeps against an application.
///
/// These are never shown to an applicant. That is enforced in three places rather than one: this
/// service is only reached from a controller restricted to the property manager policy, the notes
/// render through a view component that checks the role itself, and no applicant-facing view model
/// or endpoint projects the type at all. A note therefore cannot leak by someone forgetting a
/// guard on a new page.
///
/// Any property manager may edit any note; the author and the time of the last edit are both
/// recorded, so the history of a note stays legible.
/// </summary>
public class NoteService(PropertyManagementDbContext db, TimeProvider timeProvider) : INoteService
{
    public async Task<IReadOnlyList<PropertyManagerNote>> GetNotesAsync(
        int applicationId,
        CancellationToken cancellationToken = default) =>
        await db.PropertyManagerNotes
            .AsNoTracking()
            .Where(note => note.RentalApplicationId == applicationId)
            .OrderByDescending(note => note.CreatedAtUtc)
            .ThenByDescending(note => note.Id)
            .ToListAsync(cancellationToken);

    public async Task<PropertyManagerNote?> GetNoteAsync(
        int noteId,
        CancellationToken cancellationToken = default) =>
        await db.PropertyManagerNotes
            .AsNoTracking()
            .FirstOrDefaultAsync(note => note.Id == noteId, cancellationToken);

    public async Task<DomainResult> SaveNoteAsync(
        NoteInput input,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(actor);

        var body = input.Body?.Trim();

        if (string.IsNullOrWhiteSpace(body))
        {
            return DomainResult.Failure("A note cannot be empty.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        if (input.Id == 0)
        {
            if (!await db.RentalApplications.AnyAsync(
                    application => application.Id == input.ApplicationId,
                    cancellationToken))
            {
                return DomainResult.Failure("That application no longer exists.");
            }

            db.PropertyManagerNotes.Add(new PropertyManagerNote
            {
                RentalApplicationId = input.ApplicationId,
                Body = body,
                AuthorUserId = actor.UserId,
                AuthorName = actor.Name,
                CreatedAtUtc = now
            });
        }
        else
        {
            var note = await db.PropertyManagerNotes
                .FirstOrDefaultAsync(entity => entity.Id == input.Id, cancellationToken);

            if (note is null)
            {
                return DomainResult.Failure("That note no longer exists.");
            }

            note.Body = body;
            note.UpdatedAtUtc = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return DomainResult.Success();
    }

    public async Task<DomainResult> DeleteNoteAsync(
        int noteId,
        CancellationToken cancellationToken = default)
    {
        var note = await db.PropertyManagerNotes
            .FirstOrDefaultAsync(entity => entity.Id == noteId, cancellationToken);

        if (note is null)
        {
            return DomainResult.Failure("That note no longer exists.");
        }

        db.PropertyManagerNotes.Remove(note);
        await db.SaveChangesAsync(cancellationToken);

        return DomainResult.Success();
    }
}
