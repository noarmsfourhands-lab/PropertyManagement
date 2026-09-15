using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Application.Services;
using PropertyManagement.Infrastructure.Services;

namespace PropertyManagement.Infrastructure.Tests;

/// <summary>
/// The service checks a unit's availability and then writes a lease in a separate statement, so
/// two approvals that interleave can both pass the check. These pin the constraint that stops the
/// second write, which is the part the check cannot do on its own.
/// </summary>
public class LeaseConstraintTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task One_unit_cannot_hold_two_leases_starting_the_same_day()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var unit = property.Units.First();

        var first = await TestData.AddCompleteDraftAsync(db, unit.Id, TestData.Applicant);
        var second = await TestData.AddCompleteDraftAsync(db, unit.Id, TestData.OtherApplicant);

        db.Leases.Add(LeaseFor(unit.Id, first.Id, TestData.Today));
        await db.SaveChangesAsync();

        // Stands in for the losing half of a race: both requests read the unit's leases before
        // either wrote, so both got past the availability check.
        db.Leases.Add(LeaseFor(unit.Id, second.Id, TestData.Today));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());

        await using var check = _database.CreateContext();
        Assert.Equal(1, await check.Leases.CountAsync());
    }

    [Fact]
    public async Task Different_units_may_of_course_start_leases_on_the_same_day()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);

        var first = await TestData.AddCompleteDraftAsync(db, property.Units.First().Id);
        var second = await TestData.AddCompleteDraftAsync(db, property.Units.Last().Id);

        db.Leases.Add(LeaseFor(property.Units.First().Id, first.Id, TestData.Today));
        db.Leases.Add(LeaseFor(property.Units.Last().Id, second.Id, TestData.Today));

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.Leases.CountAsync());
    }

    private static Domain.Entities.Lease LeaseFor(int unitId, int applicationId, DateOnly start) => new()
    {
        UnitId = unitId,
        RentalApplicationId = applicationId,
        StartDate = start,
        EndDate = LeaseTerm.EndDateFor(start),
        MonthlyRent = 1500m
    };
}
