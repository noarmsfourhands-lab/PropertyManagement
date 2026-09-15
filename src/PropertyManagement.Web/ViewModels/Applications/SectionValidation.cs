using System.ComponentModel.DataAnnotations;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;

namespace PropertyManagement.Web.ViewModels.Applications;

/// <summary>One thing still standing between the application and being submitted.</summary>
/// <param name="Section">Which section the problem is in.</param>
/// <param name="Field">The field it belongs to, or null when it is about the section as a whole.</param>
/// <param name="Message">What is wrong, in the words the field itself would use.</param>
public record SectionProblem(ApplicationSection Section, string? Field, string Message)
{
    /// <summary>The model-state key this problem is reported under, so it lands on its own field.</summary>
    public string ModelStateKey => Field is null
        ? string.Empty
        : Section == ApplicationSection.ApplicantInformation
            ? $"{nameof(ApplicationWizardViewModel.ApplicantInformation)}.{Field}"
            : Field;
}

/// <summary>
/// Answers what is wrong with a section, wherever that question is asked.
///
/// A section can be saved while it is still wrong, the Summary lists everything blocking
/// submission, and the rules are defined once per section with each error returned to the field it
/// belongs to. That last part is why this evaluates the section's own
/// data annotations with <see cref="Validator"/> rather than restating them: the attributes on the
/// section view model are the single definition, used both by model binding when the section is on
/// screen and by this when the Summary asks about a section the reader cannot currently see.
/// </summary>
public static class SectionValidation
{
    /// <summary>Everything wrong with the applicant information section, by field.</summary>
    public static IReadOnlyList<SectionProblem> ProblemsWith(ApplicantInformationSectionViewModel section)
    {
        ArgumentNullException.ThrowIfNull(section);

        var results = new List<ValidationResult>();

        Validator.TryValidateObject(
            section,
            new ValidationContext(section),
            results,
            validateAllProperties: true);

        return results
            .SelectMany(
                result => result.MemberNames.DefaultIfEmpty(null),
                (result, member) => new SectionProblem(
                    ApplicationSection.ApplicantInformation,
                    member,
                    result.ErrorMessage ?? "This field is not valid."))
            .ToList();
    }

    /// <summary>
    /// Everything wrong with the residence history section. Its rule lives in the domain rather
    /// than in an attribute, because it is about the collection rather than a field.
    /// </summary>
    public static IReadOnlyList<SectionProblem> ProblemsWith(IReadOnlyCollection<Residence> residences)
    {
        var complete = ResidenceRules.ValidateHistory(residences);

        return complete.Succeeded
            ? []
            : [new SectionProblem(ApplicationSection.ResidenceHistory, null, complete.Error!)];
    }

    /// <summary>Everything still blocking submission, across every section.</summary>
    public static IReadOnlyList<SectionProblem> ProblemsWith(RentalApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return
        [
            .. ProblemsWith(ApplicantInformationSectionViewModel.From(application.ApplicantInformation)),
            .. ProblemsWith([.. application.Residences])
        ];
    }

    /// <summary>The heading a group of problems is listed under.</summary>
    public static string SectionLabel(ApplicationSection section) =>
        ApplicationWizardViewModel.LabelFor(section);
}
