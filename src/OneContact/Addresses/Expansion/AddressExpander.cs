using System.Globalization;
using System.Text;

namespace RecursiveAI.OneContact.Addresses.Expansion;

/// <summary>
/// Expands an address string into the spellings it may be written in, for matching: abbreviations become full words (<c>st</c> to <c>street</c>, <c>w</c> to
/// <c>west</c>, <c>apt</c> to <c>apartment</c>), ordinals lose their ending, and words the dictionaries mark ambiguous (a single letter may be an initial or an
/// alphabet street) are also kept as written. Driven by the libpostal dictionaries for English and French (ingested from pinned data files) and the
/// two guides' abbreviation tables.
/// </summary>
/// <remarks>
/// <para>The first variant is the one with every phrase expanded; the last is the input normalized but not expanded. Variants are distinct, in a fixed order, and at most
/// the requested count. Case is folded with the invariant culture; punctuation other than a hyphen between digits becomes a space.</para>
/// <para>Nothing is added that the input lacks: expansion only rewrites words the dictionaries know.</para>
/// </remarks>
public static class AddressExpander
{
    private static readonly (ExpansionLanguages Flag, string Code)[] LaterLanguages =
    [
        (ExpansionLanguages.Spanish, "es"),
        (ExpansionLanguages.Portuguese, "pt"),
        (ExpansionLanguages.Catalan, "ca"),
        (ExpansionLanguages.Basque, "eu"),
        (ExpansionLanguages.Galician, "gl"),
        (ExpansionLanguages.Maltese, "mt"),
        (ExpansionLanguages.Irish, "ga"),
        (ExpansionLanguages.Danish, "da"),
        (ExpansionLanguages.Swedish, "sv"),
        (ExpansionLanguages.Finnish, "fi"),
        (ExpansionLanguages.Norwegian, "nb"),
        (ExpansionLanguages.Icelandic, "is"),
        (ExpansionLanguages.Estonian, "et"),
        (ExpansionLanguages.Latvian, "lv"),
        (ExpansionLanguages.Lithuanian, "lt"),
        (ExpansionLanguages.Polish, "pl"),
        (ExpansionLanguages.Czech, "cs"),
        (ExpansionLanguages.Slovak, "sk"),
        (ExpansionLanguages.Slovenian, "sl"),
        (ExpansionLanguages.Croatian, "hr"),
        (ExpansionLanguages.Hungarian, "hu"),
        (ExpansionLanguages.Romanian, "ro"),
    ];

    /// <summary>Expands an address.</summary>
    /// <param name="address">The address text, one line or several.</param>
    /// <param name="languages">The dictionaries to use; both by default.</param>
    /// <param name="maxVariants">The most variants to return (at least 1, default 16).</param>
    /// <returns>The variants, the fully expanded one first. Empty for blank input.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxVariants"/> is less than 1.</exception>
    public static IReadOnlyList<string> Expand(string address, ExpansionLanguages languages = ExpansionLanguages.English | ExpansionLanguages.French, int maxVariants = 16)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxVariants, 1);
        List<string> words = Normalize(address);
        if (words.Count == 0)
        {
            return [];
        }

        List<string> codes = [];
        if (languages.HasFlag(ExpansionLanguages.English))
        {
            codes.Add("en");
        }

        if (languages.HasFlag(ExpansionLanguages.French))
        {
            codes.Add("fr");
        }

        if (languages.HasFlag(ExpansionLanguages.German))
        {
            codes.Add("de");
        }

        if (languages.HasFlag(ExpansionLanguages.Dutch))
        {
            codes.Add("nl");
        }

        if (languages.HasFlag(ExpansionLanguages.Italian))
        {
            codes.Add("it");
        }

        if (languages.HasFlag(ExpansionLanguages.Luxembourgish))
        {
            codes.Add("lb");
        }

        foreach ((ExpansionLanguages flag, string code) in LaterLanguages)
        {
            if (languages.HasFlag(flag))
            {
                codes.Add(code);
            }
        }

        // Each slot is a stretch of words with its alternatives; the first alternative is the expanded one.
        List<List<string>> slots = [];
        int i = 0;
        while (i < words.Count)
        {
            (int length, List<string> alternatives)? match = MatchAt(words, i, codes);
            if (match is { } m)
            {
                slots.Add(m.alternatives);
                i += m.length;
            }
            else
            {
                // German and Dutch street names end in the type (Hauptstr is Hauptstrasse): the full ending is the first alternative, the word as written the second.
                string? suffixed = ExpandEnding(words[i], codes);
                slots.Add(suffixed is null ? Ordinal(words[i]) : [suffixed, words[i]]);
                i++;
            }
        }

        List<string> result = [];
        void Add(string s)
        {
            if (s.Length > 0 && !result.Contains(s) && result.Count < maxVariants)
            {
                result.Add(s);
            }
        }

        Add(string.Join(' ', slots.Select(s => s[0])));
        Add(string.Join(' ', words));
        foreach (string combined in Product(slots, maxVariants))
        {
            Add(combined);
        }

        // The all-expanded form first, the unexpanded form last.
        string plain = string.Join(' ', words);
        if (result.Remove(plain))
        {
            result.Add(plain);
        }

        return result;
    }

    /// <summary>Replaces an abbreviated ending of a German or Dutch name word (<c>hauptstr</c>) by the full ending (<c>hauptstrasse</c>).</summary>
    /// <param name="word">The word, lower case.</param>
    /// <param name="codes">The language codes in use.</param>
    /// <returns>The word with its full ending, or <see langword="null"/> when no known ending abbreviation ends the word.</returns>
    internal static string? ExpandEnding(string word, List<string> codes)
    {
        foreach (string code in codes)
        {
            if (code is not ("de" or "nl"))
            {
                continue;
            }

            string folded = Fold(word);
            (string Abbreviation, string Full)? best = null;
            foreach ((string abbreviation, string full) in ExpansionDictionaries.Suffixes(code))
            {
                if (folded.Length > abbreviation.Length && folded.EndsWith(abbreviation, StringComparison.Ordinal) && (best is null || abbreviation.Length > best.Value.Abbreviation.Length))
                {
                    best = (abbreviation, full);
                }
            }

            if (best is { } b)
            {
                return folded[..^b.Abbreviation.Length] + Fold(b.Full);
            }
        }

        return null;
    }

    /// <summary>Normalizes text the way expansion does: lower case (invariant), punctuation to spaces, hyphens between letters to spaces, one space between words.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The words.</returns>
    internal static List<string> Normalize(string text)
    {
        StringBuilder sb = new(text.Length);
        string s = text.ToLowerInvariant();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsLetterOrDigit(c) || c == '\'' || c == '/' || c == '&' || c == '#' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
            else if (c == '-' && i > 0 && i < s.Length - 1 && char.IsAsciiDigit(s[i - 1]) && char.IsAsciiDigit(s[i + 1]))
            {
                sb.Append(c);
            }
            else if (c == '.' && i > 0 && i < s.Length - 1 && char.IsAsciiDigit(s[i - 1]) && char.IsAsciiDigit(s[i + 1]))
            {
                sb.Append(c);
            }
            else if (c == '.' || c == '\u2019')
            {
                // A period inside an abbreviation (p.o., n.e.) just vanishes; a typographic apostrophe is an apostrophe.
                if (c == '\u2019')
                {
                    sb.Append('\'');
                }
            }
            else
            {
                sb.Append(' ');
            }
        }

        List<string> words = [];
        foreach (string w in sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string word = w;
            if (word.Length > 1 && word[0] == '#')
            {
                words.Add("#");
                word = word[1..];
            }

            // French elisions (l', d') stay attached for matching names but are split off for the dictionaries.
            int apostrophe = word.IndexOf('\'', StringComparison.Ordinal);
            if (apostrophe > 0 && apostrophe < word.Length - 1 && ExpansionDictionaries.Elisions.Contains(word[..(apostrophe + 1)]))
            {
                words.Add(word[..(apostrophe + 1)]);
                word = word[(apostrophe + 1)..];
            }

            words.Add(word);
        }

        return words;
    }

    private static (int length, List<string> alternatives)? MatchAt(List<string> words, int i, List<string> codes)
    {
        int max = Math.Min(ExpansionDictionaries.MaxWords, words.Count - i);
        for (int length = max; length >= 1; length--)
        {
            string phrase = string.Join(' ', words.Skip(i).Take(length));
            List<string> alternatives = [];
            foreach (string code in codes)
            {
                foreach (PhraseEntry entry in ExpansionDictionaries.Find(code, phrase))
                {
                    if (!Applies(entry, words, i, length, phrase))
                    {
                        continue;
                    }

                    string canonical = entry.Phrases[0];
                    if (!alternatives.Contains(canonical))
                    {
                        alternatives.Add(canonical);
                    }
                }
            }

            if (alternatives.Count == 0)
            {
                continue;
            }

            // A word the dictionaries call ambiguous (a bare letter, a short word that is also a name) is also kept as written.
            if (ExpansionDictionaries.Ambiguous.Contains(phrase) || phrase.Length == 1 || codes.Any(c => ExpansionDictionaries.AmbiguousIn(c, phrase)))
            {
                alternatives.Add(phrase);
            }

            return (length, alternatives);
        }

        return null;
    }

    private static bool Applies(PhraseEntry entry, List<string> words, int i, int length, string phrase)
    {
        bool hasNext = i + length < words.Count;
        string next = hasNext ? words[i + length] : string.Empty;
        bool nextLooksNumeric = next.Length > 0 && (next.Any(char.IsAsciiDigit) || next.Length == 1);
        switch (entry.Kind)
        {
            case PhraseKind.StreetType:
                // A street type is not the first word of an English address ("st marys church" is a saint); French, Italian, Iberian and Maltese types lead the name.
                return i > 0 || entry.Language is "fr" or "it" or "es" or "pt" or "ca" or "eu" or "gl" or "mt" or "pl" or "cs" or "sk" or "sl" or "hr" or "ro" && words.Count > length;
            case PhraseKind.NumberedUnit:
            case PhraseKind.NumberWord:
                return nextLooksNumeric;
            case PhraseKind.StreetName:
            case PhraseKind.Synonym:
                return entry.Phrases[0] != phrase;
            default:
                return true;
        }
    }

    private static List<string> Ordinal(string word)
    {
        // 26th and 1re become 26 and 1: the digits are the form street indexes agree on.
        int d = 0;
        while (d < word.Length && char.IsAsciiDigit(word[d]))
        {
            d++;
        }

        if (d > 0 && d < word.Length)
        {
            string tail = word[d..];
            if (tail is "st" or "nd" or "rd" or "th" or "e" or "er" or "re" or "ere" or "ère" or "eme" or "ème" or "ième" or "ieme")
            {
                return [word[..d], word];
            }
        }

        return [word];
    }

    private static List<string> Product(List<List<string>> slots, int limit)
    {
        List<string> acc = [string.Empty];
        foreach (List<string> slot in slots)
        {
            List<string> next = [];
            foreach (string prefix in acc)
            {
                foreach (string alt in slot)
                {
                    next.Add(prefix.Length == 0 ? alt : prefix + " " + alt);
                    if (next.Count >= limit * 4)
                    {
                        break;
                    }
                }

                if (next.Count >= limit * 4)
                {
                    break;
                }
            }

            acc = next;
        }

        return acc;
    }

    /// <summary>Gets the canonical word for a street-name word: <c>st</c> is <c>saint</c>, <c>mt</c> is <c>mount</c>, <c>ft</c> is <c>fort</c> and so on, from the dictionaries' street name and synonym lists.</summary>
    /// <param name="word">The word, lower case.</param>
    /// <returns>The canonical word, or the word itself when the dictionaries do not know it.</returns>
    internal static string CanonicalNameWord(string word)
    {
        foreach (string code in new[] { "en", "fr" })
        {
            foreach (PhraseEntry entry in ExpansionDictionaries.Find(code, word))
            {
                if (entry.Kind is PhraseKind.StreetName or PhraseKind.Synonym)
                {
                    return entry.Phrases[0];
                }
            }
        }

        return word;
    }

    /// <summary>Gets the full word of a street type (<c>st</c> is <c>street</c>) or directional (<c>nw</c> is <c>northwest</c>) in a language.</summary>
    /// <param name="language">The language code.</param>
    /// <param name="word">The word, lower case.</param>
    /// <param name="kind">The kind of phrase to look for.</param>
    /// <returns>The canonical phrase, or the word itself.</returns>
    internal static string CanonicalPhrase(string language, string word, PhraseKind kind)
    {
        foreach (PhraseEntry entry in ExpansionDictionaries.Find(language, word))
        {
            if (entry.Kind == kind)
            {
                return entry.Phrases[0];
            }
        }

        return word;
    }

    private const string FoldFrom = "àáâãäåçèéêëìíîïñòóôõöøùúûüýÿāăąćĉċčďđēĕėęěĝğġģĥħĩīĭįıĵķĸĺļľŀłńņňŉōŏőŕŗřśŝşšţťŧũūŭůűųŵŷźżžſ";
    private const string FoldTo = "aaaaaaceeeeiiiinoooooouuuuyyaaaccccddeeeeegggghhiiiiijkklllllnnnnooorrrsssstttuuuuuuwyzzzs";

    /// <summary>
    /// Folds case with the invariant culture and removes diacritics from Latin letters: the accented letters of the tier languages (and their decomposed
    /// forms, base letter plus combining mark) become their base letters, <c>ß</c> becomes <c>ss</c>, <c>æ</c> and <c>œ</c> become two letters. The table is ours, so the result
    /// does not depend on the platform's normalization support.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The folded text.</returns>
    internal static string Fold(string text)
    {
        StringBuilder sb = new(text.Length);
        foreach (char raw in text.ToLowerInvariant())
        {
            if (raw is >= '\u0300' and <= '\u036f')
            {
                continue;
            }

            int i = raw < '\u00c0' ? -1 : FoldFrom.IndexOf(raw, StringComparison.Ordinal);
            if (i >= 0)
            {
                sb.Append(FoldTo[i]);
            }
            else if (raw == 'ß')
            {
                sb.Append("ss");
            }
            else if (raw == 'æ')
            {
                sb.Append("ae");
            }
            else if (raw == 'œ')
            {
                sb.Append("oe");
            }
            else
            {
                sb.Append(raw);
            }
        }

        return sb.ToString();
    }
}
