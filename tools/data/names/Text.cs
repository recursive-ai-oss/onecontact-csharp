using System.Text;

namespace OneContact.Data.Names;

/// <summary>Small text helpers: folding for keys, name casing and character-by-character case changes that never change a string's length.</summary>
internal static class Txt
{
    /// <summary>Lower-case letters and digits only: the form used for de-duplication, split buckets and text hashes.</summary>
    internal static string Fold(string s)
    {
        StringBuilder sb = new(s.Length);
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    /// <summary>Capitalizes a name written in capitals: each word, hyphen part and apostrophe part, and "Mc" followed by a letter.</summary>
    internal static string NameCase(string s)
    {
        StringBuilder sb = new(s.Length);
        bool up = true;
        foreach (char c in s)
        {
            if (char.IsLetter(c))
            {
                sb.Append(up ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
                up = false;
            }
            else
            {
                sb.Append(c);
                up = c is ' ' or '-' or '\'' or '\u2019';
            }
        }

        if (sb.Length > 2 && sb[0] == 'M' && sb[1] == 'c' && char.IsLetter(sb[2]))
        {
            sb[2] = char.ToUpperInvariant(sb[2]);
        }

        return sb.ToString();
    }

    /// <summary>Upper-cases or lower-cases every character on its own, so offsets into the string stay valid.</summary>
    internal static string MapChars(string s, bool upper)
    {
        return string.Create(s.Length, (s, upper), static (span, state) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                char c = state.s[i];
                span[i] = state.upper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);
            }
        });
    }

    internal static bool IsAllUpper(string s)
    {
        bool any = false;
        foreach (char c in s)
        {
            if (char.IsLower(c))
            {
                return false;
            }

            any |= char.IsUpper(c);
        }

        return any;
    }

    /// <summary>True for text in the Latin script blocks (up to U+024F) plus common punctuation: the scope of these training sets.</summary>
    internal static bool IsLatinText(string s)
    {
        foreach (char c in s)
        {
            if (c > '\u024F' && c is not ('\u2019' or '\u2018' or '\u2013' or '\u2014' or '\u201C' or '\u201D'))
            {
                return false;
            }

            if (char.IsControl(c))
            {
                return false;
            }
        }

        return true;
    }

    private const string Accented = "àâäáãåçéèêëíìîïñóòôöõúùûüýÿÀÂÄÁÃÅÇÉÈÊËÍÌÎÏÑÓÒÔÖÕÚÙÛÜÝ";
    private const string Plain = "aaaaaaceeeeiiiinooooouuuuyyAAAAAACEEEEIIIINOOOOOUUUUY";

    /// <summary>Replaces French and Spanish accented letters by their plain letters, one for one.</summary>
    internal static string StripAccents(string s)
    {
        return string.Create(s.Length, s, static (span, src) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                int k = Accented.IndexOf(src[i], StringComparison.Ordinal);
                span[i] = k >= 0 ? Plain[k] : src[i];
            }
        });
    }

    internal static ulong Hash64(string s) => Prng.Hash(s, 0x6F6E65636F6E7431UL);

    /// <summary>A bucket 0..99 for a key: below 90 trains, 90..94 is dev, 95..99 is reserved and never rendered.</summary>
    internal static int Bucket(string foldedKey) => (int)(Prng.Hash(foldedKey, 0x73706C6974303031UL) % 100UL);
}
