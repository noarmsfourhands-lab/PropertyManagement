using PropertyManagement.Domain.Common;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;

namespace PropertyManagement.Domain.Rules;

/// <summary>
/// The application lifecycle: which transitions exist, who may cause them, and when a section
/// is still editable. Controllers call these guards before mutating anything, so a request that
/// forges a status or an action is rejected on the server regardless of what the UI offered.
/// </summary>
public static class ApplicationWorkflow
{
    /// <summary>Statuses that accept no further transition.</summary>
    public static readonly IReadOnlySet<ApplicationStatus> TerminalStatuses =
        new HashSet<ApplicationStatus>
        {
            ApplicationStatus.Approved,
            ApplicationStatus.Denied,
            ApplicationStatus.Withdrawn
        };

    /// <summary>Statuses in which an applicant may still change the application's contents.</summary>
    public static readonly IReadOnlySet<ApplicationStatus> EditableStatuses =
        new HashSet<ApplicationStatus>
        {
            ApplicationStatus.Draft,
            ApplicationStatus.Returned
        };

    /// <summary>Every legal move, as (from, to) pairs. Anything absent is rejected.</summary>
    private static readonly HashSet<(ApplicationStatus From, ApplicationStatus To)> Allowed =
    [
        (ApplicationStatus.Draft, ApplicationStatus.Submitted),
        (ApplicationStatus.Draft, ApplicationStatus.Withdrawn),

        (ApplicationStatus.Submitted, ApplicationStatus.UnderReview),
        (ApplicationStatus.Submitted, ApplicationStatus.Returned),
        (ApplicationStatus.Submitted, ApplicationStatus.Approved),
        (ApplicationStatus.Submitted, ApplicationStatus.Denied),
        (ApplicationStatus.Submitted, ApplicationStatus.Withdrawn),

        (ApplicationStatus.UnderReview, ApplicationStatus.Submitted),
        (ApplicationStatus.UnderReview, ApplicationStatus.Returned),
        (ApplicationStatus.UnderReview, ApplicationStatus.Approved),
        (ApplicationStatus.UnderReview, ApplicationStatus.Denied),
        (ApplicationStatus.UnderReview, ApplicationStatus.Withdrawn),

        (ApplicationStatus.Returned, ApplicationStatus.Submitted),
        (ApplicationStatus.Returned, ApplicationStatus.Withdrawn)
    ];

    public static bool IsTerminal(ApplicationStatus status) => TerminalStatuses.Contains(status);

    public static bool IsAllowed(ApplicationStatus from, ApplicationStatus to) =>
        Allowed.Contains((from, to));

    /// <summary>
    /// Whether the applicant may edit the application's sections. Drives both the read-only
    /// rendering of the section partials and the server-side rejection of disallowed posts.
    /// </summary>
    public static bool ApplicantCanEdit(ApplicationStatus status) => EditableStatuses.Contains(status);

    /// <summary>A property manager never edits an applicant's sections; every section renders read-only.</summary>
    public static bool CanEdit(ApplicationStatus status, bool isApplicantOnApplication) =>
        isApplicantOnApplication && ApplicantCanEdit(status);

    public static DomainResult CanSave(RentalApplication application, bool isApplicantOnApplication)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (!isApplicantOnApplication)
        {
            return DomainResult.Failure("Only an applicant on this application can change it.");
        }

        return ApplicantCanEdit(application.Status)
            ? DomainResult.Success()
            : DomainResult.Failure($"An application in {application.Status} status cannot be edited.");
    }

    /// <summary>
    /// Submit is offered from the Summary only once both sections have been saved. The unit's
    /// availability is checked separately because it needs the unit's leases.
    /// </summary>
    public static DomainResult CanSubmit(RentalApplication application, bool isApplicantOnApplication)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (!isApplicantOnApplication)
        {
            return DomainResult.Failure("Only an applicant on this application can submit it.");
        }

        if (!IsAllowed(application.Status, ApplicationStatus.Submitted))
        {
            return DomainResult.Failure($"An application in {application.Status} status cannot be submitted.");
        }

        if (!application.ApplicantInformationSaved)
        {
            return DomainResult.Failure("Applicant Information must be saved before submitting.");
        }

        return application.ResidenceHistorySaved
            ? DomainResult.Success()
            : DomainResult.Failure("Residence History must be saved before submitting.");
    }

    public static DomainResult CanWithdraw(RentalApplication application, bool isApplicantOnApplication)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (!isApplicantOnApplication)
        {
            return DomainResult.Failure("Only an applicant on this application can withdraw it.");
        }

        return IsAllowed(application.Status, ApplicationStatus.Withdrawn)
            ? DomainResult.Success()
            : DomainResult.Failure($"An application in {application.Status} status cannot be withdrawn.");
    }

    /// <summary>Bonus 2. A manager claims a submitted application before reviewing it.</summary>
    public static DomainResult CanClaim(RentalApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.Status == ApplicationStatus.Submitted
            ? DomainResult.Success()
            : DomainResult.Failure($"Only a Submitted application can be claimed; this one is {application.Status}.");
    }

    /// <summary>Bonus 2. Only the manager holding the application may release it back to the queue.</summary>
    public static DomainResult CanRelease(RentalApplication application, string userId)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (application.Status != ApplicationStatus.UnderReview)
        {
            return DomainResult.Failure("Only an application that is Under Review can be released.");
        }

        return application.ClaimedByUserId == userId
            ? DomainResult.Success()
            : DomainResult.Failure("This application is claimed by another property manager.");
    }

    /// <summary>
    /// A manager may complete a review of a submitted application, or of one they have claimed.
    /// An application claimed by someone else is not reviewable by this manager.
    /// </summary>
    public static DomainResult CanReview(RentalApplication application, string userId)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (application.Status == ApplicationStatus.Submitted)
        {
            return DomainResult.Success();
        }

        if (application.Status != ApplicationStatus.UnderReview)
        {
            return DomainResult.Failure($"An application in {application.Status} status cannot be reviewed.");
        }

        return application.ClaimedByUserId == userId
            ? DomainResult.Success()
            : DomainResult.Failure("This application is claimed by another property manager.");
    }
}
