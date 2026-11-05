using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OneContact.Data.Blocks;

/// <summary>What the library's rules alone find in rendered blocks. Information for the span-head recipe, not a score of the parser:
/// phones through the matcher, emails and web addresses through token scans checked by the core parsers, addresses by whether the labelled text parses.
/// Person names and company names have no rule-only finder here (they need the visible-text reader's labels and the model).</summary>
internal static class RuleReport
{
    private sealed record Gold(int Start, int End, string Label, string Mode);

    private sealed class Tally
    {
        internal int Gold;
        internal int Pred;
        internal int ExactHit;
        internal int OverlapHit;
        internal int PredOk;
        internal int PredOverlap;
    }

    internal static void Run(string outDir, int sample)
    {
        string devFile = Path.Combine(outDir, "dev", "part-00000.jsonl");
        string[] all = File.ReadAllLines(devFile, Encoding.UTF8);
        int stride = Math.Max(1, all.Length / sample);
        SortedDictionary<string, Tally> tally = new(StringComparer.Ordinal);
        SortedDictionary<string, int> addrParse = new(StringComparer.Ordinal);
        SortedDictionary<string, int> addrSeen = new(StringComparer.Ordinal);
        int used = 0;
        int negFalse = 0;
        int negDocs = 0;

        Tally T(string k)
        {
            if (!tally.TryGetValue(k, out Tally? t))
            {
                tally[k] = t = new Tally();
            }

            return t;
        }

        for (int li = 0; li < all.Length && used < sample; li += stride)
        {
            used++;
            using JsonDocument doc = JsonDocument.Parse(all[li]);
            JsonElement e = doc.RootElement;
            string text = e.GetProperty("text").GetString()!;
            string loc = e.GetProperty("loc").GetString()!;
            string view = e.GetProperty("view").GetString()!;
            bool neg = e.GetProperty("neg").GetBoolean();
            Region region = loc == "us-en" ? Region.US : Region.CA;
            List<Gold> gold = [];
            foreach (JsonElement s in e.GetProperty("spans").EnumerateArray())
            {
                gold.Add(new Gold(s[0].GetInt32(), s[1].GetInt32(), s[2].GetString()!, s.GetArrayLength() > 4 ? s[4].GetString()! : ""));
            }

            // Phones: the matcher at Valid leniency.
            List<(int S, int E)> phones = [];
            foreach (PhoneNumberMatch m in PhoneNumber.FindAll(text, region, PhoneMatchLeniency.Valid))
            {
                phones.Add((m.Start, m.End));
            }

            Score(T("phone/" + view), gold, "phone", phones, plain: true);
            Score(T("phone/obfuscated"), gold, "phone", phones, plain: false);

            // Emails: a token around each '@', trailing punctuation trimmed, checked by the core parser.
            List<(int S, int E)> emails = [];
            for (int at = text.IndexOf('@', StringComparison.Ordinal); at >= 0; at = text.IndexOf('@', at + 1))
            {
                int s = at;
                while (s > 0 && !char.IsWhiteSpace(text[s - 1]) && text[s - 1] is not ('<' or '(' or ';' or ',' or '|' or ':'))
                {
                    s--;
                }

                int en = at + 1;
                while (en < text.Length && !char.IsWhiteSpace(text[en]) && text[en] is not ('>' or ')' or ';' or ',' or '|'))
                {
                    en++;
                }

                while (en > at + 1 && text[en - 1] is '.' or ',' or ':' or ';')
                {
                    en--;
                }

                if (EmailAddress.TryParse(text.AsSpan(s, en - s), out _))
                {
                    emails.Add((s, en));
                }
            }

            Score(T("email/" + view), gold, "email", emails, plain: true);
            Score(T("email/obfuscated"), gold, "email", emails, plain: false);

            // Websites: dotted tokens the core web parser accepts with a registrable domain, not part of an email address.
            List<(int S, int E)> webs = [];
            int p = 0;
            while (p < text.Length)
            {
                while (p < text.Length && IsSep(text[p]))
                {
                    p++;
                }

                int s = p;
                while (p < text.Length && !IsSep(text[p]))
                {
                    p++;
                }

                int en = p;
                while (en > s && text[en - 1] is '.' or ',' or ':' or ';' or ')')
                {
                    en--;
                }

                if (en - s < 4 || text.AsSpan(s, en - s).Contains('@') || !text.AsSpan(s, en - s).Contains('.'))
                {
                    continue;
                }

                string tok = text.Substring(s, en - s);
                if (WebAddress.TryParse(tok, out WebAddress w) && w.RegistrableDomain is not null && tok.Any(char.IsLetter))
                {
                    webs.Add((s, en));
                }
            }

            Score(T("website/" + view), gold, "website", webs, plain: true);

            // Addresses: does the labelled text parse on its own?
            foreach (Gold g in gold)
            {
                if (g.Label != "address")
                {
                    continue;
                }

                addrSeen[loc] = addrSeen.GetValueOrDefault(loc) + 1;
                string a = text.Substring(g.Start, g.End - g.Start);
                if (PostalAddress.TryParse(a, out _))
                {
                    addrParse[loc] = addrParse.GetValueOrDefault(loc) + 1;
                }
            }

            if (neg)
            {
                negDocs++;
                if (phones.Count + emails.Count + webs.Count > 0)
                {
                    negFalse++;
                }
            }
        }

        StringBuilder md = new();
        md.AppendLine("# Rules alone on rendered blocks (information)");
        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"{used} dev examples sampled by stride from `dev/part-00000.jsonl`. Exact = same start and end as the label; overlap = any shared characters.");
        md.AppendLine();
        md.AppendLine("| field/view | labelled | found | exact recall | overlap recall | precision (exact) | precision (overlap) |");
        md.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var kv in tally)
        {
            Tally t = kv.Value;
            if (t.Gold == 0 && t.Pred == 0)
            {
                continue;
            }

            md.AppendLine(CultureInfo.InvariantCulture, $"| {kv.Key} | {t.Gold} | {t.Pred} | {Pct(t.ExactHit, t.Gold)} | {Pct(t.OverlapHit, t.Gold)} | {Pct(t.PredOk, t.Pred)} | {Pct(t.PredOverlap, t.Pred)} |");
        }

        md.AppendLine();
        md.AppendLine("| addresses that parse on their own | labelled | parsed |");
        md.AppendLine("|---|---|---|");
        foreach (var kv in addrSeen)
        {
            md.AppendLine(CultureInfo.InvariantCulture, $"| {kv.Key} | {kv.Value} | {Pct(addrParse.GetValueOrDefault(kv.Key), kv.Value)} |");
        }

        md.AppendLine();
        md.AppendLine(CultureInfo.InvariantCulture, $"Negative examples with at least one rule-found phone, email or website: {negFalse} of {negDocs}.");
        md.AppendLine();
        md.AppendLine("Person names and company names: no rule-only finder was run; they need line context and the model.");
        File.WriteAllText(Path.Combine(outDir, "rule-report.md"), md.ToString());
        Console.WriteLine(md.ToString());
    }

    private static bool IsSep(char c) => char.IsWhiteSpace(c) || c is '|' or ',' or ';' or '<' or '>' or '(';

    private static string Pct(int a, int b) => b == 0 ? "-" : (100.0 * a / b).ToString("0.0", CultureInfo.InvariantCulture) + " %";

    /// <summary>Counts gold and predicted spans of one label. <paramref name="plain"/> selects the exact-form labels; false selects the obfuscated ones.</summary>
    private static void Score(Tally t, List<Gold> gold, string label, List<(int S, int E)> pred, bool plain)
    {
        List<Gold> g = gold.Where(x => x.Label == label && (x.Mode.Contains('o', StringComparison.Ordinal) != plain)).ToList();
        t.Gold += g.Count;
        foreach (Gold x in g)
        {
            if (pred.Any(p => p.S == x.Start && p.E == x.End))
            {
                t.ExactHit++;
            }

            if (pred.Any(p => p.S < x.End && x.Start < p.E))
            {
                t.OverlapHit++;
            }
        }

        if (!plain)
        {
            return; // predictions are counted once, with the plain row
        }

        t.Pred += pred.Count;
        foreach ((int s, int e) in pred)
        {
            if (gold.Any(x => x.Label == label && x.Start == s && x.End == e))
            {
                t.PredOk++;
            }

            if (gold.Any(x => x.Label == label && s < x.End && x.Start < e))
            {
                t.PredOverlap++;
            }
        }
    }
}
