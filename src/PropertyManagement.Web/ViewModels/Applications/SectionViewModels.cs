using PropertyManagement.Domain.Entities;

namespace PropertyManagement.Web.ViewModels.Applications;

/// <summary>
/// The residence list. Rendered inside the residence history section and returned on its own when
/// the modal refreshes it, so it carries everything it needs rather than reading view data.
/// </summary>
public class ResidenceListViewModel
{
    public required int ApplicationId { get; init; }

    public required IReadOnlyList<Residence> Residences { get; init; }

    public required bool Editable { get; init; }

    public bool IsEmpty => Residences.Count == 0;
}
