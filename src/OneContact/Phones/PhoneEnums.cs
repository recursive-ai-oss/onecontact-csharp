namespace RecursiveAI.OneContact;

/// <summary>The kind of line a phone number belongs to, as the numbering plan of its region classifies it.</summary>
public enum PhoneNumberType
{
    /// <summary>The number matches no type in its region's plan.</summary>
    Unknown = 0,

    /// <summary>A fixed line.</summary>
    FixedLine = 1,

    /// <summary>A mobile line.</summary>
    Mobile = 2,

    /// <summary>A fixed line or a mobile line: the region's plan does not tell them apart.</summary>
    FixedLineOrMobile = 3,

    /// <summary>Free to the caller.</summary>
    TollFree = 4,

    /// <summary>Charged above the normal rate.</summary>
    PremiumRate = 5,

    /// <summary>The cost is shared between caller and callee.</summary>
    SharedCost = 6,

    /// <summary>Voice over IP.</summary>
    Voip = 7,

    /// <summary>A personal number that follows its owner between lines.</summary>
    PersonalNumber = 8,

    /// <summary>A pager.</summary>
    Pager = 9,

    /// <summary>A universal access number: one number reaching several offices.</summary>
    Uan = 10,

    /// <summary>A voicemail access number.</summary>
    Voicemail = 11,
}

/// <summary>The ways a <see cref="PhoneNumber"/> can be written out.</summary>
public enum PhoneFormat
{
    /// <summary>E.164: <c>+</c>, the calling code and the national significant number, with no punctuation and no extension.</summary>
    E164 = 0,

    /// <summary>The international form with the region's grouping, such as <c>+1 202-456-1111</c>.</summary>
    International = 1,

    /// <summary>The form people dial inside the region, with the region's national prefix where it is used, such as <c>(202) 456-1111</c>.</summary>
    National = 2,

    /// <summary>An RFC 3966 <c>tel:</c> URI, such as <c>tel:+1-202-456-1111</c>.</summary>
    Rfc3966 = 3,
}

/// <summary>Whether a number has a length that its region's plan allows, and if not, how it misses.</summary>
public enum PhonePossibility
{
    /// <summary>The length is one a full number can have.</summary>
    IsPossible = 0,

    /// <summary>The length is one that is only dialled inside a local area, without the area code.</summary>
    IsPossibleLocalOnly = 1,

    /// <summary>The calling code is not one in the data.</summary>
    InvalidCountryCode = 2,

    /// <summary>The number is shorter than any the plan allows.</summary>
    TooShort = 3,

    /// <summary>The number is longer than any the plan allows.</summary>
    TooLong = 4,

    /// <summary>The length lies between the shortest and longest allowed but is not itself allowed.</summary>
    InvalidLength = 5,
}

/// <summary>How the calling code of a parsed number was found.</summary>
public enum CountryCodeSource
{
    /// <summary>Not recorded: the number was not parsed with its raw input kept, or was built from parts.</summary>
    Unspecified = 0,

    /// <summary>The number started with a plus sign.</summary>
    FromNumberWithPlusSign = 1,

    /// <summary>The number started with the international dialling prefix of the default region.</summary>
    FromNumberWithIdd = 2,

    /// <summary>The number started with the calling code, without a plus sign or dialling prefix.</summary>
    FromNumberWithoutPlusSign = 3,

    /// <summary>The number was national and took the calling code of the default region.</summary>
    FromDefaultCountry = 4,
}
