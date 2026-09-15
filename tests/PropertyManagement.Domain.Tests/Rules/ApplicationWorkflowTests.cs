using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;

namespace PropertyManagement.Domain.Tests.Rules;

public class ApplicationWorkflowTests
{
    private const string Manager = "manager-1";
    private const string OtherManager = "manager-2";

    private static RentalApplication ApplicationIn(
        ApplicationStatus status,
        bool applicantInformationSaved = true,
        bool residenceHistorySaved = true,
        string? claimedBy = null) =>
        new()
        {
            Id = 1,
            UnitId = 1,
            Status = status,
            ApplicantInformationSavedAtUtc = applicantInformationSaved ? DateTime.UtcNow : null,
            ResidenceHistorySavedAtUtc = residenceHistorySaved ? DateTime.UtcNow : null,
            ClaimedByUserId = claimedBy,
            // Complete on purpose. These tests are about which statuses permit which actions, so
            // the contents must not be what stops them; completeness has its own tests.
            ApplicantInformation = new ApplicantInformation
            {
                FirstName = "Robin",
                LastName = "Alvarez",
                Phone = "555-0100",
                Email = "robin@example.com",
                AddressLine1 = "4 Cedar Lane",
                City = "Portland",
                State = "OR",
                PostalCode = "97202"
            },
            Residences = [new Residence { MoveInDate = new DateOnly(2024, 1, 1) }]
        };

    [Theory]
    [InlineData(ApplicationStatus.Approved)]
    [InlineData(ApplicationStatus.Denied)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public void Approved_denied_and_withdrawn_are_terminal(ApplicationStatus status)
    {
        Assert.True(ApplicationWorkflow.IsTerminal(status));

        foreach (var target in Enum.GetValues<ApplicationStatus>())
        {
            Assert.False(ApplicationWorkflow.IsAllowed(status, target));
        }
    }

    [Theory]
    [InlineData(ApplicationStatus.Draft)]
    [InlineData(ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.UnderReview)]
    [InlineData(ApplicationStatus.Returned)]
    public void Open_statuses_are_not_terminal(ApplicationStatus status)
    {
        Assert.False(ApplicationWorkflow.IsTerminal(status));
    }

    [Theory]
    [InlineData(ApplicationStatus.Draft, true)]
    [InlineData(ApplicationStatus.Returned, true)]
    [InlineData(ApplicationStatus.Submitted, false)]
    [InlineData(ApplicationStatus.UnderReview, false)]
    [InlineData(ApplicationStatus.Approved, false)]
    [InlineData(ApplicationStatus.Denied, false)]
    [InlineData(ApplicationStatus.Withdrawn, false)]
    public void An_applicant_can_edit_only_a_draft_or_returned_application(ApplicationStatus status, bool expected)
    {
        Assert.Equal(expected, ApplicationWorkflow.ApplicantCanEdit(status));
    }

    [Fact]
    public void A_property_manager_never_edits_the_applicant_sections()
    {
        Assert.False(ApplicationWorkflow.CanEdit(ApplicationStatus.Draft, isApplicantOnApplication: false));
        Assert.False(ApplicationWorkflow.CanEdit(ApplicationStatus.Returned, isApplicantOnApplication: false));
    }

    [Fact]
    public void Saving_is_rejected_for_a_user_who_is_not_on_the_application()
    {
        var result = ApplicationWorkflow.CanSave(ApplicationIn(ApplicationStatus.Draft), isApplicantOnApplication: false);

        Assert.True(result.Failed);
        Assert.Equal("Only an applicant on this application can change it.", result.Error);
    }

    [Theory]
    [InlineData(ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.UnderReview)]
    [InlineData(ApplicationStatus.Approved)]
    [InlineData(ApplicationStatus.Denied)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public void Saving_is_rejected_once_the_application_leaves_an_editable_status(ApplicationStatus status)
    {
        var result = ApplicationWorkflow.CanSave(ApplicationIn(status), isApplicantOnApplication: true);

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(ApplicationStatus.Draft)]
    [InlineData(ApplicationStatus.Returned)]
    public void An_applicant_can_submit_a_draft_and_resubmit_a_returned_application(ApplicationStatus status)
    {
        var result = ApplicationWorkflow.CanSubmit(ApplicationIn(status), isApplicantOnApplication: true);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Submit_is_blocked_until_applicant_information_has_been_saved()
    {
        var application = ApplicationIn(ApplicationStatus.Draft, applicantInformationSaved: false);

        var result = ApplicationWorkflow.CanSubmit(application, isApplicantOnApplication: true);

        Assert.True(result.Failed);
        Assert.Equal("Applicant Information must be saved before submitting.", result.Error);
    }

    [Fact]
    public void Submit_is_blocked_until_residence_history_has_been_saved()
    {
        var application = ApplicationIn(ApplicationStatus.Draft, residenceHistorySaved: false);

        var result = ApplicationWorkflow.CanSubmit(application, isApplicantOnApplication: true);

        Assert.True(result.Failed);
        Assert.Equal("Residence History must be saved before submitting.", result.Error);
    }

    [Fact]
    public void An_already_submitted_application_cannot_be_submitted_again()
    {
        var result = ApplicationWorkflow.CanSubmit(
            ApplicationIn(ApplicationStatus.Submitted),
            isApplicantOnApplication: true);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Submit_is_rejected_for_a_user_who_is_not_on_the_application()
    {
        var result = ApplicationWorkflow.CanSubmit(
            ApplicationIn(ApplicationStatus.Draft),
            isApplicantOnApplication: false);

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(ApplicationStatus.Draft)]
    [InlineData(ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.UnderReview)]
    [InlineData(ApplicationStatus.Returned)]
    public void An_applicant_can_withdraw_any_open_application(ApplicationStatus status)
    {
        var result = ApplicationWorkflow.CanWithdraw(ApplicationIn(status), isApplicantOnApplication: true);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(ApplicationStatus.Approved)]
    [InlineData(ApplicationStatus.Denied)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public void A_terminal_application_cannot_be_withdrawn(ApplicationStatus status)
    {
        var result = ApplicationWorkflow.CanWithdraw(ApplicationIn(status), isApplicantOnApplication: true);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Only_a_submitted_application_can_be_claimed()
    {
        Assert.True(ApplicationWorkflow.CanClaim(ApplicationIn(ApplicationStatus.Submitted)).Succeeded);
        Assert.True(ApplicationWorkflow.CanClaim(ApplicationIn(ApplicationStatus.Draft)).Failed);
        Assert.True(ApplicationWorkflow.CanClaim(
            ApplicationIn(ApplicationStatus.UnderReview, claimedBy: Manager)).Failed);
    }

    [Fact]
    public void Only_the_holding_manager_can_release_a_claimed_application()
    {
        var claimed = ApplicationIn(ApplicationStatus.UnderReview, claimedBy: Manager);

        Assert.True(ApplicationWorkflow.CanRelease(claimed, Manager).Succeeded);

        var byOther = ApplicationWorkflow.CanRelease(claimed, OtherManager);
        Assert.True(byOther.Failed);
        Assert.Equal("This application is claimed by another property manager.", byOther.Error);
    }

    [Fact]
    public void An_unclaimed_application_cannot_be_released()
    {
        Assert.True(ApplicationWorkflow.CanRelease(ApplicationIn(ApplicationStatus.Submitted), Manager).Failed);
    }

    [Fact]
    public void Any_manager_can_review_an_unclaimed_submitted_application()
    {
        Assert.True(ApplicationWorkflow.CanReview(ApplicationIn(ApplicationStatus.Submitted), Manager).Succeeded);
        Assert.True(ApplicationWorkflow.CanReview(ApplicationIn(ApplicationStatus.Submitted), OtherManager).Succeeded);
    }

    [Fact]
    public void A_claimed_application_is_reviewable_only_by_the_manager_holding_it()
    {
        var claimed = ApplicationIn(ApplicationStatus.UnderReview, claimedBy: Manager);

        Assert.True(ApplicationWorkflow.CanReview(claimed, Manager).Succeeded);
        Assert.True(ApplicationWorkflow.CanReview(claimed, OtherManager).Failed);
    }

    [Theory]
    [InlineData(ApplicationStatus.Draft)]
    [InlineData(ApplicationStatus.Returned)]
    [InlineData(ApplicationStatus.Approved)]
    [InlineData(ApplicationStatus.Denied)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public void An_application_that_is_not_awaiting_review_cannot_be_reviewed(ApplicationStatus status)
    {
        Assert.True(ApplicationWorkflow.CanReview(ApplicationIn(status), Manager).Failed);
    }

    [Fact]
    public void A_returned_application_can_go_back_to_submitted()
    {
        Assert.True(ApplicationWorkflow.IsAllowed(ApplicationStatus.Returned, ApplicationStatus.Submitted));
    }

    [Fact]
    public void A_claimed_application_can_be_released_back_to_the_queue()
    {
        Assert.True(ApplicationWorkflow.IsAllowed(ApplicationStatus.UnderReview, ApplicationStatus.Submitted));
    }

    [Fact]
    public void A_draft_cannot_jump_straight_to_a_decision()
    {
        Assert.False(ApplicationWorkflow.IsAllowed(ApplicationStatus.Draft, ApplicationStatus.Approved));
        Assert.False(ApplicationWorkflow.IsAllowed(ApplicationStatus.Draft, ApplicationStatus.Denied));
        Assert.False(ApplicationWorkflow.IsAllowed(ApplicationStatus.Draft, ApplicationStatus.Returned));
    }
}
