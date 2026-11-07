using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Streets;

// usage: eval-refusal <set label> <gold.jsonl> <diagnosis output> [more <label> <gold> pairs ...]
//
// For each item of an address evaluation set the rules read the text twice, once with the item's country as the hint and once without one. The model
// packages are not referenced, so nothing else decides. Each read is put in one outcome:
//   refused               no reading was returned; the reason id is counted
//   read, no country      fields returned, country unknown (flagged CountryUndetermined)
//   read, right country   the country the reader settled on is the item's
//   read, wrong country   the country the reader settled on is another one
// Every reading is then checked for invented content: each field's text must equal the input at its span, and every word of the postal and display
// renderings must be traceable to the input (see Traceable). Only counts go to the console. The diagnosis file lists, for wrong-country and invented
// cases only, the item id, the country, the label and the offending word; it stays where the evaluation set is.
if (args.Length < 3 || (args.Length - 1) % 2 != 0)
{
    Console.Error.WriteLine("usage: eval-refusal <label> <gold.jsonl> <diagnosis.tsv> [<label> <gold.jsonl> ...]");
    return 64;
}

using StreamWriter diag = new(args[^1], false, new UTF8Encoding(false));
Console.WriteLine("set\tmode\titems\trefused\tno_country\tright_country\toverseas_dept\twrong_country\tinvented_items\toffset_mismatch_items\treasons");
for (int a = 0; a + 1 < args.Length - 1; a += 2)
{
    string label = args[a];
    List<(string Id, string Country, string Text)> items = [];
    foreach (string line in File.ReadLines(args[a + 1]))
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement r = doc.RootElement;
        items.Add((r.GetProperty("id").GetString()!, r.GetProperty("country").GetString()!, r.GetProperty("text").GetString()!));
    }

    foreach (bool hinted in new[] { true, false })
    {
        int overseas = 0, refused = 0, noCountry = 0, right = 0, wrong = 0, invented = 0, offsets = 0;
        SortedDictionary<string, int> reasons = new(StringComparer.Ordinal);
        foreach ((string id, string country, string text) in items)
        {
            Region hint = hinted ? Region.Parse(country) : Region.Unknown;
            OperationStatus status = RuleAddressParser.TryParse(text, hint, out ParsedAddress? parsed, out ParseDetail detail);
            if (status != OperationStatus.Done || parsed is null)
            {
                refused++;
                string reason = detail.PrimaryReason.Id;
                reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
                continue;
            }

            if (parsed.Country == Region.Unknown)
            {
                noCountry++;
            }
            else if (parsed.Country.ToString() == country)
            {
                right++;
            }
            else if (country == "FR" && parsed.Country.ToString() is "GP" or "MQ" or "GF" or "RE" or "YT")
            {
                // La Poste serves the overseas departments; their own ISO codes are the same postal territory as the item's FR.
                overseas++;
            }
            else
            {
                wrong++;
                diag.WriteLine($"{label}\t{(hinted ? "hint" : "nohint")}\t{id}\twrong-country\tgold {country}\tread {parsed.Country}\tinput: {text.ReplaceLineEndings(" / ")}\tread: {parsed.ToString(AddressStyle.Display, AddressLayout.SingleLine)}");
            }

            List<string> problems = Invented(text, parsed);
            List<string> offsetProblems = [.. problems.Where(p => p.StartsWith("offset:", StringComparison.Ordinal))];
            if (problems.Count > offsetProblems.Count)
            {
                invented++;
                foreach (string p in problems.Where(p => !p.StartsWith("offset:", StringComparison.Ordinal)))
                {
                    diag.WriteLine($"{label}\t{(hinted ? "hint" : "nohint")}\t{id}\tinvented\t{country}\t{p}\tinput: {text.ReplaceLineEndings(" / ")}\tread: {parsed.ToString(AddressStyle.Display, AddressLayout.SingleLine)}");
                }
            }

            if (offsetProblems.Count > 0)
            {
                offsets++;
                foreach (string p in offsetProblems)
                {
                    diag.WriteLine($"{label}\t{(hinted ? "hint" : "nohint")}\t{id}\toffset\t{country}\t{p}");
                }
            }
        }

        Console.WriteLine($"{label}\t{(hinted ? "hint" : "nohint")}\t{items.Count}\t{refused}\t{noCountry}\t{right}\t{overseas}\t{wrong}\t{invented}\t{offsets}\t{string.Join(';', reasons.Select(kv => kv.Key + "=" + kv.Value))}");
    }
}

return 0;

// The problems of one reading: fields whose text is not the input at their span, and words of the renderings that cannot be traced to the input.
static List<string> Invented(string input, ParsedAddress a)
{
    List<string> problems = [];
    void CheckSpan(string what, string fieldText, SourceSpan span)
    {
        if (span.Start < 0 || span.Length < 0 || span.Start + span.Length > input.Length || !string.Equals(input.Substring(span.Start, span.Length), fieldText, StringComparison.Ordinal))
        {
            problems.Add("offset:" + what);
        }
    }

    foreach (ParsedAddressText l in a.LeadingLines)
    {
        CheckSpan("leading", l.Text, l.Span);
    }

    foreach (StreetLine line in a.DeliveryLines)
    {
        foreach (StreetPart p in line.Parts)
        {
            // The United States and Canada readers store the standardized box designator as the part's text; its words are checked in the renderings.
            if (p.Role == StreetPartRole.BoxDesignator)
            {
                continue;
            }

            CheckSpan("street." + p.Role, p.Text, p.Span);
        }
    }

    foreach (ParsedAddressField f in a.Fields.Where(f => f.Name != "street.box-designator"))
    {
        CheckSpan("field." + f.Name, f.Text, f.Span);
    }

    List<string> words = Words(input);
    HashSet<string> wordSet = [.. words];
    foreach (AddressStyle style in new[] { AddressStyle.Postal, AddressStyle.Display })
    {
        List<string> printed = Words(a.ToString(style, AddressLayout.Label));
        string code = a.Country == Region.Unknown ? string.Empty : a.Country.ToString().ToLowerInvariant();
        for (int k = 0; k < printed.Count; k++)
        {
            string w = printed[k];
            // Initials printed apart (S A S, M I N) are the input's one word (SAS, MIN).
            int run = k;
            while (run < printed.Count && printed[run].Length == 1 && char.IsLetter(printed[run][0]))
            {
                run++;
            }

            if (run - k >= 2 && Traceable(string.Concat(printed.Skip(k).Take(run - k)), words, wordSet))
            {
                k = run - 1;
                continue;
            }

            // The postcode of some countries is printed with a country mark in front (HR-10000, LT-01100, LV-1050, L-8805): the authority's postcode form.
            bool mark = code.Length > 0 && (w == code || (w.Length == 1 && w[0] == code[0])) && k + 1 < printed.Count && char.IsDigit(printed[k + 1][0]);
            if (!mark && !Traceable(w, words, wordSet))
            {
                problems.Add($"{style}:{w}");
            }
        }
    }

    return problems;
}

// A word of the output is traceable when the input has it (ignoring case and accents), or it is the initials of consecutive one-letter input words
// ("N.W." written NW), or it abbreviates an input word (its letters are in that word in order, starting with the same letter: BLVD for Boulevard), or an
// input word abbreviates it (St written Street). Anything else is a value the input does not contain.
static bool Traceable(string w, List<string> words, HashSet<string> set)
{
    if (set.Contains(w))
    {
        return true;
    }

    // A number printed with different grouping (950 01 for 95001): its digits are the digits of the input's consecutive numeric words.
    if (char.IsDigit(w[0]))
    {
        for (int s = 0; s < words.Count; s++)
        {
            string digits = string.Empty;
            for (int e = s; e < words.Count && e < s + 4 && char.IsDigit(words[e][0]); e++)
            {
                digits += words[e];
                if (digits.Contains(w, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
    }

    foreach (string i in words)
    {
        if (i[0] == w[0] && (IsSubsequence(w, i) || IsSubsequence(i, w)))
        {
            return true;
        }
    }

    // An abbreviation of several words (CDMX for Ciudad de México): its letters are in the next few words in order, starting with the first word's initial.
    for (int s = 0; s < words.Count; s++)
    {
        if (words[s][0] != w[0])
        {
            continue;
        }

        string joined = string.Concat(words.Skip(s).Take(4));
        if (IsSubsequence(w, joined))
        {
            return true;
        }
    }

    for (int s = 0; s < words.Count; s++)
    {
        StringBuilder acc = new();
        for (int e = s; e < words.Count && words[e].Length == 1; e++)
        {
            acc.Append(words[e]);
            if (e > s && acc.ToString() == w)
            {
                return true;
            }
        }
    }

    return false;
}

static bool IsSubsequence(string small, string big)
{
    int j = 0;
    foreach (char c in big)
    {
        if (j < small.Length && small[j] == c)
        {
            j++;
        }
    }

    return j == small.Length;
}

static List<string> Words(string text)
{
    List<string> words = [];
    StringBuilder cur = new();
    // "P.O.", "P. O." and "PO" are the same word: a dot between two single letters joins them.
    foreach (char c0 in Patterns.Initials.Replace(text, string.Empty))
    {
        // Letters and digits are separate words, so a postcode written 1234AB and printed 1234 AB trace to the same words.
        if (char.IsLetterOrDigit(c0))
        {
            if (cur.Length > 0 && char.IsDigit(cur[^1]) != char.IsDigit(c0))
            {
                words.Add(cur.ToString());
                cur.Clear();
            }

            cur.Append(Fold(c0));
        }
        else if (cur.Length > 0)
        {
            words.Add(cur.ToString());
            cur.Clear();
        }
    }

    if (cur.Length > 0)
    {
        words.Add(cur.ToString());
    }

    return words;
}

// Lower case with the accents of the Latin letters removed; the letters that do not decompose are mapped by hand.
static string Fold(char c)
{
    c = char.ToLowerInvariant(c);
    const string from = "àáâãäåçèéêëìíîïñòóôõöøùúûüýÿāăąćĉċčďđēĕėęěĝğġģĥĩīĭįıĵķĺļľłńņňōŏőŕŗřśŝşšţťũūŭůűųŵŷźżž";
    const string to = "aaaaaaceeeeiiiinoooooouuuuyyaaaccccddeeeeegggghiiiiijkllllnnnooorrrssssttuuuuuuwyzzz";
    if (c == 'ß')
    {
        return "ss";
    }

    int ix = from.IndexOf(c, StringComparison.Ordinal);
    return ix >= 0 ? to[ix].ToString() : c.ToString();
}

internal static class Patterns
{
    // A single letter, a dot, optional spaces, a single letter: the dot goes, so initials join.
    public static readonly System.Text.RegularExpressions.Regex Initials = new(@"(?<=\b\p{L})\.\s*(?=\p{L}\b)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}
