namespace PropertyManagement.Domain.Entities;

/// <summary>A prior address in the applicant's residence history. Managed through a modal.</summary>
public class Residence
{
    public int Id { get; set; }

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
