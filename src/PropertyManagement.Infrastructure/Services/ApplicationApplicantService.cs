using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PropertyManagement.Application.Services;
using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Infrastructure.Identity;
using PropertyManagement.Infrastructure.Persistence;

namespace PropertyManagement.Infrastructure.Services;

/// <summary>
/// Who is on an application.
///
/// Ownership is a set rather than a single owner column, so every check in the system already asks
/// "is this user among the applicants" rather than "is this user the applicant". Adding a second
/// person therefore grants the same access as the first without any permission code changing.
/// </summary>
public class ApplicationApplicantService(
    PropertyManagementDbContext db,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider) : IApplicationApplicantService
{
    public async Task<IReadOnlyList<ApplicantSummary>> GetApplicantsAsync(
        int applicationId,
        CancellationToken cancellationToken = default)
    {
        var links = await db.RentalApplicationApplicants
            .AsNoTracking()
            .Where(link => link.RentalApplicationId == applicationId)
            .OrderByDescending(link => link.IsPrimary)
            .ThenBy(link => link.AddedAtUtc)
            .ToListAsync(cancellationToken);

        if (links.Count == 0)
        {
            return [];
        }

        // One query for the names rather than one per applicant.
        var ids = links.Select(link => link.ApplicantUserId).ToList();

        var users = await db.Users
            .AsNoTracking()
            .Where(user => ids.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, cancellationToken);

        return links
            .Select(link =>
            {
                users.TryGetValue(link.ApplicantUserId, out var user);

                return new ApplicantSummary(
                    link.ApplicantUserId,
                    user?.DisplayName ?? "Removed account",
                    user?.Email ?? string.Empty,
                    link.IsPrimary,
                    link.AddedAtUtc);
            })
            .ToList();
    }

    public async Task<DomainResult> AddApplicantAsync(
        int applicationId,
        string email,
        string actingUserId,
        CancellationToken cancellationToken = default)
    {
        var application = await db.RentalApplications
            .Include(entity => entity.Applicants)
            .FirstOrDefaultAsync(entity => entity.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return DomainResult.Failure("That application no longer exists.");
        }

        // Only someone already on the application may add to it, and only while it can be edited.
        var permitted = ApplicationWorkflow.CanSave(
            application,
            application.Applicants.Any(link => link.ApplicantUserId == actingUserId));

        if (permitted.Failed)
        {
            return permitted;
        }

        var user = await userManager.FindByEmailAsync(email.Trim());

        // Deliberately the same message whether the address is unknown or belongs to a property
        // manager, so this cannot be used to discover which addresses have accounts.
        if (user is null || !await userManager.IsInRoleAsync(user, UserRole.Applicant))
        {
            return DomainResult.Failure("No applicant account uses that email address.");
        }

        if (application.Applicants.Any(link => link.ApplicantUserId == user.Id))
        {
            return DomainResult.Failure($"{user.DisplayName} is already on this application.");
        }

        application.Applicants.Add(new RentalApplicationApplicant
        {
            ApplicantUserId = user.Id,
            IsPrimary = false,
            AddedAtUtc = timeProvider.GetUtcNow().UtcDateTime
        });

        await db.SaveChangesAsync(cancellationToken);
        return DomainResult.Success();
    }

    public async Task<DomainResult> RemoveApplicantAsync(
        int applicationId,
        string applicantUserId,
        string actingUserId,
        CancellationToken cancellationToken = default)
    {
        var application = await db.RentalApplications
            .Include(entity => entity.Applicants)
            .FirstOrDefaultAsync(entity => entity.Id == applicationId, cancellationToken);

        if (application is null)
        {
            return DomainResult.Failure("That application no longer exists.");
        }

        var permitted = ApplicationWorkflow.CanSave(
            application,
            application.Applicants.Any(link => link.ApplicantUserId == actingUserId));

        if (permitted.Failed)
        {
            return permitted;
        }

        var link = application.Applicants
            .FirstOrDefault(entity => entity.ApplicantUserId == applicantUserId);

        if (link is null)
        {
            return DomainResult.Failure("That applicant is not on this application.");
        }

        // The applicant who started it stays. Removing them would leave an application whose
        // history names someone no longer attached to it, and there is a withdraw action for
        // giving the whole thing up.
        if (link.IsPrimary)
        {
            return DomainResult.Failure(
                "The applicant who started this application cannot be removed. Withdraw it instead.");
        }

        application.Applicants.Remove(link);
        await db.SaveChangesAsync(cancellationToken);

        return DomainResult.Success();
    }
}
