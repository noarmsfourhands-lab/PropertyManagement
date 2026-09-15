using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Web.ViewModels.Applications;

namespace PropertyManagement.Web.Tests;

/// <summary>
/// Bonus four asks that the rules are defined once per section and that each error comes back to
/// the field it belongs to. These check that promise directly: the same attributes model binding
/// uses are what answers the Summary's question about a section nobody is currently looking at.
/// </summary>
public class SectionValidationTests
{
    private static ApplicantInformationSectionViewModel Complete() => new()
    {
        FirstName = "Robin",
        LastName = "Alvarez",
        Phone = "555-0100",
        Email = "robin@example.com",
        AddressLine1 = "4 Cedar Lane",
        City = "Portland",
        State = "OR",
        PostalCode = "97202"
    };

    [Fact]
    public void A_complete_section_has_nothing_wrong_with_it()
    {
        Assert.Empty(SectionValidation.ProblemsWith(Complete()));
    }

    [Fact]
    public void An_empty_section_reports_every_required_field()
    {
        var problems = SectionValidation.ProblemsWith(new ApplicantInformationSectionViewModel());

        var fields = problems.Select(problem => problem.Field).ToList();

        Assert.Contains(nameof(ApplicantInformationSectionViewModel.FirstName), fields);
        Assert.Contains(nameof(ApplicantInformationSectionViewModel.LastName), fields);
        Assert.Contains(nameof(ApplicantInformationSectionViewModel.Phone), fields);
        Assert.Contains(nameof(ApplicantInformationSectionViewModel.Email), fields);
        Assert.Contains(nameof(ApplicantInformationSectionViewModel.AddressLine1), fields);
        Assert.Contains(nameof(ApplicantInformationSectionViewModel.City), fields);
        Assert.Contains(nameof(ApplicantInformationSectionViewModel.State), fields);
        Assert.Contains(nameof(ApplicantInformationSectionViewModel.PostalCode), fields);

        // The optional line is not among them.
        Assert.DoesNotContain(nameof(ApplicantInformationSectionViewModel.AddressLine2), fields);
    }

    [Fact]
    public void Every_problem_names_the_field_it_belongs_to()
    {
        var problems = SectionValidation.ProblemsWith(new ApplicantInformationSectionViewModel());

        Assert.All(problems, problem => Assert.False(string.IsNullOrWhiteSpace(problem.Field)));
        Assert.All(problems, problem => Assert.False(string.IsNullOrWhiteSpace(problem.Message)));
    }

    [Fact]
    public void A_field_problem_is_reported_under_the_key_its_input_is_bound_to()
    {
        var problems = SectionValidation.ProblemsWith(new ApplicantInformationSectionViewModel());

        var email = problems.Single(problem =>
            problem.Field == nameof(ApplicantInformationSectionViewModel.Email));

        // Matches the name the section partial renders, so the message lands on that very input.
        Assert.Equal("ApplicantInformation.Email", email.ModelStateKey);
    }

    [Fact]
    public void A_malformed_email_is_caught_as_well_as_a_missing_one()
    {
        var section = Complete();
        section.Email = "not-an-address";

        var problems = SectionValidation.ProblemsWith(section);

        Assert.Single(problems);
        Assert.Equal(nameof(ApplicantInformationSectionViewModel.Email), problems[0].Field);
    }

    [Fact]
    public void An_over_long_value_is_caught_by_the_same_attributes()
    {
        var section = Complete();
        section.FirstName = new string('a', 101);

        var problems = SectionValidation.ProblemsWith(section);

        Assert.Single(problems);
        Assert.Equal(nameof(ApplicantInformationSectionViewModel.FirstName), problems[0].Field);
    }

    [Fact]
    public void An_empty_residence_history_is_a_problem_against_the_section_rather_than_a_field()
    {
        var problems = SectionValidation.ProblemsWith(Array.Empty<Residence>());

        var problem = Assert.Single(problems);

        Assert.Equal(ApplicationSection.ResidenceHistory, problem.Section);
        Assert.Null(problem.Field);
        Assert.Equal(string.Empty, problem.ModelStateKey);
    }

    [Fact]
    public void One_residence_settles_the_residence_history_section()
    {
        Residence[] residences = [new() { MoveInDate = new DateOnly(2024, 1, 1) }];

        Assert.Empty(SectionValidation.ProblemsWith(residences));
    }

    [Fact]
    public void An_application_reports_the_problems_of_every_section_at_once()
    {
        var application = new RentalApplication
        {
            ApplicantInformation = new ApplicantInformation { FirstName = "Robin" },
            Residences = []
        };

        var problems = SectionValidation.ProblemsWith(application);

        Assert.Contains(problems, problem => problem.Section == ApplicationSection.ApplicantInformation);
        Assert.Contains(problems, problem => problem.Section == ApplicationSection.ResidenceHistory);
    }

    [Fact]
    public void A_finished_application_has_nothing_blocking_it()
    {
        var complete = Complete();

        var application = new RentalApplication
        {
            ApplicantInformation = new ApplicantInformation
            {
                FirstName = complete.FirstName,
                LastName = complete.LastName,
                Phone = complete.Phone,
                Email = complete.Email,
                AddressLine1 = complete.AddressLine1,
                City = complete.City,
                State = complete.State,
                PostalCode = complete.PostalCode
            },
            Residences = [new Residence { MoveInDate = new DateOnly(2024, 1, 1) }]
        };

        Assert.Empty(SectionValidation.ProblemsWith(application));
    }
}

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

        // The workflow rule is satisfied, because both sections have been saved.
        Assert.Null(model.SubmitBlockedReason);

        // Submission is still refused, because the saved contents are not yet right.
        Assert.NotEmpty(model.Problems);
        Assert.False(model.CanSubmit);
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
