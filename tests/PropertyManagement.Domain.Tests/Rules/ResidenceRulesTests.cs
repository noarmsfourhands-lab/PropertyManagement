using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Rules;

namespace PropertyManagement.Domain.Tests.Rules;

public class ResidenceRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 14);

    [Fact]
    public void A_completed_stay_in_the_past_is_valid()
    {
        var result = ResidenceRules.ValidateDates(
            new DateOnly(2023, 1, 1),
            new DateOnly(2025, 6, 30),
            Today);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void A_residence_the_applicant_has_not_left_is_valid()
    {
        var result = ResidenceRules.ValidateDates(new DateOnly(2024, 3, 1), null, Today);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Moving_in_and_out_on_the_same_day_is_allowed()
    {
        var sameDay = new DateOnly(2025, 4, 10);

        Assert.True(ResidenceRules.ValidateDates(sameDay, sameDay, Today).Succeeded);
    }

    [Fact]
    public void A_stay_that_ends_today_is_allowed()
    {
        Assert.True(ResidenceRules.ValidateDates(new DateOnly(2024, 1, 1), Today, Today).Succeeded);
    }

    [Fact]
    public void A_move_in_date_in_the_future_is_rejected()
    {
        var result = ResidenceRules.ValidateDates(Today.AddDays(1), null, Today);

        Assert.True(result.Failed);
        Assert.Equal("Move-in date cannot be in the future.", result.Error);
    }

    [Fact]
    public void A_move_out_date_before_the_move_in_date_is_rejected()
    {
        var result = ResidenceRules.ValidateDates(
            new DateOnly(2025, 6, 1),
            new DateOnly(2025, 5, 31),
            Today);

        Assert.True(result.Failed);
        Assert.Equal("Move-out date cannot be before the move-in date.", result.Error);
    }

    [Fact]
    public void A_move_out_date_in_the_future_is_rejected()
    {
        var result = ResidenceRules.ValidateDates(new DateOnly(2024, 1, 1), Today.AddDays(1), Today);

        Assert.True(result.Failed);
        Assert.Equal("Move-out date cannot be in the future.", result.Error);
    }

    [Fact]
    public void An_empty_residence_history_cannot_be_continued_past()
    {
        var result = ResidenceRules.ValidateHistory([]);

        Assert.True(result.Failed);
        Assert.Equal("Add at least one previous residence before continuing.", result.Error);
    }

    [Fact]
    public void One_residence_is_enough_to_continue()
    {
        Residence[] residences = [new() { MoveInDate = new DateOnly(2024, 1, 1) }];

        Assert.True(ResidenceRules.ValidateHistory(residences).Succeeded);
    }

    [Fact]
    public void Review_order_puts_the_most_recent_stay_first()
    {
        Residence[] residences =
        [
            new() { AddressLine1 = "Oldest", MoveInDate = new DateOnly(2018, 1, 1), MoveOutDate = new DateOnly(2020, 1, 1) },
            new() { AddressLine1 = "Newest", MoveInDate = new DateOnly(2022, 1, 1), MoveOutDate = new DateOnly(2025, 1, 1) },
            new() { AddressLine1 = "Middle", MoveInDate = new DateOnly(2020, 2, 1), MoveOutDate = new DateOnly(2022, 1, 1) }
        ];

        var ordered = ResidenceRules.InReviewOrder(residences);

        Assert.Equal(["Newest", "Middle", "Oldest"], ordered.Select(residence => residence.AddressLine1));
    }

    [Fact]
    public void A_residence_not_yet_left_sorts_above_every_completed_stay()
    {
        Residence[] residences =
        [
            new() { AddressLine1 = "Moved out last year", MoveInDate = new DateOnly(2022, 1, 1), MoveOutDate = new DateOnly(2025, 1, 1) },
            new() { AddressLine1 = "Still there", MoveInDate = new DateOnly(2019, 1, 1), MoveOutDate = null }
        ];

        var ordered = ResidenceRules.InReviewOrder(residences);

        Assert.Equal("Still there", ordered[0].AddressLine1);
    }
}
