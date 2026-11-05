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

        // The canonical form is the postal-style rendering of a parsed address, so reading it back gives the parts the key is made from.
        if (RuleAddressParser.TryParse(value.Canonical, value.Country == Region.US || value.Country == Region.CA || Addresses.Tier2.Tier2Reader.Handles(value.Country) ? value.Country : Region.Unknown, out ParsedAddress? address, out _) == System.Buffers.OperationStatus.Done && address is not null)
        {
            return Compute(address, version);
        }

        return string.Join(' ', AddressExpander.Normalize(AddressExpander.Fold(value.Canonical)));
    }

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
