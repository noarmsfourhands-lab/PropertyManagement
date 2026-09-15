using System.Diagnostics.CodeAnalysis;

namespace PropertyManagement.Domain.Entities;

/// <summary>A building maintained by a property manager. Owns a collection of units.</summary>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "Property is the domain's own word for the thing. The rule guards against " +
                    "callers in languages where it is reserved, which is not a concern here.")]
public class Property
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string AddressLine1 { get; set; } = string.Empty;

    public string? AddressLine2 { get; set; }

    public string City { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string PostalCode { get; set; } = string.Empty;

    public ICollection<Unit> Units { get; set; } = [];
}
