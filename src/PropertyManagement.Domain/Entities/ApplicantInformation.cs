namespace PropertyManagement.Domain.Entities;

/// <summary>
/// Section one of the application. Mapped as an EF owned type, so it lives on the
/// application's own table while staying a distinct object in the model and view models.
/// </summary>
public class ApplicantInformation
{
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? AddressLine1 { get; set; }

    public string? AddressLine2 { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? PostalCode { get; set; }

    public string FullName => string.Join(' ', new[] { FirstName, LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));
}
