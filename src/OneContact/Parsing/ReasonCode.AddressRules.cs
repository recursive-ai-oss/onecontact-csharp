namespace RecursiveAI.OneContact;

public readonly partial struct ReasonCode
{
    /// <summary>The rules cannot tell where the street ends and the city begins (or which words are the street name); a trained model decides it. Add the NuGet package RecursiveAI.OneContact.Models.Tier1; models are never downloaded.</summary>
    public static ReasonCode AddressModelRequired { get; } = new(ContactKind.Address, 110, "model-required", "the rules cannot decide this address; add the NuGet package RecursiveAI.OneContact.Models.Tier1, whose trained model decides it");

    /// <summary>The content does not say which country the address is in, and no country hint was given.</summary>
    public static ReasonCode AddressCountryUndetermined { get; } = new(ContactKind.Address, 111, "country-undetermined", "the country cannot be told from the content; give a country hint");

    /// <summary>The address is for a country the rules do not cover yet.</summary>
    public static ReasonCode AddressUnsupportedCountry { get; } = new(ContactKind.Address, 112, "unsupported-country", "the rules cover addresses in the United States and Canada");

    /// <summary>The country hint disagrees with what the content says; the content was followed.</summary>
    public static ReasonCode AddressCountryConflict { get; } = new(ContactKind.Address, 113, "country-conflict", "the country hint disagrees with the postal code or state in the address");

    /// <summary>No delivery line could be found.</summary>
    public static ReasonCode AddressNoDeliveryLine { get; } = new(ContactKind.Address, 114, "no-delivery-line", "no street, box or route line was found");

    /// <summary>The address has no last line (city, state or province, postal code), so the country cannot be read from it.</summary>
    public static ReasonCode AddressNoLastLine { get; } = new(ContactKind.Address, 115, "no-last-line", "no city, state or postal code was found");

    /// <summary>Some words of a delivery line could not be placed; they are kept as written.</summary>
    public static ReasonCode AddressUnplacedText { get; } = new(ContactKind.Address, 116, "unplaced-text", "some words could not be placed");
}
