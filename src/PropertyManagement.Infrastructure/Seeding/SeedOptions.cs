namespace PropertyManagement.Infrastructure.Seeding;

/// <summary>
/// Controls the demo data written at start-up. Bound from the "Seeding" configuration section so
/// the volume can change per environment without touching the seeder.
/// </summary>
public class SeedOptions
{
    public const string SectionName = "Seeding";

    /// <summary>Turn seeding off entirely, for example in a real deployment.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Fixes the Bogus randomiser so every run of a fresh database produces the same names,
    /// addresses and rents. A stable data set makes the app easier to demonstrate and review.
    /// </summary>
    public int RandomSeed { get; set; } = 20260914;

    public int PropertyManagerCount { get; set; } = 2;

    public int ApplicantCount { get; set; } = 6;

    public int PropertyCount { get; set; } = 3;

    public int MinUnitsPerProperty { get; set; } = 6;

    public int MaxUnitsPerProperty { get; set; } = 10;

    /// <summary>Applications to create for each status, so every status is represented.</summary>
    public int ApplicationsPerStatus { get; set; } = 2;

    /// <summary>Password given to every seeded account. Demo data only.</summary>
    public string DemoPassword { get; set; } = "Passw0rd!";
}
