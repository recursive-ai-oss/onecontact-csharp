using System.Globalization;
using System.Text;
using OneContact.Data.AddressUsCa;

namespace OneContact.Data.AddressChallenger;

/// <summary>Counts of lines left out, by reason.</summary>
internal sealed class Rejects
{
    internal SortedDictionary<string, long> Counts { get; } = new(StringComparer.Ordinal);

    internal void Add(string reason) => Counts[reason] = Counts.GetValueOrDefault(reason) + 1;
}

/// <summary>
/// Turns one line of the published OpenStreetMap address file (<c>language TAB country TAB tokens</c>, each token written
/// <c>word/label</c>, with <c>|/FSEP</c> between fields and <c>,/SEP</c> for a comma) into a labelled example in our format.
/// The mapping of labels is in docs/training-data-address-challenger.md.
/// </summary>
internal static class Converter
{
    internal const string Version = "challenger-osm-2026-11-06.1";

    // Labels of the source that have no counterpart among the address fields (query words, informal regions): lines carrying one are left out.
    private static readonly HashSet<string> DroppedLabels = new(StringComparer.Ordinal) { "category", "near", "world_region", "country_region", "island" };

    // Source label -> our label. Unit and box labels are split into designator and number by SplitRun.
    private static readonly Dictionary<string, string> Direct = new(StringComparer.Ordinal)
    {
        ["house"] = "recipient",
        ["house_number"] = "street.house-number",
        ["road"] = "street.road",
        ["postcode"] = "postal-code",
        ["suburb"] = "dependent-locality",
        ["city_district"] = "dependent-locality.district",
        ["city"] = "locality",
        ["state"] = "admin-area",
        ["state_district"] = "admin-area.district",
        ["country"] = "country",
    };

    private static readonly HashSet<string> UnitLabels = new(StringComparer.Ordinal) { "unit", "level", "staircase", "entrance" };

    /// <summary>The mapping as table rows (source label, our label or labels), for the manifest and the docs.</summary>
    internal static IEnumerable<(string Source, string Ours)> MappingTable()
    {
        foreach (KeyValuePair<string, string> kv in Direct.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            yield return (kv.Key, kv.Value);
        }

        foreach (string u in UnitLabels.Order(StringComparer.Ordinal))
        {
            yield return (u, "street.unit-designator + street.unit-number");
        }

        yield return ("po_box", "street.box-designator + street.box-number");
        foreach (string d in DroppedLabels.Order(StringComparer.Ordinal))
        {
            yield return (d, "(line left out)");
        }
    }

    private sealed class Run
    {
        internal string Lib = string.Empty;
        internal List<string> Words = [];
        internal string SepBefore = string.Empty;
    }

    private sealed class El
    {
        internal string Word = string.Empty;
        internal int Key = -1;
        internal string SepBefore = string.Empty;
        internal bool OpenQuote;
        internal bool CloseQuote;
        internal int Start;
        internal int End;
    }

    /// <summary>The example for one line, or null with the reason counted in <paramref name="rejects"/>.</summary>
    internal static Example? Convert(string tags, string language, string country, string id, ulong seed, Rejects rejects)
    {
        List<Run> runs = [];
        Run? cur = null;
        string pending = string.Empty;
        int tokenCount = 0;
        foreach (string token in tags.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int slash = token.LastIndexOf('/');
            if (slash < 0 || slash == token.Length - 1)
            {
                rejects.Add("token-without-label");
                return null;
            }

            string word = token[..slash];
            string label = token[(slash + 1)..];
            if (label == "FSEP")
            {
                pending = "FSEP";
                cur = null;
                continue;
            }

            if (label == "SEP")
            {
                if (pending != "FSEP")
                {
                    pending = "SEP";
                }

                cur = null;
                continue;
            }

            if (word.Length == 0)
            {
                rejects.Add("empty-word");
                return null;
            }

            if (DroppedLabels.Contains(label))
            {
                rejects.Add("label:" + label);
                return null;
            }

            if (!Direct.ContainsKey(label) && !UnitLabels.Contains(label) && label != "po_box")
            {
                rejects.Add("unknown-label");
                return null;
            }

            if (word.Length > 64 || !IsLatinScript(word))
            {
                rejects.Add(word.Length > 64 ? "long-word" : "non-latin-script");
                return null;
            }

            if (++tokenCount > 40)
            {
                rejects.Add("too-many-tokens");
                return null;
            }

            if (cur == null || cur.Lib != label)
            {
                cur = new Run { Lib = label, SepBefore = runs.Count == 0 ? string.Empty : pending };
                runs.Add(cur);
                pending = string.Empty;
            }

            cur.Words.Add(word);
        }

        if (runs.Count == 0 || runs.All(r => r.Lib == "country"))
        {
            rejects.Add("no-address-field");
            return null;
        }

        // Elements in reading order; Key says which labelled span an element belongs to (-1: unlabelled punctuation).
        List<El> els = [];
        List<string> keyLabels = [];
        foreach (Run r in runs)
        {
            int lead = 0;
            while (lead < r.Words.Count && IsPunctuation(r.Words[lead]))
            {
                lead++;
            }

            int trail = r.Words.Count;
            while (trail > lead && IsPunctuation(r.Words[trail - 1]))
            {
                trail--;
            }

            // A core made only of brackets, quotes or a sign carries no field.
            if (trail > lead && !r.Words.Skip(lead).Take(trail - lead).Any(HasLetterOrDigit))
            {
                lead = trail = r.Words.Count;
            }

            bool first = true;
            void Add(string w, int key)
            {
                els.Add(new El { Word = w, Key = key, SepBefore = first ? r.SepBefore : string.Empty });
                first = false;
            }

            for (int i = 0; i < lead; i++)
            {
                Add(r.Words[i], -1);
            }

            if (trail > lead)
            {
                (string label, int count)[] parts = SplitRun(r.Lib, r.Words, lead, trail);
                int w = lead;
                foreach ((string label, int count) in parts)
                {
                    int key = keyLabels.Count;
                    keyLabels.Add(label);
                    for (int k = 0; k < count; k++)
                    {
                        Add(r.Words[w++], key);
                    }
                }
            }

            for (int i = trail; i < r.Words.Count; i++)
            {
                Add(r.Words[i], -1);
            }
        }

        // A quotation mark opens when it is the first, third, ... one of the line and closes otherwise.
        int quotes = 0;
        foreach (El e in els)
        {
            if (e.Word == "\"")
            {
                if (quotes++ % 2 == 0)
                {
                    e.OpenQuote = true;
                }
                else
                {
                    e.CloseQuote = true;
                }
            }
            else if (e.Word == "«")
            {
                e.OpenQuote = true;
            }
            else if (e.Word == "»")
            {
                e.CloseQuote = true;
            }
        }

        // The layout is drawn per example from the id: how a field separator is written is not recorded in the source.
        Prng prng = Prng.For(seed, id);
        double u = (prng.Next() >> 11) * (1.0 / 9007199254740992.0);
        string layout = u < 0.40 ? "osm.lines" : u < 0.85 ? "osm.comma" : "osm.space";

        StringBuilder sb = new();
        for (int i = 0; i < els.Count; i++)
        {
            El e = els[i];
            if (i > 0)
            {
                if (e.SepBefore.Length > 0)
                {
                    AppendSeparator(sb, e.SepBefore, layout);
                }
                else if (!Tight(els, i))
                {
                    sb.Append(' ');
                }
            }

            e.Start = sb.Length;
            sb.Append(e.Word);
            e.End = sb.Length;
        }

        string text = sb.ToString();
        if (text.Length > 300)
        {
            rejects.Add("too-long");
            return null;
        }

        List<(int Start, int End, string Label)> entities = [];
        for (int key = 0; key < keyLabels.Count; key++)
        {
            El[] members = els.Where(e => e.Key == key).ToArray();
            int start = members[0].Start;
            int end = members[^1].End;
            // An abbreviation's period or a trailing comma stays outside the span, as in the register-based sets.
            string lastWord = members[^1].Word;
            if (lastWord.Length > 1 && (lastWord[^1] == '.' || lastWord[^1] == ',') && char.IsLetterOrDigit(lastWord[^2]))
            {
                end--;
            }

            entities.Add((start, end, keyLabels[key]));
        }

        List<(int Start, int End, string Label)> fields = [];
        List<(int Start, int End, string Label)> street = entities.Where(e => e.Label.StartsWith("street.", StringComparison.Ordinal)).ToList();
        if (street.Count > 0)
        {
            fields.Add((street[0].Start, street[^1].End, "street-address"));
        }

        return new Example
        {
            Id = id,
            Country = country,
            Language = language,
            Layout = layout,
            Text = text,
            Tokens = Tokenizer.Tokenize(text, entities),
            Entities = entities,
            Fields = fields,
        };
    }

    /// <summary>Cuts a run of unit or box words into designator and number: leading words without a digit are the designator, the rest the number.</summary>
    private static (string Label, int Count)[] SplitRun(string lib, List<string> words, int from, int to)
    {
        if (Direct.TryGetValue(lib, out string? ours))
        {
            return [(ours, to - from)];
        }

        bool box = lib == "po_box";
        string designator = box ? "street.box-designator" : "street.unit-designator";
        string number = box ? "street.box-number" : "street.unit-number";
        int k = from;
        while (k < to && !HasDigit(words[k]))
        {
            k++;
        }

        if (k == from)
        {
            return [(number, to - from)];
        }

        if (k == to)
        {
            // No digit anywhere: a box keeps its words as a designator ("Postfach"); a unit's words ("Ground Floor", "Rez-de-chaussée") are its number.
            return [(box ? designator : number, to - from)];
        }

        // "# 12": the sign is the designator; a lone leading "#" is not a letter or digit, so it is handled by the same rule.
        return [(designator, k - from), (number, to - k)];
    }

    private static void AppendSeparator(StringBuilder sb, string sep, string layout)
    {
        switch (layout)
        {
            case "osm.lines":
                sb.Append(sep == "FSEP" ? "\n" : (sb.Length > 0 && sb[^1] == ',' ? " " : ", "));
                break;
            case "osm.comma":
                sb.Append(sb.Length > 0 && sb[^1] == ',' ? " " : ", ");
                break;
            default:
                sb.Append(' ');
                break;
        }
    }

    // Whether element i follows element i-1 with no space. Connectors join only between the kinds of characters they join in running
    // text; everything else is spaced, which is how the tokens were most likely written.
    private static bool Tight(List<El> els, int i)
    {
        El p = els[i - 1];
        El n = els[i];
        string a = p.Word;
        string b = n.Word;
        if (n.CloseQuote || p.OpenQuote)
        {
            return true;
        }

        if (n.OpenQuote || p.CloseQuote)
        {
            return false;
        }

        if (b is "." or "," or ";" or ":" or ")" or "]" or "!" or "?")
        {
            return true;
        }

        if (a is "(" or "[")
        {
            return true;
        }

        if (a == "#")
        {
            return char.IsLetterOrDigit(b[0]);
        }

        if (b is "/" or "-" or "'")
        {
            return Joined(els, i);
        }

        if (a is "/" or "-" or "'")
        {
            return Joined(els, i - 1);
        }

        return false;
    }

    // Whether the connector at index c has the characters it joins on both sides: "/" between letters or digits, "-" between digits, "'" between letters.
    private static bool Joined(List<El> els, int c)
    {
        if (c == 0 || c + 1 >= els.Count)
        {
            return false;
        }

        string left = els[c - 1].Word;
        string right = els[c + 1].Word;
        if (els[c].SepBefore.Length > 0 || els[c + 1].SepBefore.Length > 0)
        {
            return false;
        }

        return els[c].Word switch
        {
            "/" => char.IsLetterOrDigit(left[^1]) && char.IsLetterOrDigit(right[0]),
            "-" => char.IsDigit(left[^1]) && char.IsDigit(right[0]),
            _ => char.IsLetter(left[^1]) && char.IsLetter(right[0]),
        };
    }

    private static bool HasLetterOrDigit(string w)
    {
        foreach (char c in w)
        {
            if (char.IsLetterOrDigit(c))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDigit(string w)
    {
        foreach (char c in w)
        {
            if (char.IsDigit(c))
            {
                return true;
            }
        }

        return false;
    }

    // A word with no letter or digit; "#" counts as content because it is a unit designator, and brackets and quotation marks stay inside the span they open or close.
    private static bool IsPunctuation(string w)
    {
        foreach (char c in w)
        {
            if (char.IsLetterOrDigit(c) || c is '#' or '(' or ')' or '[' or ']' or '"' or '«' or '»')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Latin letters and digits only: ASCII, Latin-1 to Latin Extended-B, Latin Extended Additional, combining marks, general punctuation and the numero sign.</summary>
    private static bool IsLatinScript(string w)
    {
        foreach (char c in w)
        {
            if (c < 0x80)
            {
                if (c < 0x20)
                {
                    return false;
                }

                continue;
            }

            bool ok = c <= 0x24F || (c >= 0x300 && c <= 0x36F) || (c >= 0x1E00 && c <= 0x1EFF) || (c >= 0x2000 && c <= 0x206F) || c == 0x2116;
            if (!ok)
            {
                return false;
            }

            if (c <= 0x24F && c >= 0x80 && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Control)
            {
                return false;
            }

            if (char.IsDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
