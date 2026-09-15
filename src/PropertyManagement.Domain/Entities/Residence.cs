namespace PropertyManagement.Domain.Entities;

/// <summary>A prior address in the applicant's residence history. Managed through a modal.</summary>
public class Residence
{
    public int Id { get; set; }

    /// <summary>
    /// Concurrency token for this row.
    ///
    /// The section's token guards the section's own save and deliberately does not move when a row
    /// changes, because the residence modal is opened from the page holding that token and moving
    /// it would reject the applicant's own next Continue. That leaves one gap, which this closes:
    /// two applicants editing the same residence at once. Without it the second save silently wrote
    /// over the first, because each modal posts every field it was opened with.
    /// </summary>
    public Guid Version { get; set; } = Guid.NewGuid();

    public int RentalApplicationId { get; set; }

    public RentalApplication RentalApplication { get; set; } = null!;

    public string AddressLine1 { get; set; } = string.Empty;

    public string? AddressLine2 { get; set; }

    public string City { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string PostalCode { get; set; } = string.Empty;

    public string LandlordName { get; set; } = string.Empty;

    public string LandlordPhone { get; set; } = string.Empty;

    public DateOnly MoveInDate { get; set; }

    /// <summary>Null means the applicant still lives there.</summary>
    public DateOnly? MoveOutDate { get; set; }
}
