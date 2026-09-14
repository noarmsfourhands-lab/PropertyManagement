using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;

namespace PropertyManagement.Domain.Tests.Rules;

public class ReviewRulesTests
{
    [Theory]
    [InlineData(ReviewOutcome.Return, true)]
    [InlineData(ReviewOutcome.Deny, true)]
    [InlineData(ReviewOutcome.Approve, false)]
    public void Return_and_deny_require_a_comment(ReviewOutcome outcome, bool expected)
    {
        Assert.Equal(expected, ReviewRules.RequiresComment(outcome));
    }

    [Theory]
    [InlineData(ReviewOutcome.Return)]
    [InlineData(ReviewOutcome.Deny)]
    public void A_missing_comment_is_rejected_for_return_and_deny(ReviewOutcome outcome)
    {
        Assert.True(ReviewRules.ValidateComment(outcome, null).Failed);
        Assert.True(ReviewRules.ValidateComment(outcome, string.Empty).Failed);
        Assert.True(ReviewRules.ValidateComment(outcome, "   ").Failed);
    }

    [Theory]
    [InlineData(ReviewOutcome.Return)]
    [InlineData(ReviewOutcome.Deny)]
    public void A_supplied_comment_satisfies_return_and_deny(ReviewOutcome outcome)
    {
        Assert.True(ReviewRules.ValidateComment(outcome, "Please correct the move-out dates.").Succeeded);
    }

    [Fact]
    public void Approve_needs_no_comment_but_still_accepts_one()
    {
        Assert.True(ReviewRules.ValidateComment(ReviewOutcome.Approve, null).Succeeded);
        Assert.True(ReviewRules.ValidateComment(ReviewOutcome.Approve, "Looks good.").Succeeded);
    }

    [Fact]
    public void The_rejection_message_names_the_outcome()
    {
        var result = ReviewRules.ValidateComment(ReviewOutcome.Deny, null);

        Assert.Equal("A comment is required when the outcome is Deny.", result.Error);
    }

    [Theory]
    [InlineData(ReviewOutcome.Approve, ApplicationStatus.Approved)]
    [InlineData(ReviewOutcome.Return, ApplicationStatus.Returned)]
    [InlineData(ReviewOutcome.Deny, ApplicationStatus.Denied)]
    public void Each_outcome_maps_to_its_status(ReviewOutcome outcome, ApplicationStatus expected)
    {
        Assert.Equal(expected, ReviewRules.ResultingStatus(outcome));
    }

    [Fact]
    public void Every_outcome_produces_a_status_the_workflow_allows_from_submitted()
    {
        foreach (var outcome in Enum.GetValues<ReviewOutcome>())
        {
            var target = ReviewRules.ResultingStatus(outcome);

            Assert.True(ApplicationWorkflow.IsAllowed(ApplicationStatus.Submitted, target));
            Assert.True(ApplicationWorkflow.IsAllowed(ApplicationStatus.UnderReview, target));
        }
    }

    [Theory]
    [InlineData(ReviewOutcome.Approve, true)]
    [InlineData(ReviewOutcome.Return, false)]
    [InlineData(ReviewOutcome.Deny, false)]
    public void Only_approval_issues_a_lease(ReviewOutcome outcome, bool expected)
    {
        Assert.Equal(expected, ReviewRules.IssuesLease(outcome));
    }

    [Fact]
    public void An_unknown_outcome_is_a_programming_error_not_a_silent_default()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReviewRules.ResultingStatus((ReviewOutcome)99));
    }
}
