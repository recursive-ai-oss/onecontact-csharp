using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;
using RecursiveAI.OneContact.Keys;
using RecursiveAI.OneContact.Email;
using RecursiveAI.OneContact.Serialization;

namespace RecursiveAI.OneContact;

/// <summary>
/// An email address (RFC 5322 / RFC 6531). The canonical form is the address with its domain lowercased and IDNA-normalized and its local part as written; the key is the aggressive, versioned form used to compare values.
/// </summary>
/// <remarks>
/// <para>A value type: <c>default(EmailAddress)</c> is the empty value, whose <see cref="Canonical"/> and <see cref="Key"/> are empty strings.</para>
/// <para><c>==</c> and <see cref="Equals(EmailAddress)"/> compare canonical forms ordinally. Keys are compared only explicitly, through
/// <see cref="Key"/>, <see cref="GetKey"/> or <see cref="Contacts.SameKey{T}"/>.</para>
/// <para>The <see cref="IFormatProvider"/> taken by the <see cref="IParsable{TSelf}"/> and <see cref="IFormattable"/> members is ignored:
/// results never depend on the current culture.</para>
/// </remarks>
[JsonConverter(typeof(EmailAddressJsonConverter))]
public readonly partial struct EmailAddress :
    IContactValue,
    ISpanParsable<EmailAddress>,
    ISpanFormattable,
    IEquatable<EmailAddress>,
    IEqualityOperators<EmailAddress, EmailAddress, bool>
{
    private readonly string? _canonical;
    private readonly int _localLength;
    private readonly string? _displayName;

    internal EmailAddress(string canonical, int localLength, string? displayName, SourceSpan source, EmailFacts flags)
    {
        _canonical = canonical;
        _localLength = localLength;
        _displayName = displayName;
        Source = source;
        Flags = flags;
    }

    /// <summary>Gets the canonical form: faithful to the standard or the authority's style, never changing where mail goes or what a link points to. Empty for the default value.</summary>
    public string Canonical => _canonical ?? string.Empty;

    /// <summary>Gets where the value was found in the text it was parsed from, in UTF-16 code units. Empty for the default value.</summary>
    public SourceSpan Source { get; }

    /// <summary>Gets the facts found about the value.</summary>
    public EmailFacts Flags { get; }

    /// <summary>Gets the local part as written in the canonical form (a quoted local part keeps its quotes). Empty for the default value.</summary>
    public string LocalPart => Canonical[.._localLength];

    /// <summary>Gets the domain of the canonical form: lowercase, with internationalized labels as A-labels (<c>xn--</c>), or a domain literal in brackets. Empty for the default value.</summary>
    public string Domain => _canonical is null ? string.Empty : _canonical[(_localLength + 1)..];

    /// <summary>Gets the domain with A-labels shown as Unicode U-labels (UTS 46 ToUnicode). Equal to <see cref="Domain"/> when no label is internationalized and for domain literals.</summary>
    public string UnicodeDomain => EmailDomain.ToUnicode(Domain);

    /// <summary>Gets the display name of a <c>Name &lt;address&gt;</c> input: quoted strings unquoted, RFC 2047 encoded words decoded, comments dropped; <see langword="null"/> when the input had none. It is not part of the canonical form or the key.</summary>
    public string? DisplayName => _displayName;

    /// <summary>Gets the address with a suggested domain when the domain looks like a mistyped well-known provider (<c>gmial.com</c> for <c>gmail.com</c>); <see langword="null"/> otherwise. This is only a suggestion: nothing in the value is ever changed.</summary>
    public string? TypoSuggestion => _canonical is null ? null : EmailRules.SuggestTypoFix(this);

    /// <summary>Gets the comparison key for the latest <see cref="KeyVersion"/> (<see cref="KeyVersions.Latest"/>).</summary>
    public string Key => GetKey(KeyVersions.Latest);

    /// <summary>Computes the comparison key for a named key version. A published version's output never changes.</summary>
    /// <param name="version">The key version.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is not a defined <see cref="KeyVersion"/>.</exception>
    public string GetKey(KeyVersion version) => EmailKey.Compute(this, version);

    ContactKind IContactValue.Kind => ContactKind.Email;

    /// <summary>Parses a value.</summary>
    /// <param name="s">The text to parse. Surrounding white space is ignored.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="ContactParseException">The text is not a valid value; <see cref="ContactParseException.Reason"/> says why.</exception>
    public static EmailAddress Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan());
    }

    /// <summary>Parses a value.</summary>
    /// <param name="s">The text to parse. Surrounding white space is ignored.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="ContactParseException">The text is not a valid value; <see cref="ContactParseException.Reason"/> says why.</exception>
    public static EmailAddress Parse(ReadOnlySpan<char> s)
    {
        OperationStatus status = TryParse(s, out EmailAddress value, out ParseDetail detail);
        if (status != OperationStatus.Done)
        {
            throw ContactParseException.FromDetail(ContactKind.Email, detail, s.Length);
        }

        return value;
    }

    /// <summary>Tries to parse a value.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="result">The parsed value, or the default value when parsing fails.</param>
    /// <returns><see langword="true"/> when the text was parsed.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, out EmailAddress result)
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
    public static bool TryParse(ReadOnlySpan<char> s, out EmailAddress result) =>
        EmailAddressParser.TryParseValue(s, out result);

    /// <summary>The low-level parse: returns a status and a <see cref="ParseDetail"/> instead of throwing.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="value">The parsed value, or the default value when the status is not <see cref="OperationStatus.Done"/>.</param>
    /// <param name="detail">Issues with reason codes, per-field offsets and confidence, and whether a rule or a model decided each field.</param>
    /// <returns><see cref="OperationStatus.Done"/>, or <see cref="OperationStatus.InvalidData"/> with the reason in <paramref name="detail"/>.</returns>
    public static OperationStatus TryParse(ReadOnlySpan<char> s, out EmailAddress value, out ParseDetail detail) =>
        EmailAddressParser.TryParse(s, out value, out detail);

    static EmailAddress IParsable<EmailAddress>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<EmailAddress>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out EmailAddress result) =>
        TryParse(s, out result);

    static EmailAddress ISpanParsable<EmailAddress>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);

    static bool ISpanParsable<EmailAddress>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out EmailAddress result) =>
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
    public bool Equals(EmailAddress other) => string.Equals(Canonical, other.Canonical, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EmailAddress other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => string.GetHashCode(Canonical, StringComparison.Ordinal);

    /// <summary>Compares canonical forms ordinally.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the canonical forms are equal.</returns>
    public static bool operator ==(EmailAddress left, EmailAddress right) => left.Equals(right);

    /// <summary>Compares canonical forms ordinally.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the canonical forms differ.</returns>
    public static bool operator !=(EmailAddress left, EmailAddress right) => !left.Equals(right);
}
