using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Addresses.Streets;

namespace RecursiveAI.OneContact.Addresses.Parsing;

/// <summary>A line of the input kept as written (a recipient, an organization, an attention line) with where it is.</summary>
/// <param name="Text">The line as written.</param>
/// <param name="Span">Where it is in the original input, in UTF-16 code units.</param>
/// <param name="Standard">The line in the postal style: upper case, with the periods and commas the authority leaves out removed (USPS Publication 28 section 222). Words are never changed.</param>
public readonly record struct ParsedAddressText(string Text, SourceSpan Span, string Standard);

/// <summary>One field of a parsed address: what the input said, the standardized form, where it was, how sure the parse is and which rule decided it.</summary>
/// <param name="Name">The field name: <c>locality</c>, <c>admin-area</c>, <c>postal-code</c>, <c>country</c>, <c>urbanization</c>, or <c>street.&lt;role&gt;</c> for a street part (<c>street.house-number</c>, <c>street.pre-directional</c>, ...).</param>
/// <param name="Text">The text as written.</param>
/// <param name="Standard">The text after the authority's token rewrites (upper case, as printed in the postal style).</param>
/// <param name="Span">Where the field is in the original input, in UTF-16 code units.</param>
/// <param name="Confidence">From 0 to 1; exactly 1 for rule decisions.</param>
/// <param name="Source">Whether a rule or a model decided the field.</param>
/// <param name="Rule">The rule that decided it, named for the guide clause it follows.</param>
public readonly record struct ParsedAddressField(string Name, string Text, string Standard, SourceSpan Span, float Confidence, DecisionSource Source, string Rule);

/// <summary>
/// An address read by the rules: delivery lines broken into street parts, the last line (locality, state or province, postal code), the country the
/// content points to, and the lines kept as written above the delivery lines. Every field carries its offsets into the original input.
/// </summary>
/// <remarks>Immutable. Standardizing never adds a field or a value the input lacked: a city is never filled from a postal code and a province never from a forward sortation area.</remarks>
public sealed class ParsedAddress
{
    internal ParsedAddress(
        Region country,
        float countryConfidence,
        bool countryInferred,
        IReadOnlyList<ParsedAddressText> leadingLines,
        IReadOnlyList<StreetLine> deliveryLines,
        ParsedAddressField? locality,
        ParsedAddressField? adminArea,
        ParsedAddressField? postalCode,
        ParsedAddressField? countryName,
        IReadOnlyList<ParsedAddressField> fields,
        AddressFacts facts,
        SourceSpan span)
    {
        Country = country;
        CountryConfidence = countryConfidence;
        CountryInferred = countryInferred;
        LeadingLines = leadingLines;
        DeliveryLines = deliveryLines;
        Locality = locality;
        AdminArea = adminArea;
        PostalCode = postalCode;
        CountryName = countryName;
        Fields = fields;
        Facts = facts;
        Span = span;
    }

    /// <summary>Gets the country the rules decided on: the United States, Canada, or a country of the first Tier 2 group (France with its overseas departments, Monaco, Belgium, the Netherlands, Luxembourg, Germany, Austria, Switzerland, Liechtenstein).</summary>
    public Region Country { get; }

    /// <summary>Gets how sure the country is, from 0 to 1.</summary>
    public float CountryConfidence { get; }

    /// <summary>Gets a value indicating whether the country was inferred from the content rather than given as a hint or written in the input.</summary>
    public bool CountryInferred { get; }

    /// <summary>Gets the lines above the delivery lines, kept as written.</summary>
    public IReadOnlyList<ParsedAddressText> LeadingLines { get; }

    /// <summary>Gets the delivery lines in the order they are printed: a secondary unit line, the street line, and so on.</summary>
    public IReadOnlyList<StreetLine> DeliveryLines { get; }

    /// <summary>Gets the city or other locality, if the input has one.</summary>
    public ParsedAddressField? Locality { get; }

    /// <summary>Gets the state or province as its two-letter code in <see cref="ParsedAddressField.Standard"/>, if the input has one.</summary>
    public ParsedAddressField? AdminArea { get; }

    /// <summary>Gets the ZIP, ZIP+4 or postal code, if the input has one.</summary>
    public ParsedAddressField? PostalCode { get; }

    /// <summary>Gets the country name written in the input, if any.</summary>
    public ParsedAddressField? CountryName { get; }

    /// <summary>Gets every field in input order.</summary>
    public IReadOnlyList<ParsedAddressField> Fields { get; }

    /// <summary>Gets the facts found about the address.</summary>
    public AddressFacts Facts { get; }

    /// <summary>Gets the stretch of the original input the address covers.</summary>
    public SourceSpan Span { get; }

    // The text the address was read from and the country hint it was read with; kept so a stored address can be read again with its original casing.
    internal string SourceText { get; set; } = string.Empty;

    internal Region Hint { get; set; }

    // The printed lines of an address of the first Tier 2 group of countries; null for the United States and Canada.
    internal Tier2.T2Layout? Tier2 { get; set; }

    /// <summary>Writes the address.</summary>
    /// <param name="style">Postal (the authority's style, upper case) or display (title case).</param>
    /// <param name="layout">One line per address line, or all on one line.</param>
    /// <param name="includeCountry">True to add a country line; domestic addresses leave it out, as both authorities ask.</param>
    /// <returns>The text; no trailing line break.</returns>
    public string ToString(AddressStyle style, AddressLayout layout = AddressLayout.Label, bool includeCountry = false) =>
        AddressRenderer.Render(this, style, layout, includeCountry);

    /// <summary>Writes the address in the postal style on one line, the form used as the canonical form of a <see cref="PostalAddress"/>.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => AddressRenderer.Render(this, AddressStyle.Postal, AddressLayout.SingleLine, includeCountry: false);
}
