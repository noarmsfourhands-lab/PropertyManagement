using System.ComponentModel.DataAnnotations;
using PropertyManagement.Application.Services;

namespace PropertyManagement.Web.ViewModels.Applications;

/// <summary>Backs the add applicant modal.</summary>
public class AddApplicantViewModel
{
    public int ApplicationId { get; set; }

    [Required]
    [EmailAddress]
    [StringLength(256)]
    [Display(Name = "Applicant email")]
    public string Email { get; set; } = string.Empty;
}

/// <summary>Backs the remove applicant confirmation.</summary>
public class RemoveApplicantViewModel
{
    public int ApplicationId { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

/// <summary>The applicants region.</summary>
public class ApplicantListViewModel
{
    public required int ApplicationId { get; init; }

    public required IReadOnlyList<ApplicantSummary> Applicants { get; init; }

    /// <summary>Used to mark which row is the reader, so they can tell themselves apart.</summary>
    public required string CurrentUserId { get; init; }

    /// <summary>
    /// True only for an applicant on an application that can still be edited. A property manager
    /// reading the page sees who is on it but is offered nothing to change.
    /// </summary>
    public required bool CanManage { get; init; }

    public bool IsShared => Applicants.Count > 1;
}
