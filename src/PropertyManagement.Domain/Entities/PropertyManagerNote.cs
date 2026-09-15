namespace PropertyManagement.Domain.Entities;

/// <summary>
/// An internal note on an application, kept by and for property managers. Only ever loaded
/// for them; no applicant-facing view model or endpoint projects this type.
/// </summary>
public class PropertyManagerNote
{
    public int Id { get; set; }

    public int RentalApplicationId { get; set; }

    public RentalApplication RentalApplication { get; set; } = null!;

    public string Body { get; set; } = string.Empty;

    public string AuthorUserId { get; set; } = string.Empty;

    public string AuthorName { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? UpdatedAtUtc { get; set; }
}
