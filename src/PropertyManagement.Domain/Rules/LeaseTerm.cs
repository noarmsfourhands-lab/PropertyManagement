using PropertyManagement.Domain.Entities;

namespace PropertyManagement.Domain.Rules;

/// <summary>
/// Lease term arithmetic and unit availability. Every method takes the date it should reason
/// about instead of reading the clock, so the rules are deterministic under test and the web
/// layer stays the only place that decides what "today" means.
/// </summary>
public static class LeaseTerm
{
    /// <summary>Approval issues a lease of this length.</summary>
    public const int Months = 12;

    /// <summary>
    /// Inclusive last day of a twelve-month term. A lease starting 2026-01-01 ends 2026-12-31.
    /// </summary>
    public static DateOnly EndDateFor(DateOnly startDate) =>
        startDate.AddMonths(Months).AddDays(-1);

    /// <summary>Builds the lease that approval issues for a unit.</summary>
    public static Lease Issue(Unit unit, RentalApplication application, DateOnly startDate) =>
        new()
        {
            UnitId = unit.Id,
            Unit = unit,
            RentalApplicationId = application.Id,
            RentalApplication = application,
            StartDate = startDate,
            EndDate = EndDateFor(startDate),
            MonthlyRent = unit.MonthlyRent
        };

    /// <summary>A unit whose lease term covers <paramref name="asOf"/> is not available.</summary>
    public static bool HasActiveLease(IEnumerable<Lease> leases, DateOnly asOf) =>
        leases.Any(lease => lease.CoversDate(asOf));

    public static bool IsUnitAvailable(IEnumerable<Lease> leases, DateOnly asOf) =>
        !HasActiveLease(leases, asOf);
}
