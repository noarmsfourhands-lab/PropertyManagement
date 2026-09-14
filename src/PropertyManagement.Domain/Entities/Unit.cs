namespace PropertyManagement.Domain.Entities;

/// <summary>An individual apartment within a property. Applicants apply for a unit.</summary>
public class Unit
{
    public int Id { get; set; }

    public int PropertyId { get; set; }

    public Property Property { get; set; } = null!;

    public string UnitNumber { get; set; } = string.Empty;

    public int Bedrooms { get; set; }

    public decimal MonthlyRent { get; set; }

    public int UnitTypeId { get; set; }

    public UnitType UnitType { get; set; } = null!;

    public ICollection<Lease> Leases { get; set; } = [];

    public ICollection<RentalApplication> Applications { get; set; } = [];
}
