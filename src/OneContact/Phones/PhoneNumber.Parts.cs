using System.Globalization;
using RecursiveAI.OneContact.Phones;

namespace RecursiveAI.OneContact;

public readonly partial struct PhoneNumber
{
    /// <summary>Gets the country calling code, without <c>+</c>; 0 for the default value.</summary>
    public int CountryCode => _countryCode;

    /// <summary>Gets the national number as an integer. Leading zeros that belong to the number are not in it: see <see cref="LeadingZeroCount"/>.</summary>
    public ulong NationalNumber => _nationalNumber;

    /// <summary>Gets the number of zeros that start the national significant number and are kept apart from <see cref="NationalNumber"/> (Italian numbers start with one).</summary>
    public int LeadingZeroCount => _leadingZeros;

    /// <summary>Gets a value indicating whether the national significant number starts with a zero that is part of the number.</summary>
    public bool ItalianLeadingZero => _leadingZeros > 0;

    /// <summary>Gets the national significant number: the number without its calling code or national prefix, with any leading zeros the plan keeps. Empty for the default value.</summary>
    public string NationalSignificantNumber =>
        _countryCode == 0 ? string.Empty : string.Concat(new string('0', _leadingZeros), _nationalNumber.ToString(CultureInfo.InvariantCulture));

    /// <summary>Gets the extension digits as written, or <see langword="null"/>.</summary>
    public string? Extension => _extension;

    /// <summary>Gets the text the number was parsed from, when it was parsed from a <see cref="string"/> or with raw input kept; otherwise <see langword="null"/>.</summary>
    public string? RawInput => _rawInput;

    /// <summary>Gets how the calling code was found; <see cref="CountryCodeSource.Unspecified"/> when the raw input was not kept.</summary>
    public CountryCodeSource CountryCodeSource => _codeSource;

    /// <summary>Gets the carrier selection code found in the text (Brazilian and similar plans), when the raw input was kept.</summary>
    public string? PreferredDomesticCarrierCode => _carrierCode;

    /// <summary>Builds a number from its parts without parsing.</summary>
    /// <param name="countryCode">The calling code, 1 to 999.</param>
    /// <param name="nationalSignificantNumber">The digits of the national significant number, at most 17, with the leading zeros the plan keeps.</param>
    /// <param name="extension">The extension digits, or <see langword="null"/>.</param>
    /// <returns>The number. Nothing is checked beyond shape: use <see cref="IsValid"/> to check it against the plan.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The calling code is outside 1 to 999.</exception>
    /// <exception cref="ArgumentException">The national significant number is not 2 to 17 ASCII digits.</exception>
    public static PhoneNumber Create(int countryCode, string nationalSignificantNumber, string? extension = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(countryCode, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(countryCode, 999);
        ArgumentNullException.ThrowIfNull(nationalSignificantNumber);
        if (nationalSignificantNumber.Length is < 2 or > 17 || !nationalSignificantNumber.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("A national significant number is 2 to 17 ASCII digits.", nameof(nationalSignificantNumber));
        }

        int zeros = 0;
        if (nationalSignificantNumber[0] == '0')
        {
            zeros = 1;
            while (zeros < nationalSignificantNumber.Length - 1 && nationalSignificantNumber[zeros] == '0')
            {
                zeros++;
            }
        }

        ulong nn = ulong.Parse(nationalSignificantNumber.AsSpan(zeros), NumberStyles.None, CultureInfo.InvariantCulture);
        PhoneNumber n = new(countryCode, nn, zeros, extension, null, CountryCodeSource.Unspecified, null, default, PhoneFacts.None);
        return new PhoneNumber(countryCode, nn, zeros, extension, null, CountryCodeSource.Unspecified, null, default, PhoneNumberParser.Default.IsValid(n) ? PhoneFacts.Valid : PhoneFacts.None);
    }

    internal PhoneNumber WithoutExtension() =>
        _extension is null ? this : new PhoneNumber(_countryCode, _nationalNumber, _leadingZeros, null, _rawInput, _codeSource, _carrierCode, Source, Flags);

    /// <summary>Gets the type of line (fixed, mobile, toll free, ...) by the numbering plan of the number's region.</summary>
    public PhoneNumberType Type => PhoneNumberParser.Default.GetNumberType(this);

    /// <summary>Gets a value indicating whether the number matches a number pattern of its region's plan.</summary>
    public bool IsValid => PhoneNumberParser.Default.IsValid(this);

    /// <summary>Gets the region the number belongs to, or <see cref="Region.Unknown"/> when it matches none or is a non-geographic number.</summary>
    public Region Region => PhoneNumberParser.Default.GetRegion(this);

    /// <summary>Gets the E.164 form: <c>+</c>, the calling code and the national significant number. The extension is not part of it.</summary>
    public string E164 => PhoneNumberParser.Default.Format(this, PhoneFormat.E164);

    /// <summary>Writes the number in a standard format.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The text.</returns>
    public string Format(PhoneFormat format) => PhoneNumberParser.Default.Format(this, format);

    /// <summary>Tells whether the number is valid in a particular region.</summary>
    /// <param name="region">The region.</param>
    /// <returns><see langword="true"/> when the region's plan has the number.</returns>
    public bool IsValidForRegion(Region region) => PhoneNumberParser.Default.IsValidForRegion(this, region);

    /// <summary>Tells whether the length of the number is plausible, and if not, how it misses.</summary>
    /// <param name="type">The line type to test against, or <see cref="PhoneNumberType.Unknown"/> for any.</param>
    /// <returns>The verdict.</returns>
    public PhonePossibility GetPossibility(PhoneNumberType type = PhoneNumberType.Unknown) => PhoneNumberParser.Default.GetPossibility(this, type);

    /// <summary>Writes the number as it should be dialled from another region.</summary>
    /// <param name="callingFrom">The region the call is placed from.</param>
    /// <returns>The text.</returns>
    public string FormatOutOfCountry(Region callingFrom) => PhoneNumberParser.Default.FormatOutOfCountry(this, callingFrom);

    /// <summary>Writes the number as a mobile phone should be given it to dial.</summary>
    /// <param name="callingFrom">The region the call is placed from.</param>
    /// <param name="withFormatting">Whether to keep punctuation.</param>
    /// <returns>The text.</returns>
    public string FormatForMobileDialing(Region callingFrom, bool withFormatting) =>
        PhoneNumberParser.Default.FormatForMobileDialing(this, callingFrom, withFormatting);

    /// <summary>Writes the number in the format it was typed in, using the raw input kept at parse time.</summary>
    /// <param name="callingFrom">The region the number is shown in.</param>
    /// <returns>The text.</returns>
    public string FormatInOriginalFormat(Region callingFrom) => PhoneNumberParser.Default.FormatInOriginalFormat(this, callingFrom);

    /// <summary>Gets an example number for a region.</summary>
    /// <param name="region">The region.</param>
    /// <param name="type">The line type.</param>
    /// <returns>The example, or <see langword="null"/> when there is none.</returns>
    public static PhoneNumber? GetExample(Region region, PhoneNumberType type = PhoneNumberType.FixedLine) =>
        PhoneNumberParser.Default.GetExample(region, type);
}
