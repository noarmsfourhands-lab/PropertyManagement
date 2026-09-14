using PropertyManagement.Domain.Entities;
using PropertyManagement.Domain.Enums;
using PropertyManagement.Domain.Rules;

namespace PropertyManagement.Domain.Tests.Rules;

public class ApplicationWizardTests
{
    [Fact]
    public void The_sections_run_applicant_information_then_residence_history_then_summary()
    {
        Assert.Equal(
            [
                ApplicationSection.ApplicantInformation,
                ApplicationSection.ResidenceHistory,
                ApplicationSection.Summary
            ],
            ApplicationWizard.Order);
    }

    [Theory]
    [InlineData(ApplicationSection.ApplicantInformation, ApplicationSection.ResidenceHistory)]
    [InlineData(ApplicationSection.ResidenceHistory, ApplicationSection.Summary)]
    public void Continue_advances_to_the_next_section(ApplicationSection current, ApplicationSection expected)
    {
        Assert.Equal(expected, ApplicationWizard.Next(current));
    }

    [Fact]
    public void Continue_has_nowhere_to_go_from_the_summary()
    {
        Assert.Null(ApplicationWizard.Next(ApplicationSection.Summary));
    }

    [Theory]
    [InlineData(ApplicationSection.Summary, ApplicationSection.ResidenceHistory)]
    [InlineData(ApplicationSection.ResidenceHistory, ApplicationSection.ApplicantInformation)]
    public void Back_returns_to_the_previous_section(ApplicationSection current, ApplicationSection expected)
    {
        Assert.Equal(expected, ApplicationWizard.Previous(current));
    }

    [Fact]
    public void Back_has_nowhere_to_go_from_the_first_section()
    {
        Assert.Null(ApplicationWizard.Previous(ApplicationSection.ApplicantInformation));
    }

    [Fact]
    public void Next_and_previous_are_inverses_across_the_whole_order()
    {
        foreach (var section in ApplicationWizard.Order)
        {
            var next = ApplicationWizard.Next(section);
            if (next is not null)
            {
                Assert.Equal(section, ApplicationWizard.Previous(next.Value));
            }
        }
    }

    [Theory]
    [InlineData(ApplicationSection.ApplicantInformation, true)]
    [InlineData(ApplicationSection.ResidenceHistory, true)]
    [InlineData(ApplicationSection.Summary, false)]
    public void Only_the_first_two_sections_hold_data(ApplicationSection section, bool expected)
    {
        Assert.Equal(expected, ApplicationWizard.IsDataSection(section));
    }

    [Fact]
    public void A_section_counts_as_saved_only_once_it_has_been_persisted()
    {
        var application = new RentalApplication
        {
            ApplicantInformationSavedAtUtc = DateTimeOffset.UtcNow,
            ResidenceHistorySavedAtUtc = null
        };

        Assert.True(ApplicationWizard.IsSaved(application, ApplicationSection.ApplicantInformation));
        Assert.False(ApplicationWizard.IsSaved(application, ApplicationSection.ResidenceHistory));
    }

    [Fact]
    public void The_summary_is_reached_only_once_both_sections_have_been_saved()
    {
        var partial = new RentalApplication { ApplicantInformationSavedAtUtc = DateTimeOffset.UtcNow };
        Assert.False(ApplicationWizard.IsSaved(partial, ApplicationSection.Summary));

        var complete = new RentalApplication
        {
            ApplicantInformationSavedAtUtc = DateTimeOffset.UtcNow,
            ResidenceHistorySavedAtUtc = DateTimeOffset.UtcNow
        };
        Assert.True(ApplicationWizard.IsSaved(complete, ApplicationSection.Summary));
    }

    [Fact]
    public void A_brand_new_draft_has_saved_nothing()
    {
        var application = new RentalApplication();

        foreach (var section in ApplicationWizard.Order)
        {
            Assert.False(ApplicationWizard.IsSaved(application, section));
        }
    }
}
