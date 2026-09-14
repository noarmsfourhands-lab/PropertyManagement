namespace PropertyManagement.Domain.Enums;

/// <summary>
/// The two roles in the system. Values are the literal ASP.NET Identity role names.
/// </summary>
public static class UserRole
{
    public const string Applicant = "Applicant";
    public const string PropertyManager = "PropertyManager";

    public static readonly IReadOnlyList<string> All = [Applicant, PropertyManager];
}
