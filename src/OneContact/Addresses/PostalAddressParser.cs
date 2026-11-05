using System.Buffers;
using RecursiveAI.OneContact.Addresses.Parsing;

namespace RecursiveAI.OneContact.Addresses;

/// <summary>
/// The parsing seam for <see cref="PostalAddress"/>: reads United States and Canadian addresses by rules (the country comes from the content or a hint). The canonical form is the
/// address in its postal authority's style on one line; the fields and street parts the rules found sit underneath it.
/// </summary>
internal static class PostalAddressParser
{
    internal static OperationStatus TryParse(ReadOnlySpan<char> input, Region hint, out PostalAddress value, out ParseDetail detail)
    {
        OperationStatus status = RuleAddressParser.TryParse(input, hint, out ParsedAddress? address, out detail);
        if (status != OperationStatus.Done || address is null)
        {
            value = default;
            return OperationStatus.InvalidData;
        }

        value = FromParsed(address);
        return OperationStatus.Done;
    }

    internal static PostalAddress FromParsed(ParsedAddress address) =>
        new(address.ToString(), PartsOf(address), address, address.Span, address.Facts);

    // Fields as the field model holds them: the state or province as its code, the postal code in the authority's spacing, the delivery lines in the postal style.
    // Lines above the delivery lines are not labelled by the rules, so they are kept together as the recipient, never split into an organization and a person.
    internal static AddressParts PartsOf(ParsedAddress a) => new(
        a.Country,
        null,
        a.AdminArea?.Standard ?? string.Empty,
        a.Locality?.Text ?? string.Empty,
        string.Empty,
        a.PostalCode?.Standard ?? string.Empty,
        string.Empty,
        string.Empty,
        string.Join(", ", a.LeadingLines.Select(l => l.Text)),
        [.. a.DeliveryLines.Select(l => l.ToPostalString())]);
}
