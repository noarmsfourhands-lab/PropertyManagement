using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Rules;

namespace PropertyManagement.Application.Services;

/// <summary>Someone who may view and edit an application.</summary>
/// <param name="UserId">Their Identity user id.</param>
/// <param name="Name">Their display name.</param>
/// <param name="Email">The address they were added by.</param>
/// <param name="IsPrimary">True for the applicant who started it. Shown, not used for permissions.</param>
/// <param name="AddedAtUtc">When they were added.</param>
public record ApplicantSummary(
    string UserId,
    string Name,
    string Email,
    bool IsPrimary,
    DateTime AddedAtUtc);

public interface IApplicationApplicantService
{
    Task<IReadOnlyList<ApplicantSummary>> GetApplicantsAsync(
        int applicationId,
        CancellationToken cancellationToken = default);

    Task<DomainResult> AddApplicantAsync(
        int applicationId,
        string email,
        string actingUserId,
        CancellationToken cancellationToken = default);

    Task<DomainResult> RemoveApplicantAsync(
        int applicationId,
        string applicantUserId,
        string actingUserId,
        CancellationToken cancellationToken = default);
}
