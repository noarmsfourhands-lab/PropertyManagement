namespace PropertyManagement.Web;

/// <summary>
/// The names of the authorization policies, so controllers reference a constant rather than
/// repeating a string that a typo would silently turn into "no policy by that name".
/// </summary>
public static class AuthorizationPolicies
{
    public const string PropertyManager = nameof(PropertyManager);

    public const string Applicant = nameof(Applicant);
}
