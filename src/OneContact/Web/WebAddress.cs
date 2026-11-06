using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;
using RecursiveAI.OneContact.Keys;
using RecursiveAI.OneContact.Web;
using RecursiveAI.OneContact.Serialization;

namespace RecursiveAI.OneContact;

/// <summary>
/// A website address (WHATWG URL). The canonical form is the serialized URL with the host lowercased and IDNA-normalized; the key is the aggressive, versioned form used to compare values.
/// </summary>
/// <remarks>
/// <para>A value type: <c>default(WebAddress)</c> is the empty value, whose <see cref="Canonical"/> and <see cref="Key"/> are empty strings.</para>
/// <para><c>==</c> and <see cref="Equals(WebAddress)"/> compare canonical forms ordinally. Keys are compared only explicitly, through
/// <see cref="Key"/>, <see cref="GetKey"/> or <see cref="Contacts.SameKey{T}"/>.</para>
/// <para>The <see cref="IFormatProvider"/> taken by the <see cref="IParsable{TSelf}"/> and <see cref="IFormattable"/> members is ignored:
/// results never depend on the current culture.</para>
/// </remarks>
[JsonConverter(typeof(WebAddressJsonConverter))]
public readonly partial struct WebAddress :
    IContactValue,
    ISpanParsable<WebAddress>,
    ISpanFormattable,
    IEquatable<WebAddress>,
    IEqualityOperators<WebAddress, WebAddress, bool>
{
    private readonly string? _canonical;
    private readonly WebParts? _parts;

    internal WebAddress(string canonical, SourceSpan source, WebFacts flags, WebParts? parts)
    {
        _canonical = canonical;
        Source = source;
        Flags = flags;
        _parts = parts;
    }

    internal WebParts? Parts => _parts;

    /// <summary>Gets the canonical form: faithful to the standard or the authority's style, never changing where mail goes or what a link points to. Empty for the default value.</summary>
    public string Canonical => _canonical ?? string.Empty;

    /// <summary>Gets where the value was found in the text it was parsed from, in UTF-16 code units. Empty for the default value.</summary>
    public SourceSpan Source { get; }

    /// <summary>Gets the facts found about the value.</summary>
    public WebFacts Flags { get; }

    /// <summary>Gets the scheme, lowercase, such as <c>https</c>. Empty for the default value.</summary>
    public string Scheme => _parts?.Scheme ?? string.Empty;

    /// <summary>Gets the host as it appears in the canonical form: a lowercase ASCII domain (internationalized names in their <c>xn--</c> form), a dotted IPv4 address, a bracketed IPv6 address, or an opaque host. Empty when the address has no host or for the default value.</summary>
    public string Host => _parts?.Host ?? string.Empty;

    /// <summary>Gets the explicit port, or <see langword="null"/> when none is written or it is the scheme's default port (the canonical form leaves default ports out).</summary>
    public int? Port => _parts is { Port: >= 0 } p ? p.Port : null;

    /// <summary>Gets the path as it appears in the canonical form, such as <c>/a/b</c>.</summary>
    public string Path => _parts?.Path ?? string.Empty;

    /// <summary>Gets the query as it appears in the canonical form (without the <c>?</c>, tracking parameters removed), or <see langword="null"/> when there is none.</summary>
    public string? Query => _parts?.Query;

    /// <summary>Gets the public suffix of the host according to the Public Suffix List (ICANN and private sections), or <see langword="null"/> when the host is not a domain name.</summary>
    public string? PublicSuffix => _parts?.PublicSuffix;

    /// <summary>Gets the registrable domain of the host (its public suffix and one more label), or <see langword="null"/> when the host is not a domain name or is itself a public suffix.</summary>
    public string? RegistrableDomain => _parts?.RegistrableDomain;

    /// <summary>Gets the comparison key for the latest <see cref="KeyVersion"/> (<see cref="KeyVersions.Latest"/>).</summary>
    public string Key => GetKey(KeyVersions.Latest);

    /// <summary>Computes the comparison key for a named key version. A published version's output never changes.</summary>
    /// <param name="version">The key version.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is not a defined <see cref="KeyVersion"/>.</exception>
    public string GetKey(KeyVersion version) => WebKey.Compute(this, version);

    ContactKind IContactValue.Kind => ContactKind.Web;

    /// <summary>Parses a value.</summary>
    /// <param name="s">The text to parse. Surrounding white space is ignored.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="ContactParseException">The text is not a valid value; <see cref="ContactParseException.Reason"/> says why.</exception>
    public static WebAddress Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan());
    }

    /// <summary>Parses a value.</summary>
    /// <param name="s">The text to parse. Surrounding white space is ignored.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="ContactParseException">The text is not a valid value; <see cref="ContactParseException.Reason"/> says why.</exception>
    public static WebAddress Parse(ReadOnlySpan<char> s)
    {
        OperationStatus status = TryParse(s, out WebAddress value, out ParseDetail detail);
        if (status != OperationStatus.Done)
        {
            throw ContactParseException.FromDetail(ContactKind.Web, detail, s.Length);
        }

        return value;
    }

    /// <summary>Tries to parse a value.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="result">The parsed value, or the default value when parsing fails.</param>
    /// <returns><see langword="true"/> when the text was parsed.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, out WebAddress result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        return TryParse(s.AsSpan(), out result);
    }

    /// <summary>Tries to parse a value.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="result">The parsed value, or the default value when parsing fails.</param>
    /// <returns><see langword="true"/> when the text was parsed.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, out WebAddress result) =>
        WebAddressParser.TryParseValue(s, out result);

    /// <summary>The low-level parse: returns a status and a <see cref="ParseDetail"/> instead of throwing.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="value">The parsed value, or the default value when the status is not <see cref="OperationStatus.Done"/>.</param>
    /// <param name="detail">Issues with reason codes, per-field offsets and confidence, and whether a rule or a model decided each field.</param>
    /// <returns><see cref="OperationStatus.Done"/>, or <see cref="OperationStatus.InvalidData"/> with the reason in <paramref name="detail"/>.</returns>
    public static OperationStatus TryParse(ReadOnlySpan<char> s, out WebAddress value, out ParseDetail detail) =>
        WebAddressParser.TryParse(s, out value, out detail);

    static WebAddress IParsable<WebAddress>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<WebAddress>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out WebAddress result) =>
        TryParse(s, out result);

    static WebAddress ISpanParsable<WebAddress>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);

    static bool ISpanParsable<WebAddress>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out WebAddress result) =>
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
    public bool Equals(WebAddress other) => string.Equals(Canonical, other.Canonical, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is WebAddress other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => string.GetHashCode(Canonical, StringComparison.Ordinal);

    /// <summary>Compares canonical forms ordinally.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the canonical forms are equal.</returns>
    public static bool operator ==(WebAddress left, WebAddress right) => left.Equals(right);

    /// <summary>Compares canonical forms ordinally.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the canonical forms differ.</returns>
    public static bool operator !=(WebAddress left, WebAddress right) => !left.Equals(right);
}
