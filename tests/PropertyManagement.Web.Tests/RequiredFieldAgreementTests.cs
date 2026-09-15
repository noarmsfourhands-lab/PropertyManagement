using System.ComponentModel.DataAnnotations;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Web.ViewModels.Applications;

namespace PropertyManagement.Web.Tests;

/// <summary>
/// The applicant-information section says which fields are required in two places, on purpose, and
/// this is the test that stops the two drifting apart.
///
/// The domain rule owns whether a value is <em>present</em>, because that is what submission turns
/// on and the web layer cannot be the only guard for it. The view model's data annotations own shape
/// and length, because that is what someone typing into a field needs told, and because model
/// binding is what puts a message next to the right input. Both independently block submission, so a
/// field added to one and forgotten in the other gives two different answers to the same question:
/// the page would say the section is fine and the service would refuse to submit it, or the reverse.
///
/// This test lives in the web suite because it is the only one of the three that can see both sides.
/// The domain suite cannot reference the web project, which is exactly why the drift was possible.
/// </summary>
public class RequiredFieldAgreementTests
{
    /// <summary>
    /// The view model properties carrying <see cref="RequiredAttribute"/>, by the name the domain
    /// knows them under. The two types spell every one of these fields identically, which is what
    /// lets the comparison be by name rather than by a hand-written mapping that could itself drift.
    /// </summary>
    private static IReadOnlyList<string> AnnotatedAsRequired() =>
        [.. typeof(ApplicantInformationSectionViewModel)
            .GetProperties()
            .Where(property => property.GetCustomAttributes(typeof(RequiredAttribute), inherit: true).Length > 0)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)];

    [Fact]
    public void The_domain_rule_and_the_form_require_exactly_the_same_fields()
    {
        var domain = ApplicantInformationRules.RequiredFields
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(domain, AnnotatedAsRequired());
    }

    [Fact]
    public void Address_line_two_is_required_by_neither()
    {
        // Called out separately because it is the one field a reader is likely to "fix" by adding a
        // Required attribute to match the others. It is optional deliberately: plenty of addresses
        // have no second line.
        Assert.DoesNotContain(
            nameof(ApplicantInformationSectionViewModel.AddressLine2),
            ApplicantInformationRules.RequiredFields,
            StringComparer.Ordinal);

        Assert.DoesNotContain(
            nameof(ApplicantInformationSectionViewModel.AddressLine2),
            AnnotatedAsRequired(),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Every_required_field_the_domain_names_exists_on_the_form()
    {
        // Guards the comparison above against a rename on one side: if the domain listed a field the
        // view model no longer has, the first test would still pass as long as the counts happened
        // to match, and the missing field would silently stop being validated on screen.
        var properties = typeof(ApplicantInformationSectionViewModel)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var field in ApplicantInformationRules.RequiredFields)
        {
            Assert.True(properties.Contains(field), $"The form has no property called {field}.");
        }
    }
}
