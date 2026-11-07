using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;
using RecursiveAI.OneContact.Keys;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Serialization;

namespace RecursiveAI.OneContact;

/// <summary>
/// A postal address. The canonical form is the address in the style of its country's postal authority; the key is the aggressive, versioned form used to compare values.
/// </summary>
/// <remarks>
/// <para>A value type: <c>default(PostalAddress)</c> is the empty value, whose <see cref="Canonical"/> and <see cref="Key"/> are empty strings.</para>
/// <para><c>==</c> and <see cref="Equals(PostalAddress)"/> compare canonical forms ordinally. Keys are compared only explicitly, through
/// <see cref="Key"/>, <see cref="GetKey"/> or <see cref="Contacts.SameKey{T}"/>.</para>
/// <para>The <see cref="IFormatProvider"/> taken by the <see cref="IParsable{TSelf}"/> and <see cref="IFormattable"/> members is ignored:
/// results never depend on the current culture.</para>
/// </remarks>
[JsonConverter(typeof(PostalAddressJsonConverter))]
public readonly partial struct PostalAddress :
    IContactValue,
    ISpanParsable<PostalAddress>,
    ISpanFormattable,
    IEquatable<PostalAddress>,
    IEqualityOperators<PostalAddress, PostalAddress, bool>
{
    private readonly string? _canonical;
    private readonly AddressParts? _parts;
    private readonly ParsedAddress? _parsed;

    // The last key computed for this value. Computing a key reads the canonical form back through the parser, which costs about as much as the
    // first parse, and callers ask for the key of one value many times (grouping, de-duplication). The memo is shared by copies of the value;
    // a racing writer only repeats the work, because an entry is replaced whole.
    private readonly KeyMemo? _keyMemo;

    private sealed class KeyMemo
    {
        internal KeyEntry? Last;
    }

    private sealed record KeyEntry(KeyVersion Version, string Key);

    // Used by the rule parser: the canonical form is the postal-style line, the parts are what the rules read, and the parsed address keeps the street parts and offsets.
    internal PostalAddress(string canonical, AddressParts parts, ParsedAddress? parsed, SourceSpan source, AddressFacts flags)
    {
        _canonical = canonical;
        _parts = parts;
        _parsed = parsed;
        _keyMemo = new KeyMemo();
        Source = source;
        Flags = flags;
    }

    /// <summary>Gets the canonical form: faithful to the standard or the authority's style, never changing where mail goes or what a link points to. Empty for the default value.</summary>
    public string Canonical => _canonical ?? string.Empty;

    /// <summary>Gets where the value was found in the text it was parsed from, in UTF-16 code units. Empty for the default value.</summary>
    public SourceSpan Source { get; }

    /// <summary>Gets the facts found about the value.</summary>
    public AddressFacts Flags { get; }

    /// <summary>Gets the comparison key for the latest <see cref="KeyVersion"/> (<see cref="KeyVersions.Latest"/>).</summary>
    public string Key => GetKey(KeyVersions.Latest);

    /// <summary>Computes the comparison key for a named key version. A published version's output never changes.</summary>
    /// <param name="version">The key version.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is not a defined <see cref="KeyVersion"/>.</exception>
    public string GetKey(KeyVersion version)
    {
        if (_keyMemo is not { } memo)
        {
            return AddressKey.Compute(this, version);
        }

        KeyEntry? last = memo.Last;
        if (last is not null && last.Version == version)
        {
            return last.Key;
        }

        string key = AddressKey.Compute(this, version);
        memo.Last = new KeyEntry(version, key);
        return key;
    }

    ContactKind IContactValue.Kind => ContactKind.Address;

    /// <summary>
    /// Parses free text as an address, telling the country from its content (postcode forms, region names, a country line).
    /// </summary>
    /// <param name="s">The text to parse. Surrounding white space is ignored.</param>
    /// <returns>The parsed value, whose <see cref="Country"/> is never unknown.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="ContactParseException">The text is not an address, or its country cannot be told from the content
    /// (<see cref="ReasonCode.AddressCountryUndetermined"/>): use <see cref="Parse(string, Region)"/> with the country. The partial reading is
    /// available from the status-returning <see cref="TryParse(ReadOnlySpan{char}, out PostalAddress, out ParseDetail)"/>.</exception>
    public static PostalAddress Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan());
    }

    /// <summary>
    /// Parses free text as an address, telling the country from its content (postcode forms, region names, a country line).
    /// </summary>
    /// <param name="s">The text to parse. Surrounding white space is ignored.</param>
    /// <returns>The parsed value, whose <see cref="Country"/> is never unknown.</returns>
    /// <exception cref="ContactParseException">The text is not an address, or its country cannot be told from the content
    /// (<see cref="ReasonCode.AddressCountryUndetermined"/>): use <see cref="Parse(ReadOnlySpan{char}, Region)"/> with the country.</exception>
    public static PostalAddress Parse(ReadOnlySpan<char> s) => Parse(s, Region.Unknown);

    /// <summary>Parses free text as an address in a country.</summary>
    /// <param name="s">The text to parse. Surrounding white space is ignored.</param>
    /// <param name="country">The country the text is in; <see cref="Region.Unknown"/> tells it from the content, as <see cref="Parse(string)"/> does.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="ContactParseException">The text is not an address, or the country is only described structurally
    /// (<see cref="ReasonCode.AddressFreeTextRefused"/>).</exception>
    public static PostalAddress Parse(string s, Region country)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan(), country);
    }

    /// <summary>Parses free text as an address in a country.</summary>
    /// <param name="s">The text to parse. Surrounding white space is ignored.</param>
    /// <param name="country">The country the text is in; <see cref="Region.Unknown"/> tells it from the content, as <see cref="Parse(string)"/> does.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="ContactParseException">The text is not an address, or the country is only described structurally
    /// (<see cref="ReasonCode.AddressFreeTextRefused"/>).</exception>
    public static PostalAddress Parse(ReadOnlySpan<char> s, Region country)
    {
        OperationStatus status = TryParseStrict(s, country, out PostalAddress value, out ParseDetail detail);
        if (status != OperationStatus.Done)
        {
            throw ContactParseException.FromDetail(ContactKind.Address, detail, s.Length);
        }

        return value;
    }

    /// <summary>Tries to parse free text as an address, telling the country from its content.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="result">The parsed value, or the default value when parsing fails, including when the country cannot be told.</param>
    /// <returns><see langword="true"/> when the text was parsed and its country is known.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, out PostalAddress result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        return TryParse(s.AsSpan(), out result);
    }

    /// <summary>Tries to parse free text as an address, telling the country from its content.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="result">The parsed value, or the default value when parsing fails, including when the country cannot be told.</param>
    /// <returns><see langword="true"/> when the text was parsed and its country is known.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, out PostalAddress result) =>
        TryParseStrict(s, Region.Unknown, out result, out _) == OperationStatus.Done;

    /// <summary>Tries to parse free text as an address in a country.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="country">The country the text is in; <see cref="Region.Unknown"/> tells it from the content.</param>
    /// <param name="result">The parsed value, or the default value when parsing fails.</param>
    /// <returns><see langword="true"/> when the text was parsed.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, Region country, out PostalAddress result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        return TryParseStrict(s, country, out result, out _) == OperationStatus.Done;
    }

    /// <summary>Tries to parse free text as an address in a country.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="country">The country the text is in; <see cref="Region.Unknown"/> tells it from the content.</param>
    /// <param name="result">The parsed value, or the default value when parsing fails.</param>
    /// <returns><see langword="true"/> when the text was parsed.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, Region country, out PostalAddress result) =>
        TryParseStrict(s, country, out result, out _) == OperationStatus.Done;

    /// <summary>
    /// The low-level parse: returns a status and a <see cref="ParseDetail"/> instead of throwing, and keeps a partial reading. When the country cannot be told from the
    /// content the fields are returned with <see cref="OperationStatus.Done"/> and <see cref="AddressFacts.CountryUndetermined"/> set, and <see cref="Country"/> unknown;
    /// the happy-path <c>Parse</c> and <c>TryParse</c> overloads treat that case as a failure instead.
    /// </summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="value">The parsed value, or the default value when the status is not <see cref="OperationStatus.Done"/>.</param>
    /// <param name="detail">Issues with reason codes, per-field offsets and confidence, and whether a rule or a model decided each field.</param>
    /// <returns><see cref="OperationStatus.Done"/>, or <see cref="OperationStatus.InvalidData"/> with the reason in <paramref name="detail"/>.</returns>
    public static OperationStatus TryParse(ReadOnlySpan<char> s, out PostalAddress value, out ParseDetail detail) =>
        PostalAddressParser.TryParse(s, Region.Unknown, out value, out detail);

    // The happy-path rule: an address is returned only when its country is known (given, or told from the content). The partial reading with an undetermined
    // country stays available from the status-returning overload above.
    internal static OperationStatus TryParseStrict(ReadOnlySpan<char> s, Region country, out PostalAddress value, out ParseDetail detail)
    {
        OperationStatus status = country.IsUnknown
            ? PostalAddressParser.TryParse(s, Region.Unknown, out value, out detail)
            : TryParse(s, country, out value, out detail);
        if (status == OperationStatus.Done && value.Flags.HasFlag(AddressFacts.CountryUndetermined))
        {
            value = default;
            detail = ParseDetail.Failure(ReasonCode.AddressCountryUndetermined, new SourceSpan(0, s.Length));
            return OperationStatus.InvalidData;
        }

        return status;
    }

    static PostalAddress IParsable<PostalAddress>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<PostalAddress>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PostalAddress result) =>
        TryParse(s, out result);

    static PostalAddress ISpanParsable<PostalAddress>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);

    static bool ISpanParsable<PostalAddress>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out PostalAddress result) =>
        TryParse(s, out result);

    /// <summary>Returns the canonical form.</summary>
    /// <returns><see cref="Canonical"/>.</returns>
    public override string ToString() => Canonical;

    /// <summary>Formats the value. Only the canonical form (<c>null</c>, empty or <c>"C"</c>) is defined for now.</summary>
    /// <param name="format">The format: <see langword="null"/>, empty or <c>"C"</c>.</param>
    /// <param name="formatProvider">Ignored; results never depend on culture.</param>
    /// <returns>The formatted text.</returns>
    /// <exception cref="FormatException"><paramref name="format"/> is not a defined format.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        ValueFormatting.Format(Canonical, format);

    /// <summary>Tries to write the formatted value to a span.</summary>
    /// <param name="destination">Where to write.</param>
    /// <param name="charsWritten">The number of characters written.</param>
    /// <param name="format">The format: empty or <c>"C"</c>.</param>
    /// <param name="provider">Ignored; results never depend on culture.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too small.</returns>
    /// <exception cref="FormatException"><paramref name="format"/> is not a defined format.</exception>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider) =>
        ValueFormatting.TryFormat(Canonical, destination, out charsWritten, format);

    /// <summary>Compares canonical forms ordinally.</summary>
    /// <param name="other">The value to compare with.</param>
    /// <returns><see langword="true"/> when the canonical forms are equal.</returns>
    public bool Equals(PostalAddress other) => string.Equals(Canonical, other.Canonical, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PostalAddress other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => string.GetHashCode(Canonical, StringComparison.Ordinal);

    /// <summary>Compares canonical forms ordinally.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the canonical forms are equal.</returns>
    public static bool operator ==(PostalAddress left, PostalAddress right) => left.Equals(right);

    /// <summary>Compares canonical forms ordinally.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the canonical forms differ.</returns>
    public static bool operator !=(PostalAddress left, PostalAddress right) => !left.Equals(right);
}
