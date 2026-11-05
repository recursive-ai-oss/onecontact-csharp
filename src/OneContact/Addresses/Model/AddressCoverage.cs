namespace RecursiveAI.OneContact.Addresses;

/// <summary>How much the library knows about the addresses of a country.</summary>
public enum AddressCoverage
{
    /// <summary>
    /// Only the structure is known: which fields exist, their order, the required ones and the postal code patterns. Free text
    /// for these countries is not parsed.
    /// </summary>
    Structural = 0,

    /// <summary>The country is in the supported tiers: parsing, standardization and expansion rules exist for it, on top of the structure.</summary>
    Full = 1,
}
