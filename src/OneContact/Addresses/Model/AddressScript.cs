namespace RecursiveAI.OneContact.Addresses;

/// <summary>Which writing of the address layout and names to use for countries that publish both a local and a Latin form.</summary>
public enum AddressScript
{
    /// <summary>Follow the language of the address: its Latin form when the language tag names the Latin script (<c>ja-Latn</c>), the local form otherwise.</summary>
    Auto = 0,

    /// <summary>The local form of the country.</summary>
    Local = 1,

    /// <summary>The Latin-script form where the country has one; otherwise the local form.</summary>
    Latin = 2,
}
