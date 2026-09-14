using PropertyManagement.Domain.Enums;

namespace PropertyManagement.Domain.Entities;

/// <summary>
/// One entry in an application's audit trail: a status change, and the review outcome and
/// comment when the change came from a review. The actor's name is captured at the time of
/// the action so the history still reads correctly if the account is later renamed or removed.
/// </summary>
public class ApplicationEvent
{
    public int Id { get; set; }

    public int RentalApplicationId { get; set; }

    public RentalApplication RentalApplication { get; set; } = null!;

    public ApplicationStatus FromStatus { get; set; }

    public ApplicationStatus ToStatus { get; set; }

    /// <summary>Set only when the transition was the result of a property manager review.</summary>
    public ReviewOutcome? Outcome { get; set; }

    public string? Comment { get; set; }

    public string ActorUserId { get; set; } = string.Empty;

    public string ActorName { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }
}
