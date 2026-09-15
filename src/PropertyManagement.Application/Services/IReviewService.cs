using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;

namespace PropertyManagement.Application.Services;

/// <summary>A completed review, as posted from the review modal.</summary>
public record ReviewInput(int ApplicationId, ReviewOutcome Outcome, string? Comment);

public interface IReviewService
{
    Task<IReadOnlyList<ApplicationEvent>> GetHistoryAsync(
        int applicationId,
        CancellationToken cancellationToken = default);

    Task<DomainResult> ClaimAsync(int applicationId, Actor actor, CancellationToken cancellationToken = default);

    Task<DomainResult> ReleaseAsync(int applicationId, Actor actor, CancellationToken cancellationToken = default);

    Task<DomainResult> CompleteAsync(
        ReviewInput input,
        Actor actor,
        DateOnly asOf,
        CancellationToken cancellationToken = default);
}
