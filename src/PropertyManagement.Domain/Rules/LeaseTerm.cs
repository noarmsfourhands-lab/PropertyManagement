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
    ///
    /// The subtraction is skipped when the anniversary had to be clamped, which for a twelve-month
    /// term happens only from 29 February. AddMonths lands 2028-02-29 on 2029-02-28 because there
    /// is no 29th to land on, and taking a further day off would shorten the term twice: the lease
    /// would end 2029-02-27 and the unit would read as available a day before its lease ran out.
    /// </summary>
    public static DateOnly EndDateFor(DateOnly startDate)
    {
        var anniversary = startDate.AddMonths(Months);

        return anniversary.Day == startDate.Day ? anniversary.AddDays(-1) : anniversary;
    }

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
            MonthlyRent = unit.MonthlyRent,

            // Copied, not referenced, for the same reason the rent is: the lease has to keep
            // describing what it is for after the unit it points at has been edited.
            PropertyName = unit.Property?.Name ?? string.Empty,
            UnitNumber = unit.UnitNumber
        };

    /// <summary>A unit whose lease term covers <paramref name="asOf"/> is not available.</summary>
    public static bool HasActiveLease(IEnumerable<Lease> leases, DateOnly asOf) =>
        leases.Any(lease => lease.CoversDate(asOf));

    public static bool IsUnitAvailable(IEnumerable<Lease> leases, DateOnly asOf) =>
        !HasActiveLease(leases, asOf);
}
