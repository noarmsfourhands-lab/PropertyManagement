using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;

namespace PropertyManagement.Domain.Tests.Rules;

public class ApplicantInformationRulesTests
{
    private static ApplicantInformation Complete() => new()
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
    public void A_complete_section_is_complete()
    {
        Assert.True(ApplicantInformationRules.IsComplete(Complete()));
        Assert.Empty(ApplicantInformationRules.MissingFields(Complete()));
    }

    [Fact]
    public void An_untouched_section_is_missing_every_required_field()
    {
        var missing = ApplicantInformationRules.MissingFields(new ApplicantInformation());

        Assert.Equal(ApplicantInformationRules.RequiredFields, missing);
    }

    [Fact]
    public void The_optional_second_address_line_is_not_required()
    {
        Assert.DoesNotContain(
            nameof(ApplicantInformation.AddressLine2),
            ApplicantInformationRules.RequiredFields);
    }

    [Fact]
    public void Whitespace_does_not_count_as_a_value()
    {
        var section = Complete();
        section.City = "   ";

        Assert.False(ApplicantInformationRules.IsComplete(section));
        Assert.Equal([nameof(ApplicantInformation.City)], ApplicantInformationRules.MissingFields(section));
    }

    [Fact]
    public void Validate_reports_the_section_rather_than_naming_a_field()
    {
        var result = ApplicantInformationRules.Validate(new ApplicantInformation());

        Assert.True(result.Failed);
        Assert.Equal("Applicant Information is not complete.", result.Error);
    }
}

/// <summary>
/// Draft saving made "saved" and "finished" different questions. These pin the second one, which
/// is what submission actually turns on.
/// </summary>
public class SubmitCompletenessTests
{
    private static RentalApplication SavedButEmpty() => new()
    {
        Status = ApplicationStatus.Draft,
        ApplicantInformationSavedAtUtc = DateTime.UtcNow,
        ResidenceHistorySavedAtUtc = DateTime.UtcNow,
        ApplicantInformation = new ApplicantInformation(),
        Residences = []
    };

    [Fact]
    public void A_section_saved_while_still_empty_does_not_unlock_submission()
    {
        var result = ApplicationWorkflow.CanSubmit(SavedButEmpty(), isApplicantOnApplication: true);

        Assert.True(result.Failed);
        Assert.Equal("Applicant Information is not complete.", result.Error);
    }

    [Fact]
    public void An_empty_residence_history_blocks_submission_even_once_saved()
    {
        var application = SavedButEmpty();
        application.ApplicantInformation = new ApplicantInformation
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

        var result = ApplicationWorkflow.CanSubmit(application, isApplicantOnApplication: true);

        Assert.True(result.Failed);
        Assert.Contains("at least one", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_application_a_manager_is_holding_cannot_be_submitted_by_the_applicant()
    {
        var application = SavedButEmpty();
        application.Status = ApplicationStatus.UnderReview;
        application.ClaimedByUserId = "manager-1";

        var result = ApplicationWorkflow.CanSubmit(application, isApplicantOnApplication: true);

        // Under Review has a legal move to Submitted, because that is a manager releasing a claim.
        // Asking the looser question would let an applicant pull the application out from under
        // the review in progress.
        Assert.True(result.Failed);
        Assert.True(ApplicationWorkflow.IsAllowed(ApplicationStatus.UnderReview, ApplicationStatus.Submitted));
    }
}
