namespace PropertyManagement.Domain.Entities;

/// <summary>
/// Lookup for the kind of unit (Studio, Loft, Townhouse...).
/// An inactive type stays visible on units already using it but cannot be picked for any other unit.
/// </summary>
public class UnitType
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public ICollection<Unit> Units { get; set; } = [];
}
