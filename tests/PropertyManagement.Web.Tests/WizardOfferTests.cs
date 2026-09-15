using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Web.ViewModels.Applications;

namespace PropertyManagement.Web.Tests;

/// <summary>
/// The wizard model decides what the page offers. These pin the parts of that decision the bonus
/// items changed, so a draft save cannot quietly become a way past submission.
/// </summary>
public class WizardOfferTests
{
    private static ApplicationWizardViewModel ModelFor(
        RentalApplication application,
        ApplicationSection section) =>
        ApplicationWizardViewModel.FromStorage(application, section, true, false, "applicant-1");

    private static RentalApplication SavedButIncomplete() => new()
    {
        Id = 1,
        Status = ApplicationStatus.Draft,
        Unit = new Unit { UnitNumber = "101", Property = new Property(), UnitType = new UnitType() },
        // A draft save marked both sections saved while the contents are still wrong.
        ApplicantInformationSavedAtUtc = DateTime.UtcNow,
        ResidenceHistorySavedAtUtc = DateTime.UtcNow,
        ApplicantInformation = new ApplicantInformation { FirstName = "Robin" },
        Residences = [],
        Applicants = [new RentalApplicationApplicant { ApplicantUserId = "applicant-1", IsPrimary = true }]
    };

    [Fact]
    public void Saving_a_section_as_a_draft_does_not_unlock_submission()
    {
        var model = ModelFor(SavedButIncomplete(), ApplicationSection.Summary);

        // Both sections have been saved, so the flags alone would have allowed this. The rule
        // asks whether they are finished as well, and says so.
        Assert.Equal("Applicant Information is not complete.", model.SubmitBlockedReason);

        // The page additionally lists what is wrong, field by field.
        Assert.NotEmpty(model.Problems);
        Assert.False(model.CanSubmit);
    }

    [Fact]
    public void An_application_that_cannot_be_edited_offers_no_way_back()
    {
        var withdrawn = SavedButIncomplete();
        withdrawn.Status = ApplicationStatus.Withdrawn;

        var model = ModelFor(withdrawn, ApplicationSection.Summary);

        // The page forces a read-only application to the Summary on arrival, so Back would land
        // exactly where it started. It used to render anyway and read as a button that did nothing.
        Assert.False(model.CanEdit);
        Assert.False(model.ShowBack);
        Assert.False(model.ShowContinue);
        Assert.False(model.ShowSaveDraft);
    }

    [Fact]
    public void A_step_that_cannot_be_opened_says_why()
    {
        var fresh = new RentalApplication
        {
            Id = 1,
            Status = ApplicationStatus.Draft,
            Unit = new Unit { UnitNumber = "101", Property = new Property(), UnitType = new UnitType() },
            Residences = [],
            Applicants = [new RentalApplicationApplicant { ApplicantUserId = "applicant-1", IsPrimary = true }]
        };

        var model = ModelFor(fresh, ApplicationSection.ApplicantInformation);

        // The indicator looks like a row of tabs. A step that just ignores a click reads as broken,
        // so each locked one carries the reason it is locked.
        Assert.Null(model.WhySectionIsLocked(ApplicationSection.ApplicantInformation));
        Assert.Equal(
            "Save applicant information first.",
            model.WhySectionIsLocked(ApplicationSection.ResidenceHistory));
        Assert.Equal(
            "Save applicant information first.",
            model.WhySectionIsLocked(ApplicationSection.Summary));
    }

    [Fact]
    public void A_read_only_application_says_the_status_is_why()
    {
        var approved = SavedButIncomplete();
        approved.Status = ApplicationStatus.Approved;

        var model = ModelFor(approved, ApplicationSection.Summary);

        Assert.Equal(
            "An application in Approved status cannot be changed.",
            model.WhySectionIsLocked(ApplicationSection.ApplicantInformation));
    }

    [Fact]
    public void Draft_saving_is_offered_on_the_data_sections_and_not_on_the_summary()
    {
        var application = SavedButIncomplete();

        Assert.True(ModelFor(application, ApplicationSection.ApplicantInformation).ShowSaveDraft);
        Assert.True(ModelFor(application, ApplicationSection.ResidenceHistory).ShowSaveDraft);
        Assert.False(ModelFor(application, ApplicationSection.Summary).ShowSaveDraft);
    }

    [Fact]
    public void The_summary_renders_sections_read_only_even_while_the_application_is_editable()
    {
        var application = SavedButIncomplete();

        Assert.True(ModelFor(application, ApplicationSection.ApplicantInformation).SectionIsEditable);
        Assert.False(ModelFor(application, ApplicationSection.Summary).SectionIsEditable);
    }

    [Fact]
    public void Problems_are_grouped_under_the_section_they_belong_to()
    {
        var model = ModelFor(SavedButIncomplete(), ApplicationSection.Summary);

        var sections = model.ProblemsBySection.Select(group => group.Key).ToList();

        Assert.Equal(
            [ApplicationSection.ApplicantInformation, ApplicationSection.ResidenceHistory],
            sections);
    }

    [Fact]
    public void A_submitted_application_offers_nothing_that_would_change_it()
    {
        var application = SavedButIncomplete();
        application.Status = ApplicationStatus.Submitted;

        var model = ModelFor(application, ApplicationSection.Summary);

        Assert.False(model.CanEdit);
        Assert.False(model.ShowSaveDraft);
        Assert.False(model.ShowContinue);
        Assert.False(model.CanSubmit);
    }
}
