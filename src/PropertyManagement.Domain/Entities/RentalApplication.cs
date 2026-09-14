using PropertyManagement.Domain.Enums;

namespace PropertyManagement.Domain.Entities;

/// <summary>
/// An application from one or more applicants for a single unit.
/// </summary>
public class RentalApplication
{
    public int Id { get; set; }

    public int UnitId { get; set; }

    public Unit Unit { get; set; } = null!;

    public ApplicationStatus Status { get; set; } = ApplicationStatus.Draft;

    /// <summary>Section one. Always present; individual fields stay null until first saved.</summary>
    public ApplicantInformation ApplicantInformation { get; set; } = new();

    /// <summary>Section two.</summary>
    public ICollection<Residence> Residences { get; set; } = [];

    public ICollection<RentalApplicationApplicant> Applicants { get; set; } = [];

    public ICollection<ApplicationEvent> Events { get; set; } = [];

    public ICollection<PropertyManagerNote> Notes { get; set; } = [];

    /// <summary>The lease issued on approval. Null until approved.</summary>
    public Lease? Lease { get; set; }

    // Section completion. Submit is only offered once both sections have been saved,
    // so each one records when it was last persisted rather than inferring it from field values.

    public DateTime? ApplicantInformationSavedAtUtc { get; set; }

    public DateTime? ResidenceHistorySavedAtUtc { get; set; }

    /// <summary>
    /// Bonus 5a. Per-section concurrency tokens. Two applicants saving different sections do not
    /// collide, while a second save of the same stale section is rejected. SQL Server allows one
    /// rowversion per table, so these are application-managed GUIDs marked as concurrency tokens.
    /// </summary>
    public Guid ApplicantInformationVersion { get; set; } = Guid.NewGuid();

    public Guid ResidenceHistoryVersion { get; set; } = Guid.NewGuid();

    // Timestamps

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }

    public DateTime? DecidedAtUtc { get; set; }

    /// <summary>Bonus 2. The property manager currently holding the application, when Under Review.</summary>
    public string? ClaimedByUserId { get; set; }

    public DateTime? ClaimedAtUtc { get; set; }

    public bool ApplicantInformationSaved => ApplicantInformationSavedAtUtc is not null;

    public bool ResidenceHistorySaved => ResidenceHistorySavedAtUtc is not null;
}
