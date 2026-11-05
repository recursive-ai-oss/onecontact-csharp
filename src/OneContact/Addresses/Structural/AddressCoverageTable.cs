namespace RecursiveAI.OneContact.Addresses.Structural;

/// <summary>
/// Which countries are in the supported tiers (<see cref="AddressCoverage.Full"/>). Everything else the address data describes is
/// <see cref="AddressCoverage.Structural"/>. Greece, Bulgaria and Cyprus are never in a tier: their scripts are not Latin.
/// </summary>
internal static class AddressCoverageTable
{
    // Tier 1: the United States with the territories its postal service serves, and Canada.
    // Tier 2: Mexico, Australia, the United Kingdom with the Crown dependencies, the Latin-script European Union members,
    // Switzerland, Norway, Iceland, Liechtenstein, Andorra, Monaco, San Marino, the Vatican, and the French overseas departments.
    private static readonly HashSet<string> Tiered = new(StringComparer.Ordinal)
    {
        "US", "PR", "VI", "GU", "AS", "MP", "UM", "CA",
        "MX", "AU", "GB", "JE", "GG", "IM",
        "AT", "BE", "HR", "CZ", "DK", "EE", "FI", "FR", "DE", "HU", "IE", "IT", "LV", "LT", "LU", "MT", "NL", "PL", "PT", "RO", "SK", "SI", "ES", "SE",
        "CH", "NO", "IS", "LI", "AD", "MC", "SM", "VA",
        "GF", "GP", "MQ", "RE", "YT",
    };

    internal static AddressCoverage For(Region region) =>
        !region.IsUnknown && Tiered.Contains(region.Code) ? AddressCoverage.Full : AddressCoverage.Structural;
}
