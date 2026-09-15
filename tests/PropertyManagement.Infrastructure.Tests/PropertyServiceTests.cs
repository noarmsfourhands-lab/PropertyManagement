using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Infrastructure.Services;

namespace PropertyManagement.Infrastructure.Tests;

public class PropertyServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static PropertyService ServiceOver(Persistence.PropertyManagementDbContext db) => new(db);

    [Fact]
    public async Task A_property_is_created_and_then_edited_in_place()
    {
        await using var db = _database.CreateContext();
        var service = ServiceOver(db);

        var created = await service.SavePropertyAsync(
            new PropertyInput(0, "  Alder Court  ", " 10 Alder Street ", null, "Portland", "OR", "97201"));

        Assert.True(created.Succeeded);

        await using var check = _database.CreateContext();
        var property = await check.Properties.SingleAsync();

        // Whitespace is trimmed on the way in rather than being stored and worked around later.
        Assert.Equal("Alder Court", property.Name);
        Assert.Equal("10 Alder Street", property.AddressLine1);

        var edited = await ServiceOver(check).SavePropertyAsync(
            new PropertyInput(property.Id, "Alder Residences", "10 Alder Street", null, "Portland", "OR", "97201"));

        Assert.True(edited.Succeeded);

        await using var after = _database.CreateContext();
        Assert.Equal("Alder Residences", (await after.Properties.SingleAsync()).Name);
        Assert.Equal(1, await after.Properties.CountAsync());
    }

    [Fact]
    public async Task An_active_unit_type_can_be_assigned_to_a_new_unit()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var studio = await db.UnitTypes.FirstAsync(unitType => unitType.IsActive);

        var result = await ServiceOver(db).SaveUnitAsync(
            new UnitInput(0, property.Id, "201", 2, 1750m, studio.Id));

        Assert.True(result.Succeeded);

        await using var check = _database.CreateContext();
        Assert.Equal(3, await check.Units.CountAsync());
    }

    [Fact]
    public async Task A_retired_unit_type_cannot_be_given_to_a_new_unit()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var retired = await db.UnitTypes.FirstAsync(unitType => !unitType.IsActive);

        var result = await ServiceOver(db).SaveUnitAsync(
            new UnitInput(0, property.Id, "201", 2, 1750m, retired.Id));

        Assert.True(result.Failed);
        Assert.Contains("inactive", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_unit_already_on_a_retired_type_may_keep_it_while_other_fields_change()
    {
        await using var setup = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(setup);
        var unit = property.Units.First();
        var retired = await setup.UnitTypes.FirstAsync(unitType => !unitType.IsActive);

        // The type was assigned while it was still active, and then retired.
        unit.UnitTypeId = retired.Id;
        await setup.SaveChangesAsync();

        await using var db = _database.CreateContext();
        var result = await ServiceOver(db).SaveUnitAsync(
            new UnitInput(unit.Id, property.Id, unit.UnitNumber, 3, 2100m, retired.Id));

        Assert.True(result.Succeeded);

        await using var check = _database.CreateContext();
        var saved = await check.Units.FirstAsync(entity => entity.Id == unit.Id);
        Assert.Equal(3, saved.Bedrooms);
        Assert.Equal(retired.Id, saved.UnitTypeId);
    }

    [Fact]
    public async Task A_retired_type_cannot_be_moved_onto_a_different_unit()
    {
        await using var setup = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(setup);
        var retired = await setup.UnitTypes.FirstAsync(unitType => !unitType.IsActive);

        var carrier = property.Units.First();
        carrier.UnitTypeId = retired.Id;
        await setup.SaveChangesAsync();

        var other = property.Units.Last();

        await using var db = _database.CreateContext();
        var result = await ServiceOver(db).SaveUnitAsync(
            new UnitInput(other.Id, property.Id, other.UnitNumber, other.Bedrooms, other.MonthlyRent, retired.Id));

        Assert.True(result.Failed);
    }

    [Fact]
    public async Task The_offered_types_are_the_active_ones_plus_the_units_own_retired_one()
    {
        await using var db = _database.CreateContext();
        await TestData.AddPropertyWithUnitsAsync(db);
        var retired = await db.UnitTypes.FirstAsync(unitType => !unitType.IsActive);

        var forNewUnit = await ServiceOver(db).GetSelectableUnitTypesAsync(null);
        var forRetiredUnit = await ServiceOver(db).GetSelectableUnitTypesAsync(retired.Id);

        Assert.DoesNotContain(forNewUnit, unitType => !unitType.IsActive);
        Assert.Contains(forRetiredUnit, unitType => unitType.Id == retired.Id);
    }

    [Fact]
    public async Task Two_units_in_one_property_cannot_share_a_number()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var studio = await db.UnitTypes.FirstAsync(unitType => unitType.IsActive);

        var result = await ServiceOver(db).SaveUnitAsync(
            new UnitInput(0, property.Id, "101", 1, 1500m, studio.Id));

        Assert.True(result.Failed);
        Assert.Contains("already exists", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_unit_with_an_application_cannot_be_removed()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var unit = property.Units.First();
        await TestData.AddCompleteDraftAsync(db, unit.Id);

        var unitResult = await ServiceOver(db).DeleteUnitAsync(unit.Id);
        var propertyResult = await ServiceOver(db).DeletePropertyAsync(property.Id);

        Assert.True(unitResult.Failed);
        Assert.True(propertyResult.Failed);

        await using var check = _database.CreateContext();
        Assert.Equal(1, await check.Properties.CountAsync());
    }

    [Fact]
    public async Task A_unit_with_no_history_can_be_removed()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);

        var result = await ServiceOver(db).DeleteUnitAsync(property.Units.First().Id);

        Assert.True(result.Succeeded);

        await using var check = _database.CreateContext();
        Assert.Equal(1, await check.Units.CountAsync());
    }

    [Fact]
    public async Task Available_units_are_paged_in_the_database()
    {
        await using var db = _database.CreateContext();
        await TestData.AddPropertyWithUnitsAsync(db);

        var service = ServiceOver(db);

        var first = await service.GetAvailableUnitsAsync(TestData.Today, take: 1);
        var second = await service.GetAvailableUnitsAsync(TestData.Today, take: 1, skip: 1);
        var past = await service.GetAvailableUnitsAsync(TestData.Today, take: 1, skip: 99);

        Assert.Equal(2, first.TotalCount);
        Assert.Single(first.Units);
        Assert.Equal("101", first.Units.Single().UnitNumber);

        // The count is of everything matching, not of the page, so paging can be rendered.
        Assert.Equal(2, second.TotalCount);
        Assert.Equal("102", second.Units.Single().UnitNumber);

        // A page past the end is empty rather than an error.
        Assert.Empty(past.Units);
        Assert.Equal(2, past.TotalCount);
    }

    [Fact]
    public async Task A_leased_unit_drops_out_of_the_available_list()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var unit = property.Units.First();
        var application = await TestData.AddCompleteDraftAsync(db, unit.Id);

        db.Leases.Add(new Lease
        {
            UnitId = unit.Id,
            RentalApplicationId = application.Id,
            StartDate = TestData.Today.AddMonths(-1),
            EndDate = LeaseTerm.EndDateFor(TestData.Today.AddMonths(-1)),
            MonthlyRent = unit.MonthlyRent
        });
        await db.SaveChangesAsync();

        await using var check = _database.CreateContext();
        var available = await ServiceOver(check).GetAvailableUnitsAsync(TestData.Today, take: 10);

        Assert.Equal(1, available.TotalCount);
        Assert.Equal("102", available.Units.Single().UnitNumber);
    }

    [Fact]
    public async Task A_unit_becomes_available_again_the_day_after_its_lease_ends()
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var unit = property.Units.First();
        var application = await TestData.AddCompleteDraftAsync(db, unit.Id);

        var start = TestData.Today.AddYears(-1);
        db.Leases.Add(new Lease
        {
            UnitId = unit.Id,
            RentalApplicationId = application.Id,
            StartDate = start,
            EndDate = LeaseTerm.EndDateFor(start),
            MonthlyRent = unit.MonthlyRent
        });
        await db.SaveChangesAsync();

        await using var check = _database.CreateContext();
        var service = ServiceOver(check);

        var onTheLastDay = await service.GetAvailableUnitsAsync(LeaseTerm.EndDateFor(start), take: 10);
        var theDayAfter = await service.GetAvailableUnitsAsync(LeaseTerm.EndDateFor(start).AddDays(1), take: 10);

        Assert.Equal(1, onTheLastDay.TotalCount);
        Assert.Equal(2, theDayAfter.TotalCount);
    }
}
