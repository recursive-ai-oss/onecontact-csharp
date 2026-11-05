using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;
using RecursiveAI.OneContact.Keys;
using RecursiveAI.OneContact.Phones;
using RecursiveAI.OneContact.Serialization;

namespace RecursiveAI.OneContact;

/// <summary>
/// A telephone number. The canonical form is the E.164 form of the number, with any extension; the key is the aggressive, versioned form used to compare values.
/// </summary>
/// <remarks>
/// <para>A value type: <c>default(PhoneNumber)</c> is the empty value, whose <see cref="Canonical"/> and <see cref="Key"/> are empty strings.</para>
/// <para><c>==</c> and <see cref="Equals(PhoneNumber)"/> compare canonical forms ordinally. Keys are compared only explicitly, through
/// <see cref="Key"/>, <see cref="GetKey"/> or <see cref="Contacts.SameKey{T}"/>.</para>
/// <para>The <see cref="IFormatProvider"/> taken by the <see cref="IParsable{TSelf}"/> and <see cref="IFormattable"/> members is ignored:
/// results never depend on the current culture.</para>
/// </remarks>
[JsonConverter(typeof(PhoneNumberJsonConverter))]
public readonly partial struct PhoneNumber :
    IContactValue,
    ISpanParsable<PhoneNumber>,
    ISpanFormattable,
    IEquatable<PhoneNumber>,
    IEqualityOperators<PhoneNumber, PhoneNumber, bool>
{
    private readonly string? _canonical;

    internal PhoneNumber(string canonical, SourceSpan source, PhoneFacts flags)
    {
        _canonical = canonical;
        Source = source;
        Flags = flags;
    }

    /// <summary>Gets the canonical form: faithful to the standard or the authority's style, never changing where mail goes or what a link points to. Empty for the default value.</summary>
    public string Canonical => _canonical ?? string.Empty;

    /// <summary>Gets where the value was found in the text it was parsed from, in UTF-16 code units. Empty for the default value.</summary>
    public SourceSpan Source { get; }

    /// <summary>Gets the facts found about the value.</summary>
    public PhoneFacts Flags { get; }

    /// <summary>Gets the comparison key for the latest <see cref="KeyVersion"/> (<see cref="KeyVersions.Latest"/>).</summary>
    public string Key => GetKey(KeyVersions.Latest);

    /// <summary>Computes the comparison key for a named key version. A published version's output never changes.</summary>
    /// <param name="version">The key version.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is not a defined <see cref="KeyVersion"/>.</exception>
    public string GetKey(KeyVersion version) => PhoneKey.Compute(this, version);

    ContactKind IContactValue.Kind => ContactKind.Phone;

    /// <summary>Parses a value. Numbers without a leading <c>+</c> are national and need a region: use the overload that takes one.</summary>
    /// <param name="s">The text to parse. Surrounding white space is ignored.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="ContactParseException">The text is not a valid value; <see cref="ContactParseException.Reason"/> says why.</exception>
    public static PhoneNumber Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan());
    }

    /// <summary>Parses a value. Numbers without a leading <c>+</c> are national and need a region: use the overload that takes one.</summary>
    /// <param name="s">The text to parse. Surrounding white space is ignored.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="ContactParseException">The text is not a valid value; <see cref="ContactParseException.Reason"/> says why.</exception>
    public static PhoneNumber Parse(ReadOnlySpan<char> s)
    {
        OperationStatus status = TryParse(s, out PhoneNumber value, out ParseDetail detail);
        if (status != OperationStatus.Done)
        {
            throw ContactParseException.FromDetail(ContactKind.Phone, detail, s.Length);
        }

        return value;
    }

    /// <summary>Tries to parse a value.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="result">The parsed value, or the default value when parsing fails.</param>
    /// <returns><see langword="true"/> when the text was parsed.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, out PhoneNumber result)
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
    public static bool TryParse(ReadOnlySpan<char> s, out PhoneNumber result) =>
        TryParse(s, out result, out _) == OperationStatus.Done;

    /// <summary>The low-level parse: returns a status and a <see cref="ParseDetail"/> instead of throwing. Numbers must be international.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="value">The parsed number, or the default value when the status is not <see cref="OperationStatus.Done"/>.</param>
    /// <param name="detail">Issues with reason codes, per-field offsets and confidence, and whether a rule or a model decided each field.</param>
    /// <returns><see cref="OperationStatus.Done"/>, or <see cref="OperationStatus.InvalidData"/> with the reason in <paramref name="detail"/>.</returns>
    public static OperationStatus TryParse(ReadOnlySpan<char> s, out PhoneNumber value, out ParseDetail detail) =>
        PhoneNumberParser.TryParse(s, Region.Unknown, out value, out detail);

    /// <summary>Parses a phone number, reading numbers without a leading <c>+</c> as national numbers of <paramref name="defaultRegion"/>.</summary>
    /// <param name="s">The text to parse. Surrounding white space is ignored.</param>
    /// <param name="defaultRegion">The region that national numbers belong to.</param>
    /// <returns>The parsed number.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="ContactParseException">The text is not a phone number, or it is national and <paramref name="defaultRegion"/> is <see cref="Region.Unknown"/>.</exception>
    public static PhoneNumber Parse(string s, Region defaultRegion)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan(), defaultRegion);
    }

    /// <summary>Parses a phone number, reading numbers without a leading <c>+</c> as national numbers of <paramref name="defaultRegion"/>.</summary>
    /// <param name="s">The text to parse. Surrounding white space is ignored.</param>
    /// <param name="defaultRegion">The region that national numbers belong to.</param>
    /// <returns>The parsed number.</returns>
    /// <exception cref="ContactParseException">The text is not a phone number, or it is national and <paramref name="defaultRegion"/> is <see cref="Region.Unknown"/>.</exception>
    public static PhoneNumber Parse(ReadOnlySpan<char> s, Region defaultRegion)
    {
        OperationStatus status = TryParse(s, defaultRegion, out PhoneNumber value, out ParseDetail detail);
        if (status != OperationStatus.Done)
        {
            throw ContactParseException.FromDetail(ContactKind.Phone, detail, s.Length);
        }

        return value;
    }

    /// <summary>Tries to parse a phone number, reading numbers without a leading <c>+</c> as national numbers of <paramref name="defaultRegion"/>.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="defaultRegion">The region that national numbers belong to.</param>
    /// <param name="result">The parsed number, or the default value when parsing fails.</param>
    /// <returns><see langword="true"/> when the text was parsed.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, Region defaultRegion, out PhoneNumber result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        return TryParse(s.AsSpan(), defaultRegion, out result);
    }

    /// <summary>Tries to parse a phone number, reading numbers without a leading <c>+</c> as national numbers of <paramref name="defaultRegion"/>.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="defaultRegion">The region that national numbers belong to.</param>
    /// <param name="result">The parsed number, or the default value when parsing fails.</param>
    /// <returns><see langword="true"/> when the text was parsed.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, Region defaultRegion, out PhoneNumber result) =>
        TryParse(s, defaultRegion, out result, out _) == OperationStatus.Done;

    /// <summary>The low-level parse: returns a status and a <see cref="ParseDetail"/> instead of throwing.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="defaultRegion">The region that national numbers belong to; <see cref="Region.Unknown"/> accepts international numbers only.</param>
    /// <param name="value">The parsed number, or the default value when the status is not <see cref="OperationStatus.Done"/>.</param>
    /// <param name="detail">Issues with reason codes, per-field offsets and confidence, and whether a rule or a model decided each field.</param>
    /// <returns><see cref="OperationStatus.Done"/>, or <see cref="OperationStatus.InvalidData"/> with the reason in <paramref name="detail"/>.</returns>
    public static OperationStatus TryParse(ReadOnlySpan<char> s, Region defaultRegion, out PhoneNumber value, out ParseDetail detail) =>
        PhoneNumberParser.TryParse(s, defaultRegion, out value, out detail);

    static PhoneNumber IParsable<PhoneNumber>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<PhoneNumber>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out PhoneNumber result) =>
        TryParse(s, out result);

    static PhoneNumber ISpanParsable<PhoneNumber>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);

    static bool ISpanParsable<PhoneNumber>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out PhoneNumber result) =>
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
    public bool Equals(PhoneNumber other) => string.Equals(Canonical, other.Canonical, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PhoneNumber other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => string.GetHashCode(Canonical, StringComparison.Ordinal);

    /// <summary>Compares canonical forms ordinally.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the canonical forms are equal.</returns>
    public static bool operator ==(PhoneNumber left, PhoneNumber right) => left.Equals(right);

    /// <summary>Compares canonical forms ordinally.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the canonical forms differ.</returns>
    public static bool operator !=(PhoneNumber left, PhoneNumber right) => !left.Equals(right);
}
