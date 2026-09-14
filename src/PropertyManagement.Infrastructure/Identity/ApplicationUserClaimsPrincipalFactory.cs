using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace PropertyManagement.Infrastructure.Identity;

/// <summary>
/// Adds the user's display name to their sign-in cookie.
///
/// Audit entries record who performed an action by name, and that name is wanted on nearly every
/// write. Putting it in the principal at sign-in means those writes cost no extra query, and the
/// name stays correct for the session even if the account is edited elsewhere.
/// </summary>
public class ApplicationUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    public const string DisplayNameClaim = "display_name";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        identity.AddClaim(new Claim(DisplayNameClaim, user.DisplayName));

        return identity;
    }
}
