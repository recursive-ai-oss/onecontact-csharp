namespace RecursiveAI.OneContact;

public readonly partial struct ReasonCode
{
    /// <summary>The phone number is national (it does not start with <c>+</c>) and no region was given to read it in.</summary>
    public static ReasonCode PhoneRegionRequired { get; } = new(ContactKind.Phone, 1, "region-required", "a national number needs a region to be read in");

    /// <summary>The text does not have the shape of a phone number.</summary>
    public static ReasonCode PhoneNotANumber { get; } = new(ContactKind.Phone, 2, "not-a-number", "the text does not look like a phone number");

    /// <summary>The calling code at the start of the number is not one that exists, or the region given has no phone data.</summary>
    public static ReasonCode PhoneInvalidCountryCode { get; } = new(ContactKind.Phone, 3, "invalid-country-code", "the country calling code is not recognised");

    /// <summary>An international dialling prefix was found but too little followed it.</summary>
    public static ReasonCode PhoneTooShortAfterIdd { get; } = new(ContactKind.Phone, 4, "too-short-after-idd", "too little follows the international dialling prefix to be a phone number");

    /// <summary>The national number has fewer digits than any phone number can.</summary>
    public static ReasonCode PhoneTooShort { get; } = new(ContactKind.Phone, 5, "too-short", "the number is too short to be a phone number");

    /// <summary>The text, or the national number in it, is longer than any phone number can be.</summary>
    public static ReasonCode PhoneTooLong { get; } = new(ContactKind.Phone, 6, "too-long", "the number is too long to be a phone number");

    /// <summary>The default region is a well-formed code but the phone data has no entry for it.</summary>
    public static ReasonCode PhoneUnknownRegion { get; } = new(ContactKind.Phone, 7, "unknown-region", "the region has no phone data");
}
