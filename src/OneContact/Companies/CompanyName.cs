using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;
using RecursiveAI.OneContact.Keys;
using RecursiveAI.OneContact.Names;
using RecursiveAI.OneContact.Companies;
using RecursiveAI.OneContact.Serialization;

namespace RecursiveAI.OneContact;

/// <summary>
/// A company name. The canonical form is the base name followed by its legal form in canonical abbreviation; the key is the aggressive, versioned form used to compare values.
/// </summary>
/// <remarks>
/// <para>A value type: <c>default(CompanyName)</c> is the empty value, whose <see cref="Canonical"/> and <see cref="Key"/> are empty strings.</para>
/// <para><c>==</c> and <see cref="Equals(CompanyName)"/> compare canonical forms ordinally. Keys are compared only explicitly, through
/// <see cref="Key"/>, <see cref="GetKey"/> or <see cref="Contacts.SameKey{T}"/>.</para>
/// <para>The <see cref="IFormatProvider"/> taken by the <see cref="IParsable{TSelf}"/> and <see cref="IFormattable"/> members is ignored:
/// results never depend on the current culture.</para>
/// </remarks>
[JsonConverter(typeof(CompanyNameJsonConverter))]
public readonly partial struct CompanyName :
    IContactValue,
    ISpanParsable<CompanyName>,
    ISpanFormattable,
    IEquatable<CompanyName>,
    IEqualityOperators<CompanyName, CompanyName, bool>
{
    private readonly string? _canonical;
    private readonly CompanyNameParts? _parts;

    internal CompanyName(string canonical, SourceSpan source, CompanyFacts flags, CompanyNameParts parts)
    {
        _canonical = canonical;
        Source = source;
        Flags = flags;
        _parts = parts;
    }

    /// <summary>Gets the leading "The", kept apart from the base name. Empty when there is none.</summary>
    public NamePart LeadingArticle => _parts?.LeadingArticle ?? default;

    /// <summary>Gets the base name: the name without its leading "The", legal form, trading name and care-of part, as written.</summary>
    public NamePart Base => _parts?.Base ?? default;

    /// <summary>Gets the legal form found at the end (or, for forms such as "AB" or "SIA", the start) of the name, or <see langword="null"/> when there is none.</summary>
    public CompanyLegalForm? LegalForm => _parts?.LegalForm;

    /// <summary>Gets the trading name after "d/b/a", "dba", "doing business as", "t/a" or "trading as". The base name and legal form are the legal entity's.</summary>
    public NamePart TradingAs => _parts?.TradingAs ?? default;

    /// <summary>Gets the text after "c/o" or "care of" (usually a person or another company).</summary>
    public NamePart CareOf => _parts?.CareOf ?? default;

    /// <summary>Gets the canonical form: faithful to the standard or the authority's style, never changing where mail goes or what a link points to. Empty for the default value.</summary>
    public string Canonical => _canonical ?? string.Empty;

    /// <summary>Gets where the value was found in the text it was parsed from, in UTF-16 code units. Empty for the default value.</summary>
    public SourceSpan Source { get; }

    /// <summary>Gets the facts found about the value.</summary>
    public CompanyFacts Flags { get; }

    /// <summary>Gets the comparison key for the latest <see cref="KeyVersion"/> (<see cref="KeyVersions.Latest"/>).</summary>
    public string Key => GetKey(KeyVersions.Latest);

    /// <summary>Computes the comparison key for a named key version. A published version's output never changes.</summary>
    /// <param name="version">The key version.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is not a defined <see cref="KeyVersion"/>.</exception>
    public string GetKey(KeyVersion version) => CompanyKey.Compute(this, version);

    ContactKind IContactValue.Kind => ContactKind.Company;

    /// <summary>Parses a value.</summary>
    /// <param name="s">The text to parse. Surrounding white space is ignored.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="ContactParseException">The text is not a valid value; <see cref="ContactParseException.Reason"/> says why.</exception>
    public static CompanyName Parse(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan());
    }

    /// <summary>Parses a value.</summary>
    /// <param name="s">The text to parse. Surrounding white space is ignored.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="ContactParseException">The text is not a valid value; <see cref="ContactParseException.Reason"/> says why.</exception>
    public static CompanyName Parse(ReadOnlySpan<char> s)
    {
        OperationStatus status = TryParse(s, out CompanyName value, out ParseDetail detail);
        if (status != OperationStatus.Done)
        {
            throw ContactParseException.FromDetail(ContactKind.Company, detail, s.Length);
        }

        return value;
    }

    /// <summary>Tries to parse a value.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="result">The parsed value, or the default value when parsing fails.</param>
    /// <returns><see langword="true"/> when the text was parsed.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, out CompanyName result)
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
    public static bool TryParse(ReadOnlySpan<char> s, out CompanyName result) =>
        TryParse(s, out result, out _) == OperationStatus.Done;

    /// <summary>The low-level parse: returns a status and a <see cref="ParseDetail"/> instead of throwing.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="value">The parsed value, or the default value when the status is not <see cref="OperationStatus.Done"/>.</param>
    /// <param name="detail">Issues with reason codes, per-field offsets and confidence, and whether a rule or a model decided each field.</param>
    /// <returns><see cref="OperationStatus.Done"/>, or <see cref="OperationStatus.InvalidData"/> with the reason in <paramref name="detail"/>.</returns>
    public static OperationStatus TryParse(ReadOnlySpan<char> s, out CompanyName value, out ParseDetail detail) =>
        CompanyNameParser.TryParse(s, default, out value, out detail);

    /// <summary>Parses a company name read with hints: a country limits the legal forms considered to that jurisdiction's.</summary>
    /// <param name="s">The text to parse. Surrounding white space is ignored.</param>
    /// <param name="hints">The country (and language) the name is from; neither is needed.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="ContactParseException">The text is empty.</exception>
    public static CompanyName Parse(string s, NameHints hints)
    {
        ArgumentNullException.ThrowIfNull(s);
        OperationStatus status = TryParse(s, hints, out CompanyName value, out ParseDetail detail);
        if (status != OperationStatus.Done)
        {
            throw ContactParseException.FromDetail(ContactKind.Company, detail, s.Length);
        }

        return value;
    }

    /// <summary>The low-level parse with hints: returns a status and a <see cref="ParseDetail"/> instead of throwing.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="hints">The country (and language) the name is from.</param>
    /// <param name="value">The parsed value, or the default value when the status is not <see cref="OperationStatus.Done"/>.</param>
    /// <param name="detail">Issues, per-part offsets and the rule that decided each part.</param>
    /// <returns><see cref="OperationStatus.Done"/>, or <see cref="OperationStatus.InvalidData"/> with the reason in <paramref name="detail"/>.</returns>
    public static OperationStatus TryParse(ReadOnlySpan<char> s, NameHints hints, out CompanyName value, out ParseDetail detail) =>
        CompanyNameParser.TryParse(s, hints, out value, out detail);

    /// <summary>Parses with the country of the name as a hint; a shorthand for <see cref="Parse(string, NameHints)"/> with <see cref="NameHints.ForCountry(Region)"/>.</summary>
    /// <param name="s">The text to parse. Surrounding white space is ignored.</param>
    /// <param name="country">The country the name is from; <see cref="Region.Unknown"/> for none.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="ContactParseException">The text cannot be read as this kind of name; <see cref="ContactParseException.Reason"/> says why.</exception>
    public static CompanyName Parse(string s, Region country) => Parse(s, NameHints.ForCountry(country));

    /// <summary>Tries to parse with hints.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="hints">The country and language the name is written in.</param>
    /// <param name="result">The parsed value, or the default value when parsing fails.</param>
    /// <returns><see langword="true"/> when the text was parsed.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, NameHints hints, out CompanyName result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        return TryParse(s.AsSpan(), hints, out result, out _) == OperationStatus.Done;
    }

    /// <summary>Tries to parse with the country of the name as a hint.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="country">The country the name is from; <see cref="Region.Unknown"/> for none.</param>
    /// <param name="result">The parsed value, or the default value when parsing fails.</param>
    /// <returns><see langword="true"/> when the text was parsed.</returns>
    public static bool TryParse([NotNullWhen(true)] string? s, Region country, out CompanyName result) =>
        TryParse(s, NameHints.ForCountry(country), out result);

    /// <summary>Tries to parse with hints.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="hints">The country and language the name is written in.</param>
    /// <param name="result">The parsed value, or the default value when parsing fails.</param>
    /// <returns><see langword="true"/> when the text was parsed.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, NameHints hints, out CompanyName result) =>
        TryParse(s, hints, out result, out _) == OperationStatus.Done;

    static CompanyName IParsable<CompanyName>.Parse(string s, IFormatProvider? provider) => Parse(s);

    static bool IParsable<CompanyName>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out CompanyName result) =>
        TryParse(s, out result);

    static CompanyName ISpanParsable<CompanyName>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider) => Parse(s);

    static bool ISpanParsable<CompanyName>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out CompanyName result) =>
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
    public bool Equals(CompanyName other) => string.Equals(Canonical, other.Canonical, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is CompanyName other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => string.GetHashCode(Canonical, StringComparison.Ordinal);

    /// <summary>Compares canonical forms ordinally.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the canonical forms are equal.</returns>
    public static bool operator ==(CompanyName left, CompanyName right) => left.Equals(right);

    /// <summary>Compares canonical forms ordinally.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the canonical forms differ.</returns>
    public static bool operator !=(CompanyName left, CompanyName right) => !left.Equals(right);
}
