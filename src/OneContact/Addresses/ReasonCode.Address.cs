namespace RecursiveAI.OneContact;

public readonly partial struct ReasonCode
{
    // Structural layer codes start at 100 so the codes of the parsing layer can use the numbers below.

    /// <summary>The country is only described structurally; free text addresses are not parsed for it.</summary>
    public static ReasonCode AddressFreeTextRefused { get; } = new(ContactKind.Address, 100, "free-text-refused", "the country is only described structurally, so free text is not parsed for it");

    /// <summary>An address needs a country.</summary>
    public static ReasonCode AddressCountryRequired { get; } = new(ContactKind.Address, 101, "country-required", "an address needs a country");
}
