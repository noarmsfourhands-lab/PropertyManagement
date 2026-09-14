using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;

namespace PropertyManagement.Domain.Rules;

/// <summary>
/// Section ordering for the single-page application. One form posts to one action and the
/// button clicked decides what happens; this type answers where that button leads.
/// </summary>
public static class ApplicationWizard
{
    private static readonly ApplicationSection[] SectionOrder =
    [
        ApplicationSection.ApplicantInformation,
        ApplicationSection.ResidenceHistory,
        ApplicationSection.Summary
    ];

    /// <summary>The sections in display order.</summary>
    public static IReadOnlyList<ApplicationSection> Order => SectionOrder;

    /// <summary>Sections that hold data and are validated and saved. The Summary is read-only.</summary>
    public static readonly IReadOnlyList<ApplicationSection> DataSections =
    [
        ApplicationSection.ApplicantInformation,
        ApplicationSection.ResidenceHistory
    ];

    public static bool IsDataSection(ApplicationSection section) => DataSections.Contains(section);

    /// <summary>The section Continue advances to, or null from the last section.</summary>
    public static ApplicationSection? Next(ApplicationSection current)
    {
        var index = Array.IndexOf(SectionOrder, current);
        return index >= 0 && index < SectionOrder.Length - 1 ? SectionOrder[index + 1] : null;
    }

    /// <summary>The section Back returns to, or null from the first section.</summary>
    public static ApplicationSection? Previous(ApplicationSection current)
    {
        var index = Array.IndexOf(SectionOrder, current);
        return index > 0 ? SectionOrder[index - 1] : null;
    }

    /// <summary>Whether a section has been persisted. The Summary counts as reached once both have.</summary>
    public static bool IsSaved(RentalApplication application, ApplicationSection section)
    {
        ArgumentNullException.ThrowIfNull(application);

        return section switch
        {
            ApplicationSection.ApplicantInformation => application.ApplicantInformationSaved,
            ApplicationSection.ResidenceHistory => application.ResidenceHistorySaved,
            ApplicationSection.Summary => application.ApplicantInformationSaved && application.ResidenceHistorySaved,
            _ => false
        };
    }
}
