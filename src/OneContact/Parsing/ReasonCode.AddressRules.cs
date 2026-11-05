namespace RecursiveAI.OneContact;

public readonly partial struct ReasonCode
{
    /// <summary>The rules cannot tell where the street ends and the city begins (or which words are the street name); a trained model would be needed. No model is available in this build.</summary>
    public static ReasonCode AddressModelRequired { get; } = new(ContactKind.Address, 100, "model-required", "the rules cannot decide this address; a trained model would be needed");

    /// <summary>The content does not say which country the address is in, and no country hint was given.</summary>
    public static ReasonCode AddressCountryUndetermined { get; } = new(ContactKind.Address, 101, "country-undetermined", "the country cannot be told from the content; give a country hint");

    /// <summary>The address is for a country the rules do not cover yet.</summary>
    public static ReasonCode AddressUnsupportedCountry { get; } = new(ContactKind.Address, 102, "unsupported-country", "the rules cover addresses in the United States and Canada");

    /// <summary>The country hint disagrees with what the content says; the content was followed.</summary>
    public static ReasonCode AddressCountryConflict { get; } = new(ContactKind.Address, 103, "country-conflict", "the country hint disagrees with the postal code or state in the address");

    /// <summary>No delivery line could be found.</summary>
    public static ReasonCode AddressNoDeliveryLine { get; } = new(ContactKind.Address, 104, "no-delivery-line", "no street, box or route line was found");

    /// <summary>The address has no last line (city, state or province, postal code), so the country cannot be read from it.</summary>
    public static ReasonCode AddressNoLastLine { get; } = new(ContactKind.Address, 105, "no-last-line", "no city, state or postal code was found");

    /// <summary>Some words of a delivery line could not be placed; they are kept as written.</summary>
    public static ReasonCode AddressUnplacedText { get; } = new(ContactKind.Address, 106, "unplaced-text", "some words could not be placed");
}
