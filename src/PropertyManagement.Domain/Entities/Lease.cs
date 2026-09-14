namespace PropertyManagement.Domain.Entities;

/// <summary>
/// A twelve-month tenancy created when an application is approved.
/// <see cref="EndDate"/> is inclusive: a lease starting 2026-01-01 runs through 2026-12-31.
/// </summary>
public class Lease
{
    public int Id { get; set; }

    public int UnitId { get; set; }

    public Unit Unit { get; set; } = null!;

    public int RentalApplicationId { get; set; }

    public RentalApplication RentalApplication { get; set; } = null!;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public decimal MonthlyRent { get; set; }

    /// <summary>True when <paramref name="date"/> falls inside the lease term, bounds included.</summary>
    public bool CoversDate(DateOnly date) => date >= StartDate && date <= EndDate;
}
