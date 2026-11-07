using RecursiveAI.OneContact.Addresses.Expansion;
using RecursiveAI.OneContact.Addresses.Parsing;

namespace RecursiveAI.OneContact.Keys;

/// <summary>
/// Computes the comparison key of a <see cref="PostalAddress"/>. The key is the aggressive, versioned form (see <see cref="KeyVersion"/>);
/// every rule of a version is in docs/address-keys.md and a published version's output never changes.
/// </summary>
public static class AddressKey
{
    /// <summary>Computes the key of <paramref name="value"/> under key version <paramref name="version"/>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="version">The key version.</param>
    /// <returns>The key; empty for the default value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is not a defined <see cref="KeyVersion"/>.</exception>
    public static string Compute(PostalAddress value, KeyVersion version)
    {
        KeyVersions.ThrowIfUndefined(version);
        if (value.Canonical.Length == 0)
        {
            return string.Empty;
        }

        // An address without a country has no authority style to read back: its own fields make the key.
        if (value.ParsedForJson is { } kept && kept.Facts.HasFlag(AddressFacts.CountryUndetermined))
        {
            return Compute(kept, version);
        }

        // The canonical form is the postal-style rendering of a parsed address, so reading it back gives the parts the key is made from.
        if (RuleAddressParser.TryParse(value.Canonical, value.Country == Region.US || value.Country == Region.CA || Addresses.Tier2.Tier2Reader.Handles(value.Country) ? value.Country : Region.Unknown, out ParsedAddress? address, out _) == System.Buffers.OperationStatus.Done && address is not null)
        {
            return Compute(address, version);
        }

        // A country the rules do not read (a structurally described country, or one with no style of its own): the key is made from the fields in a fixed order, so
        // the layout of the country does not change it, and it starts with the country, so addresses in different countries never share a key.
        if (value.PartsForJson is { } parts)
        {
            string lines = string.Join(' ', parts.StreetLines.SelectMany(l => AddressExpander.Normalize(AddressExpander.Fold(l))));
            return string.Join(
                '|',
                parts.Country.IsUnknown ? "zz" : parts.Country.Code.ToLowerInvariant(),
                lines,
                Words(parts.DependentLocality),
                Words(parts.Locality),
                AddressExpander.Fold(parts.AdminArea),
                string.Concat(AddressExpander.Fold(parts.PostalCode).Where(char.IsLetterOrDigit)));
        }

        return string.Join(' ', AddressExpander.Normalize(AddressExpander.Fold(value.Canonical)));
    }

    private static string Words(string text) => string.Join(' ', AddressExpander.Normalize(AddressExpander.Fold(text)));

    /// <summary>Computes the key of a parsed address.</summary>
    /// <param name="address">The address.</param>
    /// <param name="version">The key version.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is not a defined <see cref="KeyVersion"/>.</exception>
    public static string Compute(ParsedAddress address, KeyVersion version)
    {
        ArgumentNullException.ThrowIfNull(address);
        KeyVersions.ThrowIfUndefined(version);
        return AddressKeyV1.Compute(address);
    }
}
