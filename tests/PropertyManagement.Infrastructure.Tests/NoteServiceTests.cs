using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Infrastructure.Identity;
using PropertyManagement.Infrastructure.Persistence;
using PropertyManagement.Application.Services;
using PropertyManagement.Infrastructure.Services;

namespace PropertyManagement.Infrastructure.Tests;

public class NoteServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private static readonly Actor Manager = new(TestData.Manager, "Alex Chen");
    private static readonly Actor OtherManager = new(TestData.OtherManager, "Jordan Poole");

    public void Dispose() => _database.Dispose();

    private NoteService ServiceOver(PropertyManagementDbContext db) =>
        new(db, new FixedTimeProvider(TestData.Now));

    [Fact]
    public async Task A_note_is_added_against_its_application_and_credits_its_author()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var application = await TestData.AddCompleteDraftAsync(db, property.Units.First().Id);

        var result = await ServiceOver(db).SaveNoteAsync(
            new NoteInput(0, application.Id, "  Income verified by phone.  "),
            Manager);

        Assert.True(result.Succeeded);

        await using var check = _database.CreateContext();
        var note = await check.PropertyManagerNotes.SingleAsync();

        Assert.Equal(application.Id, note.RentalApplicationId);
        Assert.Equal("Income verified by phone.", note.Body);
        Assert.Equal("Alex Chen", note.AuthorName);
        Assert.Equal(TestData.Now, note.CreatedAtUtc);
        Assert.Null(note.UpdatedAtUtc);
    }

    [Fact]
    public async Task An_empty_note_is_refused()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var application = await TestData.AddCompleteDraftAsync(db, property.Units.First().Id);

        var result = await ServiceOver(db).SaveNoteAsync(new NoteInput(0, application.Id, "   "), Manager);

        Assert.True(result.Failed);
        Assert.Empty(await db.PropertyManagerNotes.ToListAsync());
    }

    [Fact]
    public async Task A_note_cannot_be_attached_to_an_application_that_does_not_exist()
    {
        await using var db = _database.CreateContext();

        var result = await ServiceOver(db).SaveNoteAsync(new NoteInput(0, 999, "Orphan."), Manager);

        Assert.True(result.Failed);
    }

    [Fact]
    public async Task Any_property_manager_can_edit_a_note_and_the_edit_is_dated()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var application = await TestData.AddCompleteDraftAsync(db, property.Units.First().Id);

        await ServiceOver(db).SaveNoteAsync(new NoteInput(0, application.Id, "First pass."), Manager);

        await using var second = _database.CreateContext();
        var note = await second.PropertyManagerNotes.SingleAsync();

        var edited = await ServiceOver(second).SaveNoteAsync(
            new NoteInput(note.Id, application.Id, "Second pass."),
            OtherManager);

        Assert.True(edited.Succeeded);

        await using var check = _database.CreateContext();
        var stored = await check.PropertyManagerNotes.SingleAsync();

        Assert.Equal("Second pass.", stored.Body);
        Assert.Equal("Alex Chen", stored.AuthorName);
        Assert.Equal(TestData.Now, stored.UpdatedAtUtc);
    }

    [Fact]
    public async Task Notes_read_newest_first_and_delete_cleanly()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var application = await TestData.AddCompleteDraftAsync(db, property.Units.First().Id);
        var service = ServiceOver(db);

        await service.SaveNoteAsync(new NoteInput(0, application.Id, "Older."), Manager);
        await service.SaveNoteAsync(new NoteInput(0, application.Id, "Newer."), Manager);

        var notes = await service.GetNotesAsync(application.Id);
        Assert.Equal(["Newer.", "Older."], notes.Select(note => note.Body));

        Assert.True((await service.DeleteNoteAsync(notes[0].Id)).Succeeded);
        Assert.True((await service.DeleteNoteAsync(notes[0].Id)).Failed);

        await using var check = _database.CreateContext();
        Assert.Single(await check.PropertyManagerNotes.ToListAsync());
    }

    [Fact]
    public async Task Notes_belong_to_one_application_only()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var first = await TestData.AddCompleteDraftAsync(db, property.Units.First().Id);
        var second = await TestData.AddCompleteDraftAsync(db, property.Units.Last().Id);
        var service = ServiceOver(db);

        await service.SaveNoteAsync(new NoteInput(0, first.Id, "About the first."), Manager);

        Assert.Single(await service.GetNotesAsync(first.Id));
        Assert.Empty(await service.GetNotesAsync(second.Id));
    }
}
