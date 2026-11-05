using System.Buffers;
using RecursiveAI.OneContact.Addresses.Parsing;

namespace RecursiveAI.OneContact.Addresses;

/// <summary>
/// The parsing seam for <see cref="PostalAddress"/>: reads United States and Canadian addresses by rules (the country comes from the content). The canonical form is the
/// address in its postal authority's style on one line.
/// </summary>
internal static class PostalAddressParser
{
    internal static OperationStatus TryParse(ReadOnlySpan<char> input, out PostalAddress value, out ParseDetail detail)
    {
        OperationStatus status = RuleAddressParser.TryParse(input, Region.Unknown, out ParsedAddress? address, out detail);
        if (status != OperationStatus.Done || address is null)
        {
            value = default;
            return OperationStatus.InvalidData;
        }

        value = new PostalAddress(address.ToString(), address.Span, address.Facts);
        return OperationStatus.Done;
    }
}
