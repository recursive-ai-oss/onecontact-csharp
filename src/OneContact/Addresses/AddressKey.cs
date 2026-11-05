namespace RecursiveAI.OneContact.Keys;

/// <summary>
/// Computes the comparison key of a <see cref="PostalAddress"/>. The key is the aggressive, versioned form (see <see cref="KeyVersion"/>);
/// every rule of a version is documented in <c>docs/</c> and a published version's output never changes.
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

        // PLACEHOLDER (V1 is not published yet): the key is the canonical form until the Address rules are written.
        return value.Canonical;
    }
}
