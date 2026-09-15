using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Infrastructure.Services;

/// <summary>Who is performing an action. The name is copied onto audit entries as they are written.</summary>
public record Actor(string UserId, string Name);

/// <summary>Section one, as posted. <paramref name="Version"/> is the token the page was rendered with.</summary>
public record ApplicantInformationInput(
    int ApplicationId,
    Guid Version,
    string? FirstName,
    string? LastName,
    string? Phone,
    string? Email,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode);

/// <summary>One residence, as posted from the modal. Id is zero when adding.</summary>
public record ResidenceInput(
    int Id,
    int ApplicationId,
    string AddressLine1,
    string? AddressLine2,
    string City,
    string State,
    string PostalCode,
    string LandlordName,
    string LandlordPhone,
    DateOnly MoveInDate,
    DateOnly? MoveOutDate);

/// <summary>
/// The columns the list can be ordered by. An enum rather than a column name from the request, so
/// the sort can never become a way to inject a fragment of SQL or to order by something private.
/// </summary>
public enum ApplicationSort
{
    Submitted = 0,
    Status = 1,
    Property = 2,
    Unit = 3,
    Applicant = 4
}

/// <summary>What the list is filtered, sorted and paged by. Both filters are optional.</summary>
public record ApplicationListFilter(
    ApplicationStatus? Status = null,
    int? PropertyId = null,
    int Page = 1,
    int PageSize = 20,
    ApplicationSort Sort = ApplicationSort.Submitted,
    bool Descending = true);

/// <summary>One row of the list. Projected in the database; no entity graph is loaded.</summary>
public record ApplicationListRow(
    int Id,
    string PropertyName,
    string UnitNumber,
    string ApplicantName,
    ApplicationStatus Status,
    DateTime CreatedAtUtc,
    DateTime? SubmittedAtUtc);

public record ApplicationListPage(
    IReadOnlyList<ApplicationListRow> Rows,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int PageCount => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page < PageCount;
}

public interface IRentalApplicationService
{
    Task<RentalApplication?> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> IsApplicantOnAsync(int applicationId, string userId, CancellationToken cancellationToken = default);

    Task<DomainResult<int>> StartAsync(int unitId, Actor actor, DateOnly asOf, CancellationToken cancellationToken = default);

    Task<DomainResult> SaveApplicantInformationAsync(
        ApplicantInformationInput input,
        string userId,
        CancellationToken cancellationToken = default);

    Task<DomainResult> SaveResidenceHistoryAsync(
        int applicationId,
        Guid version,
        string userId,
        bool requireComplete = true,
        CancellationToken cancellationToken = default);

    Task<DomainResult> SaveResidenceAsync(
        ResidenceInput input,
        string userId,
        DateOnly asOf,
        CancellationToken cancellationToken = default);

    Task<DomainResult> DeleteResidenceAsync(int residenceId, string userId, CancellationToken cancellationToken = default);

    Task<DomainResult> SubmitAsync(int applicationId, Actor actor, DateOnly asOf, CancellationToken cancellationToken = default);

    Task<DomainResult> WithdrawAsync(int applicationId, Actor actor, CancellationToken cancellationToken = default);

    Task<ApplicationListPage> ListAsync(
        ApplicationListFilter filter,
        string? applicantUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Property>> GetFilterPropertiesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Everything an applicant does to an application. The guards in
/// <see cref="ApplicationWorkflow"/> run here rather than in the controller, so a request that
/// forges a status, an owner or a section is rejected whatever the page offered.
/// </summary>
public class RentalApplicationService(PropertyManagementDbContext db, TimeProvider timeProvider)
    : IRentalApplicationService
{
    private const string StaleSaveMessage =
        "Someone else saved this section while you were editing. Reload the page and apply your changes again.";

    public async Task<RentalApplication?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        await db.RentalApplications
            .Include(application => application.Unit)
                .ThenInclude(unit => unit.Property)
            .Include(application => application.Unit)
                .ThenInclude(unit => unit.UnitType)
            .Include(application => application.Residences)
            .Include(application => application.Applicants)
            .Include(application => application.Lease)
            .FirstOrDefaultAsync(application => application.Id == id, cancellationToken);

    /// <summary>
    /// Ownership is a set rather than a single column, so an application shared between applicants
    /// answers this for every one of them.
    /// </summary>
    public async Task<bool> IsApplicantOnAsync(
        int applicationId,
        string userId,
        CancellationToken cancellationToken = default) =>
        await db.RentalApplicationApplicants.AnyAsync(
            link => link.RentalApplicationId == applicationId && link.ApplicantUserId == userId,
            cancellationToken);

    /// <summary>
    /// Starts a draft for a unit. An applicant who already has an open application for the unit is
    /// handed that one back instead of collecting duplicates.
    /// </summary>
    public async Task<DomainResult<int>> StartAsync(
        int unitId,
        Actor actor,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var unit = await db.Units
            .Include(entity => entity.Leases)
            .FirstOrDefaultAsync(entity => entity.Id == unitId, cancellationToken);

        if (unit is null)
        {
            return DomainResult<int>.Failure("That unit no longer exists.");
        }

        if (LeaseTerm.HasActiveLease(unit.Leases, asOf))
        {
            return DomainResult<int>.Failure("That unit is currently leased and is not accepting applications.");
        }

        // An array rather than the rule's set, because EF translates Enumerable.Contains to NOT IN.
        var terminalStatuses = ApplicationWorkflow.TerminalStatuses.ToArray();

        var existing = await db.RentalApplications
            .Where(application => application.UnitId == unitId)
            .Where(application => application.Applicants.Any(link => link.ApplicantUserId == actor.UserId))
            .Where(application => !terminalStatuses.Contains(application.Status))
            .Select(application => (int?)application.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            return DomainResult<int>.Success(existing.Value);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        var application = new RentalApplication
        {
            UnitId = unitId,
            Status = ApplicationStatus.Draft,
            CreatedAtUtc = now,
            Applicants =
            [
                new RentalApplicationApplicant
                {
                    ApplicantUserId = actor.UserId,
                    IsPrimary = true,
                    AddedAtUtc = now
                }
            ]
        };

        db.RentalApplications.Add(application);
        await db.SaveChangesAsync(cancellationToken);

        return DomainResult<int>.Success(application.Id);
    }

    public async Task<DomainResult> SaveApplicantInformationAsync(
        ApplicantInformationInput input,
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var application = await db.RentalApplications
            .Include(entity => entity.Applicants)
            .FirstOrDefaultAsync(entity => entity.Id == input.ApplicationId, cancellationToken);

        if (application is null)
        {
            return DomainResult.Failure("That application no longer exists.");
        }

        var permitted = ApplicationWorkflow.CanSave(application, IsApplicantOn(application, userId));

        if (permitted.Failed)
        {
            return permitted;
        }

        application.ApplicantInformation.FirstName = Clean(input.FirstName);
        application.ApplicantInformation.LastName = Clean(input.LastName);
        application.ApplicantInformation.Phone = Clean(input.Phone);
        application.ApplicantInformation.Email = Clean(input.Email);
        application.ApplicantInformation.AddressLine1 = Clean(input.AddressLine1);
        application.ApplicantInformation.AddressLine2 = Clean(input.AddressLine2);
        application.ApplicantInformation.City = Clean(input.City);
        application.ApplicantInformation.State = Clean(input.State);
        application.ApplicantInformation.PostalCode = Clean(input.PostalCode);

        application.ApplicantInformationSavedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        return await SaveSectionAsync(
            application,
            entity => entity.ApplicantInformationVersion,
            input.Version,
            cancellationToken);
    }

    /// <summary>
    /// Marks the residence history section saved. The residences themselves are written by the
    /// modal; this records that the applicant has been through the section.
    /// </summary>
    /// <param name="requireComplete">
    /// True when moving on, which needs the section to be complete. False for a deliberate draft
    /// save, which records progress on a section the applicant has not finished. Submission is
    /// blocked either way while anything is still wrong; this only decides whether the save itself
    /// is refused.
    /// </param>
    public async Task<DomainResult> SaveResidenceHistoryAsync(
        int applicationId,
        Guid version,
        string userId,
        bool requireComplete = true,
        CancellationToken cancellationToken = default)
    {
        var application = await db.RentalApplications
            .Include(entity => entity.Applicants)
            .Include(entity => entity.Residences)
            .FirstOrDefaultAsync(entity => entity.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return DomainResult.Failure("That application no longer exists.");
        }

        var permitted = ApplicationWorkflow.CanSave(application, IsApplicantOn(application, userId));

        if (permitted.Failed)
        {
            return permitted;
        }

        if (requireComplete)
        {
            var complete = ResidenceRules.ValidateHistory([.. application.Residences]);

            if (complete.Failed)
            {
                return complete;
            }
        }

        application.ResidenceHistorySavedAtUtc = timeProvider.GetUtcNow().UtcDateTime;

        return await SaveSectionAsync(
            application,
            entity => entity.ResidenceHistoryVersion,
            version,
            cancellationToken);
    }

    public async Task<DomainResult> SaveResidenceAsync(
        ResidenceInput input,
        string userId,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var application = await db.RentalApplications
            .Include(entity => entity.Applicants)
            .Include(entity => entity.Residences)
            .FirstOrDefaultAsync(entity => entity.Id == input.ApplicationId, cancellationToken);

        if (application is null)
        {
            return DomainResult.Failure("That application no longer exists.");
        }

        var permitted = ApplicationWorkflow.CanSave(application, IsApplicantOn(application, userId));

        if (permitted.Failed)
        {
            return permitted;
        }

        var dates = ResidenceRules.ValidateDates(input.MoveInDate, input.MoveOutDate, asOf);

        if (dates.Failed)
        {
            return dates;
        }

        Residence residence;

        if (input.Id == 0)
        {
            residence = new Residence { RentalApplicationId = application.Id };
            application.Residences.Add(residence);
        }
        else
        {
            // Looked up through the application, so a residence id belonging to someone else
            // cannot be edited by guessing its number.
            var existing = application.Residences.FirstOrDefault(entity => entity.Id == input.Id);

            if (existing is null)
            {
                return DomainResult.Failure("That residence no longer exists.");
            }

            residence = existing;
        }

        residence.AddressLine1 = input.AddressLine1.Trim();
        residence.AddressLine2 = Clean(input.AddressLine2);
        residence.City = input.City.Trim();
        residence.State = input.State.Trim();
        residence.PostalCode = input.PostalCode.Trim();
        residence.LandlordName = input.LandlordName.Trim();
        residence.LandlordPhone = input.LandlordPhone.Trim();
        residence.MoveInDate = input.MoveInDate;
        residence.MoveOutDate = input.MoveOutDate;

        await db.SaveChangesAsync(cancellationToken);
        return DomainResult.Success();
    }

    public async Task<DomainResult> DeleteResidenceAsync(
        int residenceId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        var residence = await db.Residences
            .Include(entity => entity.RentalApplication)
                .ThenInclude(application => application.Applicants)
            .FirstOrDefaultAsync(entity => entity.Id == residenceId, cancellationToken);

        if (residence is null)
        {
            return DomainResult.Failure("That residence no longer exists.");
        }

        var application = residence.RentalApplication;
        var permitted = ApplicationWorkflow.CanSave(application, IsApplicantOn(application, userId));

        if (permitted.Failed)
        {
            return permitted;
        }

        db.Residences.Remove(residence);
        await db.SaveChangesAsync(cancellationToken);

        return DomainResult.Success();
    }

    /// <summary>
    /// Submits, or resubmits a returned application. The unit's availability is checked here and
    /// again at approval, because a lease can be issued to someone else in between.
    /// </summary>
    public async Task<DomainResult> SubmitAsync(
        int applicationId,
        Actor actor,
        DateOnly asOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var application = await db.RentalApplications
            .Include(entity => entity.Applicants)
            .Include(entity => entity.Unit)
                .ThenInclude(unit => unit.Leases)
            .FirstOrDefaultAsync(entity => entity.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return DomainResult.Failure("That application no longer exists.");
        }

        var permitted = ApplicationWorkflow.CanSubmit(application, IsApplicantOn(application, actor.UserId));

        if (permitted.Failed)
        {
            return permitted;
        }

        if (LeaseTerm.HasActiveLease(application.Unit.Leases, asOf))
        {
            return DomainResult.Failure(
                "This unit has been leased and is no longer accepting applications.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var from = application.Status;

        application.Status = ApplicationStatus.Submitted;
        application.SubmittedAtUtc = now;

        application.Events.Add(new ApplicationEvent
        {
            FromStatus = from,
            ToStatus = ApplicationStatus.Submitted,
            ActorUserId = actor.UserId,
            ActorName = actor.Name,
            OccurredAtUtc = now
        });

        await db.SaveChangesAsync(cancellationToken);
        return DomainResult.Success();
    }

    public async Task<DomainResult> WithdrawAsync(
        int applicationId,
        Actor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var application = await db.RentalApplications
            .Include(entity => entity.Applicants)
            .FirstOrDefaultAsync(entity => entity.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return DomainResult.Failure("That application no longer exists.");
        }

        var permitted = ApplicationWorkflow.CanWithdraw(application, IsApplicantOn(application, actor.UserId));

        if (permitted.Failed)
        {
            return permitted;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var from = application.Status;

        application.Status = ApplicationStatus.Withdrawn;
        application.DecidedAtUtc = now;
        application.ClaimedByUserId = null;
        application.ClaimedAtUtc = null;

        application.Events.Add(new ApplicationEvent
        {
            FromStatus = from,
            ToStatus = ApplicationStatus.Withdrawn,
            ActorUserId = actor.UserId,
            ActorName = actor.Name,
            Comment = "Withdrawn by the applicant.",
            OccurredAtUtc = now
        });

        await db.SaveChangesAsync(cancellationToken);
        return DomainResult.Success();
    }

    /// <summary>
    /// The application list. Both filters, the ownership restriction, the count and the page are
    /// all expressed on <see cref="IQueryable{T}"/>, so the database does the work and only the
    /// rows on screen are ever materialised.
    /// </summary>
    public async Task<ApplicationListPage> ListAsync(
        ApplicationListFilter filter,
        string? applicantUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = db.RentalApplications.AsNoTracking();

        // A null applicant id means a property manager, who sees every application.
        if (applicantUserId is not null)
        {
            query = query.Where(application =>
                application.Applicants.Any(link => link.ApplicantUserId == applicantUserId));
        }

        if (filter.Status is not null)
        {
            query = query.Where(application => application.Status == filter.Status);
        }

        if (filter.PropertyId is not null)
        {
            query = query.Where(application => application.Unit.PropertyId == filter.PropertyId);
        }

        var total = await query.CountAsync(cancellationToken);

        var page = Math.Max(filter.Page, 1);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);

        var rows = await OrderBy(query, filter.Sort, filter.Descending)
            // A stable tiebreak, so a row cannot drift between pages when the sort key ties.
            .ThenByDescending(application => application.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(application => new ApplicationListRow(
                application.Id,
                application.Unit.Property.Name,
                application.Unit.UnitNumber,
                ((application.ApplicantInformation.FirstName ?? string.Empty)
                    + " "
                    + (application.ApplicantInformation.LastName ?? string.Empty)).Trim(),
                application.Status,
                application.CreatedAtUtc,
                application.SubmittedAtUtc))
            .ToListAsync(cancellationToken);

        return new ApplicationListPage(rows, total, page, pageSize);
    }

    public async Task<IReadOnlyList<Property>> GetFilterPropertiesAsync(
        CancellationToken cancellationToken = default) =>
        await db.Properties
            .AsNoTracking()
            .OrderBy(property => property.Name)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Applies the chosen ordering to the query rather than to the results, so the database does
    /// the sorting and paging can be trusted to return the right slice.
    /// </summary>
    private static IOrderedQueryable<RentalApplication> OrderBy(
        IQueryable<RentalApplication> query,
        ApplicationSort sort,
        bool descending) => sort switch
    {
        ApplicationSort.Status => descending
            ? query.OrderByDescending(application => application.Status)
            : query.OrderBy(application => application.Status),

        ApplicationSort.Property => descending
            ? query.OrderByDescending(application => application.Unit.Property.Name)
            : query.OrderBy(application => application.Unit.Property.Name),

        ApplicationSort.Unit => descending
            ? query.OrderByDescending(application => application.Unit.UnitNumber)
            : query.OrderBy(application => application.Unit.UnitNumber),

        ApplicationSort.Applicant => descending
            ? query.OrderByDescending(application => application.ApplicantInformation.LastName)
            : query.OrderBy(application => application.ApplicantInformation.LastName),

        // Submitted, and anything unrecognised, falls back to when the application was last
        // moved on, which is the order a queue is worked in.
        _ => descending
            ? query.OrderByDescending(application => application.SubmittedAtUtc ?? application.CreatedAtUtc)
            : query.OrderBy(application => application.SubmittedAtUtc ?? application.CreatedAtUtc)
    };

    private static bool IsApplicantOn(RentalApplication application, string userId) =>
        application.Applicants.Any(link => link.ApplicantUserId == userId);

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Saves a section under its own concurrency token. Telling EF the original value puts it in
    /// the update's WHERE clause, so a save built on a stale copy of that section changes nothing
    /// and is reported rather than silently overwriting the other person's work. The other
    /// section has its own token and is unaffected.
    /// </summary>
    private async Task<DomainResult> SaveSectionAsync(
        RentalApplication application,
        Expression<Func<RentalApplication, Guid>> token,
        Guid expectedVersion,
        CancellationToken cancellationToken)
    {
        var property = db.Entry(application).Property(token);
        property.OriginalValue = expectedVersion;
        property.CurrentValue = Guid.NewGuid();

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return DomainResult.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return DomainResult.Failure(StaleSaveMessage);
        }
    }
}
