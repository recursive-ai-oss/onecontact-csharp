namespace RecursiveAI.OneContact;

/// <summary>Facts found about a <see cref="PostalAddress"/>. Flags report; they never change the value.</summary>
[Flags]
public enum AddressFacts : uint
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>The delivery line is a post office box.</summary>
    PostOfficeBox = 1,

    /// <summary>The delivery line is a rural route or highway contract route.</summary>
    Route = 2,

    /// <summary>The delivery line is general delivery.</summary>
    GeneralDelivery = 4,

    /// <summary>The address is a military address (APO, FPO, DPO).</summary>
    Military = 8,

    /// <summary>The address has a secondary unit (apartment, suite, unit).</summary>
    HasUnit = 16,

    /// <summary>The country was inferred from the content, not given as a hint or written in the input.</summary>
    CountryInferred = 32,

    /// <summary>The address is in a United States territory or possession served by USPS (Puerto Rico, Guam, the Virgin Islands and the others).</summary>
    Territory = 64,
}
