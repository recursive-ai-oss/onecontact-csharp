using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Addresses.Streets;

namespace RecursiveAI.OneContact;

public readonly partial struct PostalAddress
{
    // The street parts and the authority renderings come from the rule reader. An address made from fields is read again from its label lines, so it
    // gets the same parts and renderings as one parsed from text. Addresses outside the United States and Canada have neither.
    private ParsedAddress? Rules()
    {
        if (_parsed is not null)
        {
            return _parsed;
        }

        if (_parts is null || (_parts.Country != Region.US && _parts.Country != Region.CA && !Addresses.Tier2.Tier2Reader.Handles(_parts.Country)))
        {
            return null;
        }

        string text = string.Join('\n', GetLabelLines());
        return RuleAddressParser.TryParse(text, _parts.Country, out ParsedAddress? read, out _) == System.Buffers.OperationStatus.Done ? read : null;
    }

    /// <summary>
    /// Gets the delivery lines broken into street parts (number, directionals, street name and type, unit, box, route) with offsets and the rule that decided each part.
    /// Offsets index the text the address was parsed from; for an address made from fields they index the label lines joined with line feeds.
    /// Empty for an address outside the United States and Canada and for the default value.
    /// </summary>
    public IReadOnlyList<StreetLine> StreetDetail => Rules()?.DeliveryLines ?? [];

    /// <summary>
    /// Gets the address as the parsing rules read it, with every field's offsets, confidence and deciding rule, or <see langword="null"/> for an address outside the
    /// United States and Canada, for the default value, and for fields the rules cannot read back.
    /// </summary>
    public ParsedAddress? RuleDetail => Rules();

    /// <summary>
    /// Writes the address in a postal authority's style: USPS Publication 28 for the United States and Canada Post's guidelines for Canada. Standardizing rewrites tokens only; it never adds a field or a value the address lacks.
    /// </summary>
    /// <param name="style"><see cref="AddressStyle.Postal"/> (the authority's own style, upper case) or <see cref="AddressStyle.Display"/> (the same tokens in title case).</param>
    /// <param name="layout"><see cref="AddressLayout.Label"/> (one line per address line, separated by a line feed) or <see cref="AddressLayout.SingleLine"/> (a comma and a space between lines).</param>
    /// <param name="includeCountry">Adds a country line; domestic addresses leave it out, as both authorities ask.</param>
    /// <returns>The text with no trailing line break. For an address the rules do not cover, the country's label from the field model, unstyled.</returns>
    public string ToString(AddressStyle style, AddressLayout layout = AddressLayout.Label, bool includeCountry = false)
    {
        ParsedAddress? parsed = Rules();
        if (parsed is not null)
        {
            return parsed.ToString(style, layout, includeCountry);
        }

        return layout == AddressLayout.SingleLine ? ToSingleLine() : string.Join('\n', GetLabelLines());
    }

    // Used by the JSON converter: the canonical form was stored, so it is restored as it was written instead of being derived again.
    internal static PostalAddress Restore(string canonical, SourceSpan source, AddressFacts flags, Addresses.AddressParts parts) =>
        new(canonical, parts, null, source, flags);

    // Used by the JSON converter for an address read from text: the rules read the stored text again, so the renderings keep the casing the text had.
    internal static PostalAddress RestoreParsed(ParsedAddress parsed, SourceSpan source) =>
        new(parsed.ToString(), Addresses.PostalAddressParser.PartsOf(parsed), parsed, source, parsed.Facts);

    internal Addresses.AddressParts? PartsForJson => _parts;

    internal ParsedAddress? ParsedForJson => _parsed;
}
