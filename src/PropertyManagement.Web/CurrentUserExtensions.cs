using System.Security.Claims;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Infrastructure.Identity;
using PropertyManagement.Infrastructure.Services;

namespace PropertyManagement.Web;

/// <summary>
/// Reads the signed-in user out of the principal. Controllers go through these rather than
/// reaching for claim names directly, so the claim types are written down in one place.
/// </summary>
public static class CurrentUserExtensions
{
    /// <summary>The Identity user id. Throws when called on an unauthenticated request.</summary>
    public static string GetUserId(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("No user id on a request that reached an authorized action.");
    }

    /// <summary>
    /// The name to record on an audit entry. Falls back to the sign-in name for a cookie issued
    /// before the display-name claim existed.
    /// </summary>
    public static string GetDisplayName(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var displayName = user.FindFirstValue(ApplicationUserClaimsPrincipalFactory.DisplayNameClaim);

        return string.IsNullOrWhiteSpace(displayName)
            ? user.Identity?.Name ?? "Unknown user"
            : displayName;
    }

    public static Actor ToActor(this ClaimsPrincipal user) =>
        new(user.GetUserId(), user.GetDisplayName());

    public static bool IsPropertyManager(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.IsInRole(UserRole.PropertyManager);
    }

    public static bool IsApplicant(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return user.IsInRole(UserRole.Applicant);
    }
}
