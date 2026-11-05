using RecursiveAI.OneContact.Names;

namespace RecursiveAI.OneContact.Keys;

/// <summary>
/// Computes the comparison key of a <see cref="CompanyName"/>. The key is the aggressive, versioned form (see <see cref="KeyVersion"/>);
/// every rule of a version is documented in docs/company-names.md and a published version's output never changes.
/// </summary>
public static class CompanyKey
{
    /// <summary>Computes the key of <paramref name="value"/> under key version <paramref name="version"/>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="version">The key version.</param>
    /// <returns>The key; empty for the default value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is not a defined <see cref="KeyVersion"/>.</exception>
    public static string Compute(CompanyName value, KeyVersion version)
    {
        KeyVersions.ThrowIfUndefined(version);
        if (value.Base.IsEmpty)
        {
            return string.Empty;
        }

        // V1 (docs/company-names.md): the base name folded to lower-case words (an ampersand is the word "and"; the leading "The",
        // trading name and care-of part are left out), a bar, then the legal form reduced to its ELF code, or "~" and the folded
        // abbreviation when the form fits several codes, or nothing.
        string text = value.Base.Text.Replace("&", " and ", StringComparison.Ordinal);
        string words = NameFolding.Words(text);
        string form = value.LegalForm?.KeyToken ?? string.Empty;
        return words + "|" + form;
    }
}
