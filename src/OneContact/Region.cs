using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;
using RecursiveAI.OneContact.Serialization;

namespace RecursiveAI.OneContact;

/// <summary>
/// A region code: an assigned ISO 3166-1 alpha-2 code (<c>US</c>, <c>DE</c>), always uppercase, plus <c>AC</c>, <c>TA</c> and <c>XK</c>,
/// which phone and address data use for Ascension Island, Tristan da Cunha and Kosovo. <c>ZZ</c> parses to <see cref="Unknown"/>, which is
/// what <c>default</c> is, so the unspecified region survives a round trip through text and JSON. Codes that are not assigned (<c>UK</c>,
/// <c>XX</c>) are refused; the United Kingdom is <c>GB</c>. Whether a data set has an entry for an assigned region is for that data set to say.
/// Never affected by the current culture.
/// </summary>
[JsonConverter(typeof(RegionJsonConverter))]
public readonly partial struct Region :
    ISpanParsable<Region>,
    ISpanFormattable,
    IEquatable<Region>,
    IEqualityOperators<Region, Region, bool>
{
    // Two uppercase ASCII letters packed high byte first; zero is Unknown.
    private readonly ushort _packed;

    private Region(char first, char second)
    {
        _packed = (ushort)((first << 8) | second);
    }

    /// <summary>Gets the unspecified region, <c>ZZ</c>. It is what <c>default(Region)</c> is.</summary>
    public static Region Unknown => default;

    /// <summary>Gets the United States, <c>US</c>.</summary>
    public static Region US => new('U', 'S');

    /// <summary>Gets Canada, <c>CA</c>.</summary>
    public static Region CA => new('C', 'A');

    private const string KnownCodes =
        "ADAEAFAGAIALAMAOAQARASATAUAWAXAZBABBBDBEBFBGBHBIBJBLBMBNBOBQBRBSBTBVBWBYBZCACCCDCFCGCHCICKCLCMCNCOCRCUCVCWCXCYCZ" +
        "DEDJDKDMDODZECEEEGEHERESETFIFJFKFMFOFRGAGBGDGEGFGGGHGIGLGMGNGPGQGRGSGTGUGWGYHKHMHNHRHTHUIDIEILIMINIOIQIRISITJEJMJOJP" +
        "KEKGKHKIKMKNKPKRKWKYKZLALBLCLILKLRLSLTLULVLYMAMCMDMEMFMGMHMKMLMMMNMOMPMQMRMSMTMUMVMWMXMYMZNANCNENFNGNINLNONPNRNUNZ" +
        "OMPAPEPFPGPHPKPLPMPNPRPSPTPWPYQARERORSRURWSASBSCSDSESGSHSISJSKSLSMSNSOSRSSSTSVSXSYSZTCTDTFTGTHTJTKTLTMTNTOTRTTTVTWTZ" +
        "UAUGUMUSUYUZVAVCVEVGVIVNVUWFWSYEYTZAZMZW" +
        "ACTAXK";

    private static readonly bool[] Known = BuildKnown();

    private static bool[] BuildKnown()
    {
        var table = new bool[26 * 26];
        for (int i = 0; i < KnownCodes.Length; i += 2)
        {
            table[((KnownCodes[i] - 'A') * 26) + (KnownCodes[i + 1] - 'A')] = true;
        }

        return table;
    }

    /// <summary>Gets a value indicating whether this is <see cref="Unknown"/>.</summary>
    public bool IsUnknown => _packed == 0;

    /// <summary>Gets the two-letter uppercase code; <c>ZZ</c> for <see cref="Unknown"/>.</summary>
    public string Code => IsUnknown ? "ZZ" : string.Create(2, _packed, static (span, packed) =>
    {
        span[0] = (char)(packed >> 8);
        span[1] = (char)(packed & 0xFF);
    });

    /// <summary>Parses a region code, in either letter case.</summary>
    /// <param name="s">An assigned region code, or <c>ZZ</c> for <see cref="Unknown"/>.</param>
    /// <returns>The region.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="ContactParseException"><paramref name="s"/> is not an assigned region code.</exception>
    public static Region Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan());
    }

    /// <summary>Parses a region code, in either letter case.</summary>
    /// <param name="s">An assigned region code, or <c>ZZ</c> for <see cref="Unknown"/>.</param>
    /// <returns>The region.</returns>
    /// <exception cref="ContactParseException"><paramref name="s"/> is not an assigned region code.</exception>
    public static Region Parse(ReadOnlySpan<char> s)
    {
        if (!TryParse(s, out Region result))
        {
            throw new ContactParseException(ContactKind.Unknown, ReasonCode.InvalidRegion, new SourceSpan(0, s.Length), s.Equals("UK", StringComparison.OrdinalIgnoreCase) ? "UK is not a region code; the United Kingdom is GB (invalid-region)." : "A region is an assigned two-letter code such as US; '" + s.ToString() + "' is not one (invalid-region).");
        }

        return result;
    }

    /// <summary>Tries to parse a region code, in either letter case.</summary>
    /// <param name="s">An assigned region code, or <c>ZZ</c> for <see cref="Unknown"/>.</param>
    /// <param name="result">The region, or <see cref="Unknown"/> when parsing fails.</param>
    /// <returns><see langword="true"/> when <paramref name="s"/> is an assigned region code or <c>ZZ</c>.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, out Region result) =>
        TryParse(s.AsSpan(), out result);

    /// <summary>Tries to parse a region code, in either letter case.</summary>
    /// <param name="s">An assigned region code, or <c>ZZ</c> for <see cref="Unknown"/>.</param>
    /// <param name="result">The region, or <see cref="Unknown"/> when parsing fails.</param>
    /// <returns><see langword="true"/> when <paramref name="s"/> is an assigned region code or <c>ZZ</c>.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, out Region result)
    {
        if (s.Length == 2 && char.IsAsciiLetter(s[0]) && char.IsAsciiLetter(s[1]))
        {
            // Ordinal ASCII folding: no culture involved.
            char first = char.ToUpperInvariant(s[0]);
            char second = char.ToUpperInvariant(s[1]);
            if (first == 'Z' && second == 'Z')
            {
                result = default;
                return true;
            }

            if (Known[((first - 'A') * 26) + (second - 'A')])
            {
                result = new Region(first, second);
                return true;
            }
        }

        result = default;
        return false;
    }

    static Region IParsable<Region>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<Region>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Region result) =>
        TryParse(s, out result);

    static Region ISpanParsable<Region>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);

    static bool ISpanParsable<Region>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Region result) =>
        TryParse(s, out result);

    /// <summary>Returns <see cref="Code"/>.</summary>
    /// <returns>The two-letter code.</returns>
    public override string ToString() => Code;

    /// <summary>Returns <see cref="Code"/>. Only the default format (<see langword="null"/>, empty or <c>"G"</c>) is defined.</summary>
    /// <param name="format">The format.</param>
    /// <param name="formatProvider">Ignored.</param>
    /// <returns>The two-letter code.</returns>
    /// <exception cref="FormatException"><paramref name="format"/> is not a defined format.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        ValueFormatting.Format(Code, format, "G");

    /// <summary>Tries to write <see cref="Code"/> to a span.</summary>
    /// <param name="destination">Where to write.</param>
    /// <param name="charsWritten">The number of characters written.</param>
    /// <param name="format">The format: empty or <c>"G"</c>.</param>
    /// <param name="provider">Ignored.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too small.</returns>
    /// <exception cref="FormatException"><paramref name="format"/> is not a defined format.</exception>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        ValueFormatting.TryFormat(Code, destination, out charsWritten, format, "G");

    /// <inheritdoc/>
    public bool Equals(Region other) => _packed == other._packed;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Region other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _packed;

    /// <summary>Compares two regions.</summary>
    /// <param name="left">The first region.</param>
    /// <param name="right">The second region.</param>
    /// <returns><see langword="true"/> when they are the same code.</returns>
    public static bool operator ==(Region left, Region right) => left._packed == right._packed;

    /// <summary>Compares two regions.</summary>
    /// <param name="left">The first region.</param>
    /// <param name="right">The second region.</param>
    /// <returns><see langword="true"/> when they are different codes.</returns>
    public static bool operator !=(Region left, Region right) => left._packed != right._packed;
}
