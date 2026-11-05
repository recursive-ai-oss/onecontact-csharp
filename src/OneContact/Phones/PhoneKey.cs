using RecursiveAI.OneContact.Phones;

namespace RecursiveAI.OneContact.Keys;

/// <summary>
/// Computes the comparison key of a <see cref="PhoneNumber"/>. The key is the aggressive, versioned form (see <see cref="KeyVersion"/>);
/// every rule of a version is documented in <c>docs/</c> and a published version's output never changes.
/// </summary>
public static class PhoneKey
{
    /// <summary>Computes the key of <paramref name="value"/> under key version <paramref name="version"/>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="version">The key version.</param>
    /// <returns>The key; empty for the default value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is not a defined <see cref="KeyVersion"/>.</exception>
    public static string Compute(PhoneNumber value, KeyVersion version)
    {
        KeyVersions.ThrowIfUndefined(version);

        // Key V1: the E.164 digits, and the extension (digits only) after an "x" when there is one. Nothing else varies: the same
        // line written with different punctuation, prefixes or scripts gets one key. Validity plays no part in the key.
        if (value.CountryCode == 0)
        {
            return string.Empty;
        }

        string e164 = "+" + value.CountryCode.ToString(System.Globalization.CultureInfo.InvariantCulture) + value.NationalSignificantNumber;
        if (string.IsNullOrEmpty(value.Extension))
        {
            return e164;
        }

        string ext = PhoneText.NormalizeDigitsOnly(value.Extension);
        return ext.Length == 0 ? e164 : e164 + "x" + ext;
    }
}
