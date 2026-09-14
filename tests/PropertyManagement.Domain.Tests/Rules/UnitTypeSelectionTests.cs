using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Rules;

namespace PropertyManagement.Domain.Tests.Rules;

public class UnitTypeSelectionTests
{
    private static readonly UnitType Studio = new() { Id = 1, Name = "Studio", IsActive = true };
    private static readonly UnitType Loft = new() { Id = 2, Name = "Loft", IsActive = true };
    private static readonly UnitType Retired = new() { Id = 3, Name = "Garden Flat", IsActive = false };

    [Fact]
    public void An_active_type_can_be_assigned_to_a_new_unit()
    {
        Assert.True(UnitTypeSelection.CanAssign(Studio, currentUnitTypeId: null));
    }

    [Fact]
    public void An_inactive_type_cannot_be_assigned_to_a_new_unit()
    {
        Assert.False(UnitTypeSelection.CanAssign(Retired, currentUnitTypeId: null));
    }

    [Fact]
    public void An_inactive_type_cannot_be_moved_onto_a_different_unit()
    {
        Assert.False(UnitTypeSelection.CanAssign(Retired, currentUnitTypeId: Studio.Id));
    }

    [Fact]
    public void A_unit_already_using_an_inactive_type_may_keep_it()
    {
        Assert.True(UnitTypeSelection.CanAssign(Retired, currentUnitTypeId: Retired.Id));
    }

    [Fact]
    public void The_rejection_message_names_the_type()
    {
        var result = UnitTypeSelection.Validate(Retired, currentUnitTypeId: null);

        Assert.True(result.Failed);
        Assert.Equal("Unit type 'Garden Flat' is inactive and cannot be selected.", result.Error);
    }

    [Fact]
    public void Validate_succeeds_where_assignment_is_allowed()
    {
        Assert.True(UnitTypeSelection.Validate(Studio, null).Succeeded);
        Assert.True(UnitTypeSelection.Validate(Retired, Retired.Id).Succeeded);
    }

    [Fact]
    public void A_new_unit_is_offered_only_the_active_types()
    {
        var selectable = UnitTypeSelection.SelectableFor([Studio, Loft, Retired], currentUnitTypeId: null);

        Assert.Equal(["Loft", "Studio"], selectable.Select(type => type.Name));
    }

    [Fact]
    public void An_existing_unit_is_offered_the_active_types_plus_its_own_retired_one()
    {
        var selectable = UnitTypeSelection.SelectableFor([Studio, Loft, Retired], currentUnitTypeId: Retired.Id);

        Assert.Equal(["Garden Flat", "Loft", "Studio"], selectable.Select(type => type.Name));
    }

    [Fact]
    public void The_offered_list_is_sorted_by_name()
    {
        var selectable = UnitTypeSelection.SelectableFor([Loft, Studio], currentUnitTypeId: null);

        Assert.Equal(["Loft", "Studio"], selectable.Select(type => type.Name));
    }
}
