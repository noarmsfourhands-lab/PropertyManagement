using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Entities;

namespace PropertyManagement.Domain.Rules;

/// <summary>
/// What the applicant information section must contain before an application can be submitted.
///
/// This exists because draft saving lets a section be stored while it is still incomplete. Once
/// that is allowed, "both sections have been saved" stops being the same question as "the
/// application is fit to submit", and the second question has to be answered somewhere the web
/// layer cannot be the only guard for.
///
/// The division of labour with the view model's data annotations is deliberate. This owns whether
/// a value is <em>present</em>, which is what submission turns on. The annotations own shape and
/// length, which is what a person typing into a field needs told. A test asserts the two agree on
/// which fields are required, so the split cannot quietly drift.
/// </summary>
public static class ApplicantInformationRules
{
    /// <summary>The fields that must carry a value. Address line two is deliberately absent.</summary>
    public static readonly IReadOnlyList<string> RequiredFields =
    [
        nameof(ApplicantInformation.FirstName),
        nameof(ApplicantInformation.LastName),
        nameof(ApplicantInformation.Phone),
        nameof(ApplicantInformation.Email),
        nameof(ApplicantInformation.AddressLine1),
        nameof(ApplicantInformation.City),
        nameof(ApplicantInformation.State),
        nameof(ApplicantInformation.PostalCode)
    ];

    /// <summary>The required fields that are still empty, in the order they appear on the form.</summary>
    public static IReadOnlyList<string> MissingFields(ApplicantInformation information)
    {
        ArgumentNullException.ThrowIfNull(information);

        return
        [
            .. RequiredFields.Where(field => string.IsNullOrWhiteSpace(ValueOf(information, field)))
        ];
    }

    public static bool IsComplete(ApplicantInformation information) =>
        MissingFields(information).Count == 0;

    public static DomainResult Validate(ApplicantInformation information)
    {
        var missing = MissingFields(information);

        return missing.Count == 0
            ? DomainResult.Success()
            : DomainResult.Failure("Applicant Information is not complete.");
    }

    private static string? ValueOf(ApplicantInformation information, string field) => field switch
    {
        nameof(ApplicantInformation.FirstName) => information.FirstName,
        nameof(ApplicantInformation.LastName) => information.LastName,
        nameof(ApplicantInformation.Phone) => information.Phone,
        nameof(ApplicantInformation.Email) => information.Email,
        nameof(ApplicantInformation.AddressLine1) => information.AddressLine1,
        nameof(ApplicantInformation.City) => information.City,
        nameof(ApplicantInformation.State) => information.State,
        nameof(ApplicantInformation.PostalCode) => information.PostalCode,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Not a required field.")
    };
}
