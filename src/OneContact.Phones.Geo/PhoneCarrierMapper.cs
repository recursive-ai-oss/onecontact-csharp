using RecursiveAI.OneContact.Phones.Geo.Data;

namespace RecursiveAI.OneContact.Phones.Geo;

/// <summary>
/// Names the carrier a mobile number range was first assigned to.
/// </summary>
/// <remarks>
/// <para><b>This is the original carrier.</b> Where mobile numbers can move between carriers (most regions with number portability), the
/// carrier that owns a number today may differ from the one named here. Use <see cref="GetSafeDisplayName"/> when a wrong carrier
/// would mislead: it answers only for regions whose numbers cannot be ported.</para>
/// <para>Languages are BCP 47 tags, with the fallbacks of <see cref="PhoneGeocoder"/>. The data loads lazily, one table per language and
/// calling code. <c>CultureInfo</c> never affects results.</para>
/// </remarks>
public sealed class PhoneCarrierMapper
{
    private static readonly Lazy<PhoneCarrierMapper> DefaultInstance = new(() => new PhoneCarrierMapper(PhoneNumberParser.Default));

    private readonly PhoneNumberParser _parser;

    /// <summary>Creates a mapper that classifies numbers with a parser, which carries the numbering-plan metadata.</summary>
    /// <param name="parser">The parser.</param>
    /// <exception cref="ArgumentNullException"><paramref name="parser"/> is <see langword="null"/>.</exception>
    public PhoneCarrierMapper(PhoneNumberParser parser)
    {
        ArgumentNullException.ThrowIfNull(parser);
        _parser = parser;
    }

    /// <summary>Gets a mapper over the default metadata.</summary>
    public static PhoneCarrierMapper Default => DefaultInstance.Value;

    /// <summary>Names the carrier a number was first assigned to, when the number is a mobile, fixed-or-mobile or pager number.</summary>
    /// <param name="number">The number.</param>
    /// <param name="languageTag">The language, as a BCP 47 tag.</param>
    /// <returns>The carrier's name, or an empty string when the data has none or the number is of another type.</returns>
    public string GetName(PhoneNumber number, string languageTag) =>
        _parser.GetNumberType(number) is PhoneNumberType.Mobile or PhoneNumberType.FixedLineOrMobile or PhoneNumberType.Pager
            ? GetNameForValidNumber(number, languageTag)
            : string.Empty;

    /// <summary>Names the carrier a number was first assigned to, without checking the number's validity or type.</summary>
    /// <param name="number">The number.</param>
    /// <param name="languageTag">The language, as a BCP 47 tag.</param>
    /// <returns>The carrier's name, or an empty string when the data has none.</returns>
    public static string GetNameForValidNumber(PhoneNumber number, string languageTag) =>
        GeoData.Lookup(GeoData.Carriers, "c", LanguageTag.Parse(languageTag), number) ?? string.Empty;

    /// <summary>Names the carrier only where the name can be trusted: regions whose mobile numbers can be ported answer with an empty string.</summary>
    /// <param name="number">The number.</param>
    /// <param name="languageTag">The language, as a BCP 47 tag.</param>
    /// <returns>The carrier's name, or an empty string when the data has none, the number is of another type, or its region allows number portability.</returns>
    public string GetSafeDisplayName(PhoneNumber number, string languageTag) =>
        _parser.IsMobileNumberPortableRegion(_parser.GetRegion(number)) ? string.Empty : GetName(number, languageTag);
}
