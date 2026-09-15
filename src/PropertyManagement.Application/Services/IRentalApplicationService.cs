using System.Linq.Expressions;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;

namespace PropertyManagement.Application.Services;

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

/// <summary>
/// Enough of an application to decide what one person may do with it: whether it exists, what state
/// it is in, and whether they are on it. Answering that by loading the application itself costs
/// five queries and returns residences, a lease and a property nobody is about to render.
/// </summary>
public record ApplicationAccess(int ApplicationId, ApplicationStatus Status, bool IsApplicantOn);

public interface IRentalApplicationService
{
    Task<RentalApplication?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>What this user may do with this application, in one query.</summary>
    Task<ApplicationAccess?> GetAccessAsync(
        int applicationId,
        string userId,
        CancellationToken cancellationToken = default);

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
