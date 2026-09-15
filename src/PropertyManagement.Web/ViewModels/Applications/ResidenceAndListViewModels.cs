using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Infrastructure.Services;
using PropertyManagement.Web.ViewComponents;

namespace PropertyManagement.Web.ViewModels.Applications;

/// <summary>Backs the add and edit residence modal. Id is zero when adding.</summary>
public class ResidenceFormViewModel
{
    public int Id { get; set; }

    public int ApplicationId { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "Address")]
    public string AddressLine1 { get; set; } = string.Empty;

    [StringLength(200)]
    [Display(Name = "Address line 2")]
    public string? AddressLine2 { get; set; }

    [Required]
    [StringLength(100)]
    [Display(Name = "City")]
    public string City { get; set; } = string.Empty;

    [Required]
    [StringLength(50)]
    [Display(Name = "State")]
    public string State { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    [Display(Name = "ZIP code")]
    public string PostalCode { get; set; } = string.Empty;

    [Required]
    [StringLength(200)]
    [Display(Name = "Landlord name")]
    public string LandlordName { get; set; } = string.Empty;

    [Required]
    [Phone]
    [StringLength(30)]
    [Display(Name = "Landlord phone")]
    public string LandlordPhone { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Moved in")]
    public DateOnly? MoveInDate { get; set; }

    /// <summary>Left blank when the applicant has not moved out yet.</summary>
    [DataType(DataType.Date)]
    [Display(Name = "Moved out")]
    public DateOnly? MoveOutDate { get; set; }

    public bool IsNew => Id == 0;

    public string Title => IsNew ? "Add previous residence" : "Edit previous residence";

    public static ResidenceFormViewModel From(Residence residence) => new()
    {
        Id = residence.Id,
        ApplicationId = residence.RentalApplicationId,
        AddressLine1 = residence.AddressLine1,
        AddressLine2 = residence.AddressLine2,
        City = residence.City,
        State = residence.State,
        PostalCode = residence.PostalCode,
        LandlordName = residence.LandlordName,
        LandlordPhone = residence.LandlordPhone,
        MoveInDate = residence.MoveInDate,
        MoveOutDate = residence.MoveOutDate
    };

    public ResidenceInput ToInput() =>
        new(Id, ApplicationId, AddressLine1, AddressLine2, City, State, PostalCode,
            LandlordName, LandlordPhone, MoveInDate!.Value, MoveOutDate);
}

/// <summary>Backs the review modal.</summary>
public class ReviewFormViewModel
{
    public int ApplicationId { get; set; }

    public string PropertyName { get; set; } = string.Empty;

    public string UnitNumber { get; set; } = string.Empty;

    public string ApplicantName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Choose an outcome.")]
    [Display(Name = "Outcome")]
    public ReviewOutcome? Outcome { get; set; }

    [StringLength(2000)]
    [Display(Name = "Comment")]
    public string? Comment { get; set; }

    /// <summary>Drives the hint under the comment box; the server rule is the real guard.</summary>
    public static IReadOnlyList<ReviewOutcome> OutcomesRequiringComment =>
        [.. Domain.Rules.ReviewRules.OutcomesRequiringComment];

    public ReviewInput ToInput() => new(ApplicationId, Outcome!.Value, Comment);
}

/// <summary>The application list, its filters and its page of rows.</summary>
public class ApplicationListViewModel
{
    public ApplicationStatus? Status { get; set; }

    public int? PropertyId { get; set; }

    public int Page { get; set; } = 1;

    public required ApplicationListPage Results { get; init; }

    public IEnumerable<SelectListItem> StatusChoices { get; set; } = [];

    public IEnumerable<SelectListItem> PropertyChoices { get; set; } = [];

    /// <summary>True for a property manager, who sees every application rather than only their own.</summary>
    public bool ShowsEveryApplicant { get; init; }

    public ApplicationListFilter ToFilter() => new(Status, PropertyId, Page);

    /// <summary>
    /// Describes this list to the generic grid component: where to fetch rows, which columns to
    /// draw, and what may be filtered. The grid learns nothing about applications from this beyond
    /// the field names on the rows the endpoint returns.
    /// </summary>
    public DataGridModel ToGrid(string endpoint) => new()
    {
        Id = "applications-grid",
        Endpoint = endpoint,
        PageSize = 20,
        Sort = nameof(ApplicationSort.Submitted),
        Descending = true,
        EmptyMessage = "No applications match these filters.",
        Columns =
        [
            new DataGridColumn("PropertyName", "Property", Sort: nameof(ApplicationSort.Property)),
            new DataGridColumn("UnitNumber", "Unit", Sort: nameof(ApplicationSort.Unit)),
            new DataGridColumn("ApplicantName", "Applicant", Sort: nameof(ApplicationSort.Applicant)),
            new DataGridColumn(
                "StatusLabel",
                "Status",
                Sort: nameof(ApplicationSort.Status),
                Render: DataGridRender.Badge,
                ClassField: "StatusClass"),
            new DataGridColumn("Submitted", "Submitted", Sort: nameof(ApplicationSort.Submitted)),
            new DataGridColumn(
                "DetailUrl",
                string.Empty,
                Render: DataGridRender.Link,
                Align: DataGridAlign.End,
                LinkText: "Open")
        ],
        Filters =
        [
            new DataGridFilter("status", "Status", "Any status", [.. StatusChoices], Status is null ? null : ((int)Status).ToString()),
            new DataGridFilter("propertyId", "Property", "Any property", [.. PropertyChoices], PropertyId?.ToString())
        ]
    };

    public void SetChoices(IReadOnlyList<Property> properties)
    {
        StatusChoices = Enum.GetValues<ApplicationStatus>()
            .Select(status => new SelectListItem
            {
                Value = ((int)status).ToString(),
                Text = DisplayNameFor(status),
                Selected = Status == status
            })
            .ToList();

        PropertyChoices = properties
            .Select(property => new SelectListItem
            {
                Value = property.Id.ToString(),
                Text = property.Name,
                Selected = PropertyId == property.Id
            })
            .ToList();
    }

    /// <summary>"Under review" rather than "UnderReview" wherever a status is shown to a person.</summary>
    public static string DisplayNameFor(ApplicationStatus status) => status switch
    {
        ApplicationStatus.UnderReview => "Under review",
        _ => status.ToString()
    };

    /// <summary>The Bootstrap contextual class each status is rendered with.</summary>
    public static string BadgeClassFor(ApplicationStatus status) => status switch
    {
        ApplicationStatus.Draft => "text-bg-secondary",
        ApplicationStatus.Submitted => "text-bg-primary",
        ApplicationStatus.UnderReview => "text-bg-info",
        ApplicationStatus.Returned => "text-bg-warning",
        ApplicationStatus.Approved => "text-bg-success",
        ApplicationStatus.Denied => "text-bg-danger",
        ApplicationStatus.Withdrawn => "text-bg-dark",
        _ => "text-bg-secondary"
    };
}
