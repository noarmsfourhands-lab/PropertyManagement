using System.ComponentModel.DataAnnotations;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;
using PropertyManagement.Application.Services;

namespace PropertyManagement.Web.ViewModels.Applications;

/// <summary>Which button on the single form was pressed.</summary>
public enum WizardCommand
{
    /// <summary>Validate the current section, save it only if valid, and move on.</summary>
    Continue = 0,

    /// <summary>Go back a section without saving or validating.</summary>
    Back = 1,

    /// <summary>Submit, from the Summary, once nothing is blocking it.</summary>
    Submit = 2,

    /// <summary>
    /// Save the current section as it stands, even while it is still wrong, and stay on it. The
    /// errors are shown, and submission remains blocked until they are dealt with.
    /// </summary>
    SaveDraft = 3
}

/// <summary>
/// Section one. Its validation rules are declared here and nowhere else, so an error always lands
/// on the field it belongs to.
/// </summary>
public class ApplicantInformationSectionViewModel
{
    [Required]
    [StringLength(100)]
    [Display(Name = "First name")]
    public string? FirstName { get; set; }

    [Required]
    [StringLength(100)]
    [Display(Name = "Last name")]
    public string? LastName { get; set; }

    [Required]
    [Phone]
    [StringLength(30)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [Required]
    [EmailAddress]
    [StringLength(256)]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "Current address")]
    public string? AddressLine1 { get; set; }

    [StringLength(200)]
    [Display(Name = "Address line 2")]
    public string? AddressLine2 { get; set; }

    [Required]
    [StringLength(100)]
    [Display(Name = "City")]
    public string? City { get; set; }

    [Required]
    [StringLength(50)]
    [Display(Name = "State")]
    public string? State { get; set; }

    [Required]
    [StringLength(20)]
    [Display(Name = "ZIP code")]
    public string? PostalCode { get; set; }

    public static ApplicantInformationSectionViewModel From(ApplicantInformation information) => new()
    {
        FirstName = information.FirstName,
        LastName = information.LastName,
        Phone = information.Phone,
        Email = information.Email,
        AddressLine1 = information.AddressLine1,
        AddressLine2 = information.AddressLine2,
        City = information.City,
        State = information.State,
        PostalCode = information.PostalCode
    };

    public ApplicantInformationInput ToInput(int applicationId, Guid version) =>
        new(applicationId, version, FirstName, LastName, Phone, Email,
            AddressLine1, AddressLine2, City, State, PostalCode);
}

/// <summary>
/// Section two. It holds no fields of its own: residences are added, edited and removed through a
/// modal, and the section's validity is the domain rule about how many there are.
/// </summary>
public class ResidenceHistorySectionViewModel
{
    public IReadOnlyList<Residence> Residences { get; set; } = [];

    public bool IsEmpty => Residences.Count == 0;
}

/// <summary>The unit being applied for, shown as context on every section.</summary>
public class UnitSummaryViewModel
{
    public int UnitId { get; init; }

    public string PropertyName { get; init; } = string.Empty;

    public string UnitNumber { get; init; } = string.Empty;

    public string UnitTypeName { get; init; } = string.Empty;

    public int Bedrooms { get; init; }

    public decimal MonthlyRent { get; init; }

    public static UnitSummaryViewModel From(Unit unit) => new()
    {
        UnitId = unit.Id,
        PropertyName = unit.Property?.Name ?? string.Empty,
        UnitNumber = unit.UnitNumber,
        UnitTypeName = unit.UnitType?.Name ?? string.Empty,
        Bedrooms = unit.Bedrooms,
        MonthlyRent = unit.MonthlyRent
    };
}

/// <summary>
/// The one view model that drives the whole application page.
///
/// Only the section currently on screen is bound from the post. Everything else here is context
/// the server rebuilds from the database on every request, so a crafted post cannot change the
/// status, the unit, or whether the page is editable by saying so in a hidden field.
/// </summary>
public class ApplicationWizardViewModel
{
    public int ApplicationId { get; set; }

    /// <summary>The section on screen. Posted back so the action knows what it is validating.</summary>
    public ApplicationSection CurrentSection { get; set; }

    /// <summary>Round-tripped so a save built on a stale copy of a section can be detected.</summary>
    public Guid ApplicantInformationVersion { get; set; }

    public Guid ResidenceHistoryVersion { get; set; }

    public ApplicantInformationSectionViewModel ApplicantInformation { get; set; } = new();

    public ResidenceHistorySectionViewModel ResidenceHistory { get; set; } = new();

    // ---- Rebuilt server-side on every request; never trusted from the post. ----

    public ApplicationStatus Status { get; private set; }

    public UnitSummaryViewModel Unit { get; private set; } = new();

    /// <summary>The server-side decision that makes a section partial editable or read-only.</summary>
    public bool CanEdit { get; private set; }

    public bool ApplicantInformationSaved { get; private set; }

    public bool ResidenceHistorySaved { get; private set; }

    /// <summary>Null when Submit may be offered; otherwise why it may not.</summary>
    public string? SubmitBlockedReason { get; private set; }

    /// <summary>
    /// Everything still wrong with the stored application, whichever section it is in. Populated
    /// on every render, because a draft save can leave a section saved but incomplete.
    /// </summary>
    public IReadOnlyList<SectionProblem> Problems { get; private set; } = [];

    public bool CanSubmit => SubmitBlockedReason is null && Problems.Count == 0;

    public bool CanWithdraw { get; private set; }

    /// <summary>True when a property manager is reading someone else's application.</summary>
    public bool IsManagerView { get; private set; }

    public bool CanReview { get; private set; }

    public bool CanClaim { get; private set; }

    public bool CanRelease { get; private set; }

    /// <summary>
    /// Back is offered only when it has somewhere to go. On an application that cannot be edited
    /// every section but the Summary is unreachable, so the page is forced back to the Summary on
    /// arrival: Back would land where it started and read as a button that does nothing.
    /// </summary>
    public bool ShowBack => CanEdit && ApplicationWizard.Previous(CurrentSection) is not null;

    public bool ShowContinue => CanEdit && CurrentSection != ApplicationSection.Summary;

    /// <summary>Draft saving is offered on the sections that hold data, never on the Summary.</summary>
    public bool ShowSaveDraft => CanEdit && ApplicationWizard.IsDataSection(CurrentSection);

    /// <summary>The blocking problems grouped under the section they belong to.</summary>
    public IEnumerable<IGrouping<ApplicationSection, SectionProblem>> ProblemsBySection =>
        Problems.GroupBy(problem => problem.Section).OrderBy(group => group.Key);

    /// <summary>
    /// Whether the section partials render as inputs or as text. The Summary is a read-only view
    /// of both sections, so it is false there even while the application is still editable. Both
    /// renderings go through the same partial; only this answer differs.
    /// </summary>
    public bool SectionIsEditable => CanEdit && CurrentSection != ApplicationSection.Summary;

    /// <summary>
    /// Whether the step indicator may link to a section. The first section is always reachable;
    /// the others open only once the work before them has been saved, so the indicator cannot be
    /// used to skip past a section the server has not accepted.
    /// </summary>
    public bool IsSectionReachable(ApplicationSection section)
    {
        // Nothing but the Summary is reachable on an application that cannot be edited, so the
        // indicator does not render links that would only bounce back to where they started.
        if (!CanEdit)
        {
            return section == ApplicationSection.Summary;
        }

        return section switch
        {
            ApplicationSection.ApplicantInformation => true,
            ApplicationSection.ResidenceHistory => ApplicantInformationSaved,
            ApplicationSection.Summary => ApplicantInformationSaved && ResidenceHistorySaved,
            _ => false
        };
    }

    /// <summary>
    /// Why a step cannot be opened, or null when it can.
    ///
    /// The indicator looks like a row of tabs, so a step that silently ignores a click reads as
    /// broken rather than as locked. The reason is shown on the step itself.
    /// </summary>
    public string? WhySectionIsLocked(ApplicationSection section)
    {
        if (IsSectionReachable(section))
        {
            return null;
        }

        if (!CanEdit)
        {
            return $"An application in {ApplicationListViewModel.DisplayNameFor(Status)} status cannot be changed.";
        }

        return section switch
        {
            ApplicationSection.ResidenceHistory => "Save applicant information first.",
            ApplicationSection.Summary when !ApplicantInformationSaved =>
                "Save applicant information first.",
            ApplicationSection.Summary => "Save residence history first.",
            _ => "Not available yet."
        };
    }

    /// <summary>The label a section is shown under.</summary>
    public static string LabelFor(ApplicationSection section) => section switch
    {
        ApplicationSection.ApplicantInformation => "Applicant information",
        ApplicationSection.ResidenceHistory => "Residence history",
        ApplicationSection.Summary => "Summary",
        _ => section.ToString()
    };

    /// <summary>The residence list, as its own partial's model.</summary>
    public ResidenceListViewModel ResidenceList() => new()
    {
        ApplicationId = ApplicationId,
        Residences = ResidenceHistory.Residences,
        Editable = SectionIsEditable
    };

    /// <summary>
    /// Fills in everything the server decides, and replaces the section contents with what is
    /// actually stored for any section other than the one being edited.
    /// </summary>
    public void Rehydrate(RentalApplication application, bool isApplicantOn, bool isManager, string userId)
    {
        ArgumentNullException.ThrowIfNull(application);

        ApplicationId = application.Id;
        Status = application.Status;
        Unit = UnitSummaryViewModel.From(application.Unit);

        CanEdit = ApplicationWorkflow.CanEdit(application.Status, isApplicantOn);
        IsManagerView = isManager && !isApplicantOn;

        ApplicantInformationSaved = application.ApplicantInformationSaved;
        ResidenceHistorySaved = application.ResidenceHistorySaved;

        ApplicantInformationVersion = application.ApplicantInformationVersion;
        ResidenceHistoryVersion = application.ResidenceHistoryVersion;

        // The residence list is always what is stored; the modal is the only way to change it.
        ResidenceHistory.Residences = ResidenceRules.InReviewOrder(application.Residences);

        var submit = ApplicationWorkflow.CanSubmit(application, isApplicantOn);
        SubmitBlockedReason = submit.Succeeded ? null : submit.Error;

        // Evaluated against what is stored, not against what was posted, so the Summary reports
        // the application as it actually stands.
        Problems = SectionValidation.ProblemsWith(application);

        CanWithdraw = ApplicationWorkflow.CanWithdraw(application, isApplicantOn).Succeeded;

        CanReview = isManager && ApplicationWorkflow.CanReview(application, userId).Succeeded;
        CanClaim = isManager && ApplicationWorkflow.CanClaim(application).Succeeded;
        CanRelease = isManager && ApplicationWorkflow.CanRelease(application).Succeeded;
    }

    /// <summary>Builds the page fresh from storage, for a GET or after a redirect.</summary>
    public static ApplicationWizardViewModel FromStorage(
        RentalApplication application,
        ApplicationSection section,
        bool isApplicantOn,
        bool isManager,
        string userId)
    {
        ArgumentNullException.ThrowIfNull(application);

        var model = new ApplicationWizardViewModel
        {
            CurrentSection = section,
            ApplicantInformation = ApplicantInformationSectionViewModel.From(application.ApplicantInformation)
        };

        model.Rehydrate(application, isApplicantOn, isManager, userId);
        return model;
    }

    /// <summary>
    /// Which section to open on. An editable application resumes at the first section still
    /// unsaved; anything read-only opens on the Summary, which shows everything at once.
    /// </summary>
    public static ApplicationSection DefaultSectionFor(RentalApplication application, bool isApplicantOn)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (!ApplicationWorkflow.CanEdit(application.Status, isApplicantOn))
        {
            return ApplicationSection.Summary;
        }

        if (!application.ApplicantInformationSaved)
        {
            return ApplicationSection.ApplicantInformation;
        }

        return application.ResidenceHistorySaved
            ? ApplicationSection.Summary
            : ApplicationSection.ResidenceHistory;
    }
}
