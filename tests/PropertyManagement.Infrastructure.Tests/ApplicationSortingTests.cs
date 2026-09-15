using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Application.Services;
using PropertyManagement.Infrastructure.Services;

namespace PropertyManagement.Infrastructure.Tests;

/// <summary>
/// Sorting is part of the list's contract, so it is checked against a real database rather than
/// assumed: an ordering that does not translate would otherwise only surface at runtime.
/// </summary>
public class ApplicationSortingTests : IDisposable
{
    private readonly TestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private RentalApplicationService ServiceOver(Persistence.PropertyManagementDbContext db) =>
        new(db, new FixedTimeProvider(TestData.Now));

    /// <summary>Two properties, so ordering by property name has something to order.</summary>
    private async Task SeedAsync(Persistence.PropertyManagementDbContext db)
    {
        var first = await TestData.AddPropertyWithUnitsAsync(db);

        var second = new Domain.Entities.Property
        {
            Name = "Birch Commons",
            AddressLine1 = "2 Birch Way",
            City = "Portland",
            State = "OR",
            PostalCode = "97203",
            Units =
            [
                new Domain.Entities.Unit
                {
                    UnitNumber = "201",
                    Bedrooms = 1,
                    MonthlyRent = 1200m,
                    UnitTypeId = first.Units.First().UnitTypeId
                }
            ]
        };

        db.Properties.Add(second);
        await db.SaveChangesAsync();

        var alder = await TestData.AddCompleteDraftAsync(db, first.Units.First().Id);
        var birch = await TestData.AddCompleteDraftAsync(db, second.Units.First().Id, TestData.OtherApplicant);

        // Give them different names and submission times so every ordering is distinguishable.
        alder.ApplicantInformation.LastName = "Zeta";
        birch.ApplicantInformation.LastName = "Alpha";
        birch.Status = ApplicationStatus.Submitted;
        birch.SubmittedAtUtc = TestData.Now;
        alder.SubmittedAtUtc = TestData.Now.AddDays(-5);
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(ApplicationSort.Property, false, "Alder Court")]
    [InlineData(ApplicationSort.Property, true, "Birch Commons")]
    [InlineData(ApplicationSort.Unit, false, "Alder Court")]
    [InlineData(ApplicationSort.Unit, true, "Birch Commons")]
    [InlineData(ApplicationSort.Applicant, false, "Birch Commons")]
    [InlineData(ApplicationSort.Applicant, true, "Alder Court")]
    [InlineData(ApplicationSort.Submitted, true, "Birch Commons")]
    [InlineData(ApplicationSort.Submitted, false, "Alder Court")]
    [InlineData(ApplicationSort.Status, false, "Alder Court")]
    public async Task Each_ordering_puts_the_expected_row_first(
        ApplicationSort sort,
        bool descending,
        string expectedProperty)
    {
        await using var db = _database.CreateContext();
        await SeedAsync(db);

        var page = await ServiceOver(db).ListAsync(
            new ApplicationListFilter(Sort: sort, Descending: descending),
            null);

        Assert.Equal(expectedProperty, page.Rows[0].PropertyName);
    }

    [Fact]
    public async Task Sorting_and_filtering_combine()
    {
        await using var db = _database.CreateContext();
        await SeedAsync(db);

        var page = await ServiceOver(db).ListAsync(
            new ApplicationListFilter(Status: ApplicationStatus.Submitted, Sort: ApplicationSort.Property),
            null);

        Assert.Single(page.Rows);
        Assert.Equal("Birch Commons", page.Rows[0].PropertyName);
    }

    [Fact]
    public async Task An_unrecognised_sort_falls_back_rather_than_throwing()
    {
        await using var db = _database.CreateContext();
        await SeedAsync(db);

        var page = await ServiceOver(db).ListAsync(
            new ApplicationListFilter(Sort: (ApplicationSort)99),
            null);

        Assert.Equal(2, page.TotalCount);
    }
}
