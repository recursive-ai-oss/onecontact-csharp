using System.Text;
using RecursiveAI.OneContact.Names;

namespace RecursiveAI.OneContact.Companies;

/// <summary>Finds the legal form at the end (or, for a few forms, the start) of a company name and resolves it against the ELF code list.</summary>
internal static class LegalFormMatcher
{
    private const int MaxFormTokens = 10;

    // Forms that stand before the name in the jurisdictions that write them so, and are never anything else.
    private static readonly HashSet<string> AlwaysPrefix = new(StringComparer.Ordinal) { "uab", "sia", "zao", "oao", "pao", "tov" };

    // English forms that fit many codes (every US state has its own) are written long or short; the key joins the two spellings.
    private static readonly Dictionary<string, string> KeySynonyms = new(StringComparer.Ordinal)
    {
        ["incorporated"] = "inc",
        ["incorporation"] = "inc",
        ["corporation"] = "corp",
        ["limited"] = "ltd",
        ["company"] = "co",
        ["limitedliabilitycompany"] = "llc",
        ["limitedliabilitypartnership"] = "llp",
        ["publiclimitedcompany"] = "plc",
    };

    // French legal forms are also the forms of these overseas territories (the list says so in its notes).
    private static readonly string[] FrenchOverseas = ["GF", "GP", "MQ", "YT", "RE", "BL", "MF"];

    /// <summary>A form found in the token list: the tokens it covers and the rows its text matches.</summary>
    internal readonly record struct Hit(int FirstToken, int LastToken, int[] Rows, bool IsPrefix, string Folded);

    /// <summary>Looks for a legal form in <paramref name="tokens"/>; at least one token of name must remain.</summary>
    internal static Hit? Find(ReadOnlySpan<char> whole, IReadOnlyList<NameToken> tokens, NameHints hints)
    {
        ElfTable table = ElfTable.Instance;
        int n = tokens.Count;
        if (n < 2)
        {
            return null;
        }

        // Tail: the longest window that is a listed form wins ("GmbH & Co. KG" before "KG").
        int maxTokens = Math.Min(n - 1, MaxFormTokens);
        for (int k = maxTokens; k >= 1; k--)
        {
            int first = n - k;
            if (!tokens[first].HasLetterOrDigit(whole) || !HasName(whole, tokens, 0, first))
            {
                continue;
            }

            string folded = Fold(whole, tokens, first, n);
            if (folded.Length > table.MaxVariantLength || !table.Variants.TryGetValue(folded, out int[]? rows))
            {
                continue;
            }

            if (!ShortFormAllowed(whole, tokens, first, n, folded, rows, hints))
            {
                continue;
            }

            return new Hit(first, n - 1, rows, false, folded);
        }

        // Prefix: "AB Volvo", "SIA Rota". Only forms of the hinted country, or forms that are never anything else.
        int maxPrefix = Math.Min(n - 1, 6);
        for (int k = maxPrefix; k >= 1; k--)
        {
            if (!tokens[k - 1].HasLetterOrDigit(whole) || !HasName(whole, tokens, k, n))
            {
                continue;
            }

            string folded = Fold(whole, tokens, 0, k);
            if (folded.Length > table.MaxVariantLength || !table.Abbreviations.TryGetValue(folded, out int[]? rows))
            {
                continue;
            }

            if (!AlwaysPrefix.Contains(folded))
            {
                int[] inCountry = InCountry(rows, hints);
                if (inCountry.Length == 0)
                {
                    continue;
                }

                rows = inCountry;
            }

            if (!ShortFormAllowed(whole, tokens, 0, k, folded, rows, hints))
            {
                continue;
            }

            return new Hit(0, k - 1, rows, true, folded);
        }

        return null;
    }

    /// <summary>Builds the public legal form from a hit.</summary>
    internal static CompanyLegalForm Resolve(ReadOnlySpan<char> whole, IReadOnlyList<NameToken> tokens, Hit hit, NameHints hints, out bool outsideCountry, out bool inactiveOnly)
    {
        ElfTable table = ElfTable.Instance;
        int start = tokens[hit.FirstToken].Start;
        int end = tokens[hit.LastToken].End;
        string text = whole[start..end].ToString();

        // Key token: no hint is used, so the key is the same wherever the name is read.
        List<string> globalCodes = DistinctCodes(table, PreferActive(table, hit.Rows));
        string keyToken;
        string? globalCommon = null;
        if (globalCodes.Count == 1)
        {
            keyToken = globalCodes[0];
        }
        else
        {
            globalCommon = CommonAbbreviation(table, globalCodes, hit.Rows);
            string common = NameFolding.LettersAndDigits(globalCommon);
            string token = common.Length > 0 ? common : hit.Folded;
            keyToken = "~" + (KeySynonyms.TryGetValue(token, out string? shorter) ? shorter : token);
        }

        // Candidates: the hinted country's rows when it has any, otherwise all.
        int[] rows = hit.Rows;
        outsideCountry = false;
        if (!hints.Country.IsUnknown)
        {
            int[] inCountry = InCountry(rows, hints);
            if (inCountry.Length > 0)
            {
                rows = inCountry;
            }
            else
            {
                outsideCountry = true;
            }
        }

        int[] active = PreferActive(table, rows);
        inactiveOnly = active.Length > 0 && !table.Rows[active[0]].IsActive;
        List<string> codes = DistinctCodes(table, active);
        // The first of the active rows that carries each code, found in one pass (codes are sorted).
        int[] firstRow = new int[codes.Count];
        Array.Fill(firstRow, -1);
        foreach (int i in active)
        {
            int at = codes.BinarySearch(table.Rows[i].Code, StringComparer.Ordinal);
            if (at >= 0 && firstRow[at] < 0)
            {
                firstRow[at] = i;
            }
        }

        List<ElfCandidate> candidates = new(codes.Count);
        for (int c = 0; c < codes.Count; c++)
        {
            ElfRow row = table.Rows[firstRow[c] >= 0 ? firstRow[c] : table.ByCode[codes[c]][0]];
            candidates.Add(new ElfCandidate(codes[c], row.Country, row.Name, row.IsActive));
        }

        string elf = codes.Count == 1 ? codes[0] : string.Empty;

        // With no country hint the codes are the ones the key already worked from: the common abbreviation is the same text.
        string abbreviation = codes.Count == 1
            ? CanonicalAbbreviation(table, codes[0], hit.Rows)
            : globalCommon is not null && codes.SequenceEqual(globalCodes, StringComparer.Ordinal) ? globalCommon : CommonAbbreviation(table, codes, hit.Rows);

        // A composite form ("GmbH & Co. KG") is one legal form written with its partners; its canonical abbreviation would drop words.
        if (text.Contains('&') || text.Contains(" und ", StringComparison.OrdinalIgnoreCase) || text.Contains(" and ", StringComparison.OrdinalIgnoreCase))
        {
            abbreviation = string.Empty;
        }

        return new CompanyLegalForm(text, new SourceSpan(start, end - start), elf, abbreviation, candidates, hit.IsPrefix) { KeyToken = keyToken };
    }

    /// <summary>
    /// The canonical abbreviation of an ELF code, from the rows that carry the written form: of their abbreviations the one with the
    /// fewest letters; among equals mixed case over all capitals when it is longer than three letters ("Pty Ltd" over "PTY LTD"), then
    /// fewer dots and spaces, then a capital initial ("Oy" over "oy"), then the first listed.
    /// </summary>
    internal static string CanonicalAbbreviation(ElfTable table, string code, int[] matchedRows)
    {
        // The rows that carry the written form decide the language of the abbreviation ("Cwmni Cyfyngedig Cyhoeddus" gives the Welsh one).
        List<int> rows = [];
        foreach (int i in table.ByCode[code])
        {
            if (Array.IndexOf(matchedRows, i) >= 0)
            {
                rows.Add(i);
            }
        }

        if (rows.Count == 0)
        {
            rows.AddRange(table.ByCode[code]);
        }

        string best = string.Empty;
        (int Letters, int Upper, int Punctuation, int LowerInitial) bestScore = (int.MaxValue, 0, 0, 0);
        foreach (int i in rows)
        {
            ElfRow row = table.Rows[i];
            string[] pool = row.Abbreviations.Length > 0 && HasLatinLetter(row.Abbreviations[0]) ? row.Abbreviations : row.TransliteratedAbbreviations;
            foreach (string abbr in pool)
            {
                int letters = NameFolding.LettersAndDigits(abbr).Length;
                if (letters == 0)
                {
                    continue;
                }

                int punctuation = 0;
                int initial = 0;
                bool seenLetter = false;
                foreach (char c in abbr)
                {
                    if (!char.IsLetterOrDigit(c))
                    {
                        punctuation++;
                    }
                    else if (!seenLetter)
                    {
                        seenLetter = true;
                        initial = char.IsLower(c) ? 1 : 0;
                    }
                }

                (int, int, int, int) score = (letters, letters > 3 && NameFolding.IsAllUpper(abbr) ? 1 : 0, punctuation, initial);
                if (score.CompareTo(bestScore) < 0)
                {
                    best = abbr;
                    bestScore = score;
                }
            }

            if (best.Length > 0)
            {
                break;
            }
        }

        return best;
    }

    private static string CommonAbbreviation(ElfTable table, List<string> codes, int[] matchedRows)
    {
        string first = string.Empty;
        string firstFold = string.Empty;
        foreach (string code in codes)
        {
            string abbr = CanonicalAbbreviation(table, code, matchedRows);
            string fold = NameFolding.LettersAndDigits(abbr);
            if (fold.Length == 0)
            {
                return string.Empty;
            }

            if (first.Length == 0)
            {
                first = abbr;
                firstFold = fold;
            }
            else if (!string.Equals(fold, firstFold, StringComparison.Ordinal))
            {
                return string.Empty;
            }
        }

        return first;
    }

    private static bool HasLatinLetter(string s)
    {
        foreach (char c in s)
        {
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '\u00C0' and <= '\u024F')
            {
                return true;
            }
        }

        return false;
    }

    private static int[] PreferActive(ElfTable table, int[] rows)
    {
        List<int> active = [];
        foreach (int i in rows)
        {
            if (table.Rows[i].IsActive)
            {
                active.Add(i);
            }
        }

        return active.Count > 0 ? [.. active] : rows;
    }

    private static List<string> DistinctCodes(ElfTable table, int[] rows)
    {
        List<string> codes = new(rows.Length);
        foreach (int i in rows)
        {
            codes.Add(table.Rows[i].Code);
        }

        codes.Sort(StringComparer.Ordinal);

        // Sorted, so the repeats sit together.
        int kept = 0;
        for (int i = 0; i < codes.Count; i++)
        {
            if (i == 0 || !string.Equals(codes[i], codes[kept - 1], StringComparison.Ordinal))
            {
                codes[kept++] = codes[i];
            }
        }

        codes.RemoveRange(kept, codes.Count - kept);
        return codes;
    }

    private static int[] InCountry(int[] rows, NameHints hints)
    {
        if (hints.Country.IsUnknown)
        {
            return [];
        }

        ElfTable table = ElfTable.Instance;
        string country = hints.Country.Code;
        bool overseas = Array.IndexOf(FrenchOverseas, country) >= 0;
        List<int> kept = [];
        foreach (int i in rows)
        {
            string c = table.Rows[i].Country;
            if (string.Equals(c, country, StringComparison.Ordinal) || (overseas && c == "FR"))
            {
                kept.Add(i);
            }
        }

        return [.. kept];
    }

    // Two-letter forms ("AB", "AS", "SA") are also words and initials: they count only when written in capitals or with dots,
    // or when the country hint says the jurisdiction uses them (a Finnish "Oy").
    private static bool ShortFormAllowed(ReadOnlySpan<char> whole, IReadOnlyList<NameToken> tokens, int first, int endExclusive, string folded, int[] rows, NameHints hints)
    {
        if (folded.Length > 2)
        {
            return true;
        }

        if (InCountry(rows, hints).Length > 0)
        {
            return true;
        }

        // Letters separated by dots or a slash ("S.A.", "A/S"); a word with a stray dot or slash ("Bo/") is not one.
        for (int i = first; i < endExclusive; i++)
        {
            if (IsDottedAbbreviation(tokens[i].Text(whole)))
            {
                return true;
            }
        }

        return NameFolding.IsAllUpper(whole[tokens[first].Start..tokens[endExclusive - 1].End]);
    }

    private static bool IsDottedAbbreviation(ReadOnlySpan<char> token)
    {
        ReadOnlySpan<char> t = token.TrimEnd(".,;:");
        if (t.Length < 3)
        {
            return false;
        }

        for (int i = 0; i < t.Length; i++)
        {
            bool letter = char.IsLetter(t[i]);
            if (i % 2 == 0 ? !letter : t[i] is not ('.' or '/'))
            {
                return false;
            }
        }

        return t.Length % 2 == 1;
    }

    private static bool HasName(ReadOnlySpan<char> whole, IReadOnlyList<NameToken> tokens, int from, int to)
    {
        for (int i = from; i < to; i++)
        {
            if (tokens[i].HasLetterOrDigit(whole))
            {
                return true;
            }
        }

        return false;
    }

    private static string Fold(ReadOnlySpan<char> whole, IReadOnlyList<NameToken> tokens, int from, int to)
    {
        StringBuilder sb = new();
        for (int i = from; i < to; i++)
        {
            foreach (char c in tokens[i].Text(whole))
            {
                if (char.IsLetterOrDigit(c) || c is >= '\u0300' and <= '\u036F')
                {
                    NameFolding.AppendFolded(sb, c);
                }
            }
        }

        return sb.ToString();
    }
}
