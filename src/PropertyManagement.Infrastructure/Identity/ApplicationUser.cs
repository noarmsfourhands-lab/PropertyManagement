using Microsoft.AspNetCore.Identity;

namespace PropertyManagement.Infrastructure.Identity;

/// <summary>
/// The application's Identity user. Both roles share one user type; the role assignment, not the
/// class, decides what a signed-in user may do.
/// </summary>
public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Name shown in the UI and copied onto audit entries at the time of an action.
    /// Falls back to the email so a record is never attributed to a blank name.
    /// </summary>
    public string DisplayName
    {
        get
        {
            var name = string.Join(' ', new[] { FirstName, LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));
            return string.IsNullOrWhiteSpace(name) ? Email ?? UserName ?? "Unknown user" : name;
        }
    }
}
