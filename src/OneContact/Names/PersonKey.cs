using System.Text;
using RecursiveAI.OneContact.Names;

namespace RecursiveAI.OneContact.Keys;

/// <summary>
/// Computes the comparison key of a <see cref="PersonName"/>. The key is the aggressive, versioned form (see <see cref="KeyVersion"/>);
/// every rule of a version is documented in docs/name-keys.md and a published version's output never changes.
/// </summary>
public static class PersonKey
{
    /// <summary>Computes the key of <paramref name="value"/> under key version <paramref name="version"/>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="version">The key version.</param>
    /// <returns>The key; empty for the default value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is not a defined <see cref="KeyVersion"/>.</exception>
    public static string Compute(PersonName value, KeyVersion version)
    {
        KeyVersions.ThrowIfUndefined(version);

        // V1 (docs/name-keys.md): given name in its formal spelling, the initials of the middle names, then the family name with
        // its particle and second family name run together. Titles, suffixes and the nickname are not part of it.
        StringBuilder sb = new();
        string given = Squash(value.Given.Text);
        if (given.Length > 0)
        {
            sb.Append(NameLexicon.FormalName(given));
        }

        foreach (string word in NameFolding.Words(value.Middle.Text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append(sb.Length > 0 ? " " : string.Empty).Append(word[0]);
        }

        string family = Squash(value.Particle.Text) + Squash(value.Family.Text) + Squash(value.SecondFamily.Text);
        if (family.Length > 0)
        {
            sb.Append(sb.Length > 0 ? " " : string.Empty).Append(family);
        }

        return sb.ToString();
    }

    // Folded letters and digits of every word, with no separators: "Anne-Marie" and "AnneMarie", "O'Brien" and "OBrien" agree.
    private static string Squash(string text) => NameFolding.LettersAndDigits(text);
}
