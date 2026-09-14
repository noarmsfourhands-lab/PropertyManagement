using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Rules;

namespace PropertyManagement.Domain.Tests.Rules;

public class LeaseTermTests
{
    [Theory]
    [InlineData("2026-01-01", "2026-12-31")]
    [InlineData("2026-06-15", "2027-06-14")]
    [InlineData("2026-12-31", "2027-12-30")]
    public void EndDateFor_returns_the_inclusive_last_day_of_a_twelve_month_term(string start, string expectedEnd)
    {
        var actual = LeaseTerm.EndDateFor(DateOnly.Parse(start));

        Assert.Equal(DateOnly.Parse(expectedEnd), actual);
    }

    [Fact]
    public void EndDateFor_clamps_a_leap_day_start_to_the_end_of_february()
    {
        // 2024-02-29 plus twelve months has no 29th, so the term ends one day before 2025-02-28.
        var actual = LeaseTerm.EndDateFor(new DateOnly(2024, 2, 29));

        Assert.Equal(new DateOnly(2025, 2, 27), actual);
    }

    [Fact]
    public void EndDateFor_spans_exactly_one_year_less_a_day()
    {
        var start = new DateOnly(2026, 3, 1);

        var end = LeaseTerm.EndDateFor(start);

        Assert.Equal(start.AddYears(1).AddDays(-1), end);
    }

    [Fact]
    public void Issue_builds_a_twelve_month_lease_carrying_the_units_rent()
    {
        var unit = new Unit { Id = 7, MonthlyRent = 1850m };
        var application = new RentalApplication { Id = 42, UnitId = 7 };
        var start = new DateOnly(2026, 4, 1);

        var lease = LeaseTerm.Issue(unit, application, start);

        Assert.Equal(7, lease.UnitId);
        Assert.Equal(42, lease.RentalApplicationId);
        Assert.Equal(start, lease.StartDate);
        Assert.Equal(new DateOnly(2027, 3, 31), lease.EndDate);
        Assert.Equal(1850m, lease.MonthlyRent);
    }

    [Theory]
    [InlineData("2026-01-01", true)]   // first day, inclusive
    [InlineData("2026-06-30", true)]   // mid term
    [InlineData("2026-12-31", true)]   // last day, inclusive
    [InlineData("2025-12-31", false)]  // day before it starts
    [InlineData("2027-01-01", false)]  // day after it ends
    public void CoversDate_includes_both_boundaries(string date, bool expected)
    {
        var lease = new Lease
        {
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31)
        };

        Assert.Equal(expected, lease.CoversDate(DateOnly.Parse(date)));
    }

    [Fact]
    public void A_unit_with_no_leases_is_available()
    {
        Assert.True(LeaseTerm.IsUnitAvailable([], new DateOnly(2026, 5, 1)));
    }

    [Fact]
    public void A_unit_is_unavailable_while_a_lease_term_covers_the_date()
    {
        Lease[] leases =
        [
            new() { StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) }
        ];

        Assert.False(LeaseTerm.IsUnitAvailable(leases, new DateOnly(2026, 5, 1)));
        Assert.True(LeaseTerm.HasActiveLease(leases, new DateOnly(2026, 5, 1)));
    }

    [Fact]
    public void A_unit_whose_only_lease_has_expired_is_available_again()
    {
        Lease[] leases =
        [
            new() { StartDate = new DateOnly(2024, 1, 1), EndDate = new DateOnly(2024, 12, 31) }
        ];

        Assert.True(LeaseTerm.IsUnitAvailable(leases, new DateOnly(2026, 5, 1)));
    }

    [Fact]
    public void An_expired_lease_does_not_mask_a_current_one()
    {
        Lease[] leases =
        [
            new() { StartDate = new DateOnly(2024, 1, 1), EndDate = new DateOnly(2024, 12, 31) },
            new() { StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) }
        ];

        Assert.False(LeaseTerm.IsUnitAvailable(leases, new DateOnly(2026, 5, 1)));
    }

    [Fact]
    public void A_lease_that_has_not_started_yet_leaves_the_unit_available_today()
    {
        Lease[] leases =
        [
            new() { StartDate = new DateOnly(2027, 1, 1), EndDate = new DateOnly(2027, 12, 31) }
        ];

        Assert.True(LeaseTerm.IsUnitAvailable(leases, new DateOnly(2026, 5, 1)));
    }
}
