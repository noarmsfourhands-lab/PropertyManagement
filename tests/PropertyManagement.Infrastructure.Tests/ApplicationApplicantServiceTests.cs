using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Infrastructure.Identity;
using PropertyManagement.Infrastructure.Persistence;
using PropertyManagement.Application.Services;
using PropertyManagement.Infrastructure.Services;

namespace PropertyManagement.Infrastructure.Tests;

public class ApplicationApplicantServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly ServiceProvider _services;

    private const string SecondEmail = "second@example.com";
    private const string ManagerEmail = "amanager@example.com";

    public ApplicationApplicantServiceTests()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddDbContext<PropertyManagementDbContext>(options => options.UseSqlite(_database.Connection));
        services.AddIdentityCore<ApplicationUser>(options => options.Password.RequiredLength = 8)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<PropertyManagementDbContext>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(TestData.Now));

        _services = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _database.Dispose();
    }

    /// <summary>Creates the accounts these tests add to an application, and returns the starter's id.</summary>
    private async Task<string> SeedUsersAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in UserRole.All)
        {
            await roles.CreateAsync(new IdentityRole(role));
        }

        var starter = new ApplicationUser
        {
            UserName = "starter@example.com",
            Email = "starter@example.com",
            FirstName = "Robin",
            LastName = "Alvarez"
        };
        await users.CreateAsync(starter, "Passw0rd!");
        await users.AddToRoleAsync(starter, UserRole.Applicant);

        var second = new ApplicationUser
        {
            UserName = SecondEmail,
            Email = SecondEmail,
            FirstName = "Sam",
            LastName = "Reyes"
        };
        await users.CreateAsync(second, "Passw0rd!");
        await users.AddToRoleAsync(second, UserRole.Applicant);

        var manager = new ApplicationUser
        {
            UserName = ManagerEmail,
            Email = ManagerEmail,
            FirstName = "Alex",
            LastName = "Chen"
        };
        await users.CreateAsync(manager, "Passw0rd!");
        await users.AddToRoleAsync(manager, UserRole.PropertyManager);

        return starter.Id;
    }

    private async Task<T> WithServiceAsync<T>(Func<IApplicationApplicantService, Task<T>> work)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PropertyManagementDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        return await work(new ApplicationApplicantService(db, users, new FixedTimeProvider(TestData.Now)));
    }

    private async Task<int> StartApplicationAsync(string starterId)
    {
        await using var db = _database.CreateContext();
        var property = await TestData.AddPropertyWithUnitsAsync(db);
        var application = await TestData.AddCompleteDraftAsync(db, property.Units.First().Id, starterId);

        return application.Id;
    }

    [Fact]
    public async Task A_second_applicant_can_be_added_by_email()
    {
        var starterId = await SeedUsersAsync();
        var applicationId = await StartApplicationAsync(starterId);

        var result = await WithServiceAsync(service =>
            service.AddApplicantAsync(applicationId, SecondEmail, starterId));

        Assert.True(result.Succeeded);

        var applicants = await WithServiceAsync(service => service.GetApplicantsAsync(applicationId));

        Assert.Equal(2, applicants.Count);
        Assert.True(applicants[0].IsPrimary);
        Assert.Contains(applicants, entry => entry.Email == SecondEmail && !entry.IsPrimary);
    }

    [Fact]
    public async Task A_second_applicant_gains_the_same_access_as_the_first()
    {
        var starterId = await SeedUsersAsync();
        var applicationId = await StartApplicationAsync(starterId);

        await using var before = _database.CreateContext();
        var applications = new RentalApplicationService(before, new FixedTimeProvider(TestData.Now));

        // Ownership is a set, so adding someone to it is all that granting access takes.
        Assert.False(await applications.IsApplicantOnAsync(applicationId, TestData.OtherApplicant));

        await WithServiceAsync(service =>
            service.AddApplicantAsync(applicationId, SecondEmail, starterId));

        await using var db = _database.CreateContext();
        var secondId = await db.Users.Where(user => user.Email == SecondEmail).Select(user => user.Id).SingleAsync();

        var after = new RentalApplicationService(db, new FixedTimeProvider(TestData.Now));
        Assert.True(await after.IsApplicantOnAsync(applicationId, secondId));

        var stored = await db.RentalApplications.Include(a => a.Applicants).FirstAsync(a => a.Id == applicationId);
        var saved = await after.SaveApplicantInformationAsync(
            new ApplicantInformationInput(
                applicationId, stored.ApplicantInformationVersion,
                "Sam", "Reyes", "555-0101", "sam@example.com",
                "8 Fir Street", null, "Portland", "OR", "97204"),
            secondId);

        Assert.True(saved.Succeeded);
    }

    [Fact]
    public async Task A_property_manager_cannot_be_added_as_an_applicant()
    {
        var starterId = await SeedUsersAsync();
        var applicationId = await StartApplicationAsync(starterId);

        var result = await WithServiceAsync(service =>
            service.AddApplicantAsync(applicationId, ManagerEmail, starterId));

        Assert.True(result.Failed);

        // The same message as an unknown address, so this cannot be used to find out who has an account.
        Assert.Equal("No applicant account uses that email address.", result.Error);
    }

    [Fact]
    public async Task An_unknown_email_is_refused_with_the_same_message()
    {
        var starterId = await SeedUsersAsync();
        var applicationId = await StartApplicationAsync(starterId);

        var result = await WithServiceAsync(service =>
            service.AddApplicantAsync(applicationId, "nobody@example.com", starterId));

        Assert.True(result.Failed);
        Assert.Equal("No applicant account uses that email address.", result.Error);
    }

    [Fact]
    public async Task The_same_applicant_cannot_be_added_twice()
    {
        var starterId = await SeedUsersAsync();
        var applicationId = await StartApplicationAsync(starterId);

        await WithServiceAsync(service => service.AddApplicantAsync(applicationId, SecondEmail, starterId));
        var again = await WithServiceAsync(service => service.AddApplicantAsync(applicationId, SecondEmail, starterId));

        Assert.True(again.Failed);
        Assert.Contains("already on this application", again.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Someone_not_on_the_application_cannot_add_to_it()
    {
        var starterId = await SeedUsersAsync();
        var applicationId = await StartApplicationAsync(starterId);

        var result = await WithServiceAsync(service =>
            service.AddApplicantAsync(applicationId, SecondEmail, "a-stranger"));

        Assert.True(result.Failed);
    }

    [Fact]
    public async Task The_applicant_who_started_it_cannot_be_removed()
    {
        var starterId = await SeedUsersAsync();
        var applicationId = await StartApplicationAsync(starterId);

        var result = await WithServiceAsync(service =>
            service.RemoveApplicantAsync(applicationId, starterId, starterId));

        Assert.True(result.Failed);
        Assert.Contains("Withdraw it instead", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_added_applicant_can_be_removed_again()
    {
        var starterId = await SeedUsersAsync();
        var applicationId = await StartApplicationAsync(starterId);

        await WithServiceAsync(service => service.AddApplicantAsync(applicationId, SecondEmail, starterId));

        await using var db = _database.CreateContext();
        var secondId = await db.Users.Where(user => user.Email == SecondEmail).Select(user => user.Id).SingleAsync();

        var removed = await WithServiceAsync(service =>
            service.RemoveApplicantAsync(applicationId, secondId, starterId));

        Assert.True(removed.Succeeded);

        var applicants = await WithServiceAsync(service => service.GetApplicantsAsync(applicationId));
        Assert.Single(applicants);
    }

    [Fact]
    public async Task Applicants_cannot_be_changed_once_the_application_leaves_an_editable_status()
    {
        var starterId = await SeedUsersAsync();
        var applicationId = await StartApplicationAsync(starterId);

        await using (var db = _database.CreateContext())
        {
            var application = await db.RentalApplications.FirstAsync(entity => entity.Id == applicationId);
            application.Status = ApplicationStatus.Submitted;
            await db.SaveChangesAsync();
        }

        var result = await WithServiceAsync(service =>
            service.AddApplicantAsync(applicationId, SecondEmail, starterId));

        Assert.True(result.Failed);
        Assert.Contains("Submitted", result.Error!, StringComparison.Ordinal);
    }
}
