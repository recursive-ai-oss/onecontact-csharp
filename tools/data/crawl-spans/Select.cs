using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Markup;
using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneContact.Extraction.Text;
using RecursiveAI.OneHtml;

namespace OneContact.Data.CrawlSpans;

// Hand-labelled pieces (docs/maintainers/extraction-span-labels.md).
//
//   crawl-spans select --pages DIR --out DIR --seed N --quota stratum=count,...
//     Cuts short windows of the pages' visible text around the places where a decision about a person or an organization has to be made,
//     draws them by stratum with a fixed seed, and writes candidates.jsonl (text and proposed spans) and review.txt (the same pieces with
//     the proposals written inline as [[P1:Jane Smith]], for a reader to correct).
//   crawl-spans apply --cands FILE --decisions FILE --out DIR
//     Applies the reader's corrections (one line per piece: "c0007 -2 -5 +P:Jane Smith +O:Acme Dental", "c0008 ok", "c0009 skip") and writes
//     the labelled pieces as JSON Lines split by host.
// Page text is written only to --out, on the machine that holds the pages.
internal static partial class Program
{
    private const int WindowChars = 600;
    private const int WindowLines = 6;
    private const ulong DevSalt = 0x7370616e6c626c31UL; // "spanlbl1"

    private sealed record Prop(int Start, int End, string Tag, int Priority);

    private sealed class Cand
    {
        public string Id = "";
        public string Page = "";
        public string Host = "";
        public string Stratum = "";
        public string Text = "";
        public List<Prop> Props = [];
    }

    private static readonly Regex CueLine = new(
        @"\b(contact|call|phone|email|e-mail|ask for|attn|attention|regards|sincerely|thanks|posted by|written by|owner|manager|agent|broker|realtor|attorney|lawyer|dentist|doctor|dr\.|office|director|president|founder|coach|instructor|teacher|pastor|staff|team)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Credential = new(@",?\s+(?:MD|M\.D\.|DDS|DMD|DVM|PhD|Ph\.D\.|Esq\.?|CPA|RN|NP|PA-C|MBA|LCSW|DO|D\.O\.|Jr\.?|Sr\.?|II|III|IV|CFP|CPA|JD|J\.D\.)\b.*$", RegexOptions.CultureInvariant);

    private static int Select(string[] args)
    {
        var o = Options(args);
        string pagesDir = o["pages"];
        string outDir = o["out"];
        ulong seed = ulong.Parse(o["seed"], System.Globalization.CultureInfo.InvariantCulture);
        var quota = o["quota"].Split(',').Select(q => q.Split('=')).ToDictionary(q => q[0], q => int.Parse(q[1], System.Globalization.CultureInfo.InvariantCulture));
        int perPage = o.TryGetValue("per-page", out string? pp) ? int.Parse(pp, System.Globalization.CultureInfo.InvariantCulture) : 3;
        Directory.CreateDirectory(outDir);

        var byStratum = quota.Keys.ToDictionary(k => k, _ => new List<(ulong Key, Cand Cand)>());
        var seenText = new HashSet<string>(StringComparer.Ordinal);
        int pages = 0;
        int failed = 0;
        foreach (string line in File.ReadLines(Path.Combine(pagesDir, "pages.jsonl")))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using JsonDocument meta = JsonDocument.Parse(line);
            JsonElement p = meta.RootElement;
            string id = p.GetProperty("id").GetString()!;
            string host = p.GetProperty("host").GetString()!;
            pages++;
            try
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(pagesDir, "pages", id + ".html"));
                string region = p.GetProperty("tld").GetString() == "ca" ? "CA" : "US";
                var windows = PageWindows(bytes, p.GetProperty("ctype").GetString() ?? "", region);
                foreach (Cand c in windows.OrderBy(w => Hash(seed + ":" + id + ":" + w.Text)).Take(perPage))
                {
                    c.Page = id;
                    c.Host = host;
                    if (byStratum.TryGetValue(c.Stratum, out var list) && seenText.Add(c.Text))
                    {
                        list.Add((Hash(seed + ":" + c.Text), c));
                    }
                }
            }
            catch (Exception e) when (e is HtmlLimitExceededException or ArgumentException or FormatException or IOException)
            {
                failed++;
            }
        }

        var chosen = new List<(ulong Key, Cand Cand)>();
        foreach ((string stratum, int n) in quota)
        {
            chosen.AddRange(byStratum[stratum].OrderBy(x => x.Key).Take(n));
            Console.WriteLine($"stratum {stratum}: available {byStratum[stratum].Count} chosen {Math.Min(n, byStratum[stratum].Count)}");
        }

        int number = 0;
        using var cands = new StreamWriter(Path.Combine(outDir, "candidates.jsonl"), false, new UTF8Encoding(false));
        using var review = new StreamWriter(Path.Combine(outDir, "review.txt"), false, new UTF8Encoding(false));
        foreach ((_, Cand c) in chosen.OrderBy(x => x.Key))
        {
            c.Id = "c" + (++number).ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
            c.Props = [.. c.Props.OrderBy(x => x.Start)];
            cands.WriteLine(JsonSerializer.Serialize(new object[] { c.Id, c.Page, c.Host, c.Stratum, c.Text, c.Props.Select(x => new object[] { x.Start, x.End, x.Tag }).ToArray() }, Raw));
            review.WriteLine(Review(c));
        }

        Console.WriteLine($"pages {pages} failed {failed} candidates {number}");
        return 0;
    }

    private static readonly JsonSerializerOptions Raw = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static Dictionary<string, string> Options(string[] args)
    {
        var o = new Dictionary<string, string>();
        for (int i = 1; i + 1 < args.Length; i += 2)
        {
            o[args[i].TrimStart('-')] = args[i + 1];
        }

        return o;
    }

    private static string Review(Cand c)
    {
        var sb = new StringBuilder();
        sb.Append('#').Append(c.Id).Append(' ').Append(c.Stratum[0]).Append(' ');
        int at = 0;
        int n = 0;
        foreach (Prop p in c.Props)
        {
            sb.Append(c.Text, at, p.Start - at).Append("[[").Append(p.Tag).Append(++n).Append(':').Append(c.Text, p.Start, p.End - p.Start).Append("]]");
            at = p.End;
        }

        sb.Append(c.Text, at, c.Text.Length - at);
        return sb.ToString().Replace("\r", "", StringComparison.Ordinal).Replace("\n", "¶", StringComparison.Ordinal);
    }

    private static List<Cand> PageWindows(byte[] bytes, string ctype, string region)
    {
        Document doc = Document.Parse(bytes, new ParseOptions { ContentType = ctype });
        var ctx = new PageContext(doc, null, bytesInput: true);
        TextView view = VisibleTextReader.Read(ctx, out _);
        string text = view.Text;
        var result = new List<Cand>();
        if (text.Length == 0 || view.Lines.Count == 0)
        {
            return result;
        }

        var extractor = new ContactExtractor { DefaultRegion = Region.Parse(region) };
        ExtractResult markup = new ContactExtractor { Sources = ExtractionSources.Markup, DefaultRegion = Region.Parse(region) }.ExtractHtml(bytes, ctype);
        var props = new List<Prop>();

        // 1. The page's own markup names, wherever they repeat in the text.
        foreach (ContactRecord r in markup.Records)
        {
            foreach (ExtractedField f in r.Fields.Where(f => f.Kind is FieldKind.Name or FieldKind.Organization))
            {
                string label = f.Kind == FieldKind.Organization || r.Kind == ContactRecordKind.Organization ? "O" : "P";
                if (!UsableName(f.Text, label == "P" ? "person" : "company"))
                {
                    continue;
                }

                int from = 0;
                while (from < text.Length)
                {
                    int at = text.IndexOf(f.Text, from, StringComparison.Ordinal);
                    if (at < 0)
                    {
                        break;
                    }

                    int end = at + f.Text.Length;
                    from = end;
                    if ((at > 0 && char.IsLetterOrDigit(text[at - 1])) || (end < text.Length && char.IsLetterOrDigit(text[end])) || text.IndexOf('\n', at, f.Text.Length) >= 0)
                    {
                        continue;
                    }

                    props.Add(new Prop(at, end, label, 1));
                }
            }
        }

        // 2. Lines shaped like a person's name (title and credentials left out), 3. the rules' own guesses and fields, 4. legal-form phrases.
        for (int l = 0; l < view.Lines.Count; l++)
        {
            TextLine tl = view.Lines[l];
            int e = Math.Min(tl.End, text.Length);
            string line = text.Substring(tl.Start, Math.Max(0, e - tl.Start));
            if (NameLike(line))
            {
                int a = 0;
                string[] words = line.Split(' ');
                if (words.Length > 0 && Titles.Contains(words[0]))
                {
                    a = words[0].Length + 1;
                }

                Match cred = Credential.Match(line);
                int b = cred.Success && cred.Index > a ? cred.Index : line.TrimEnd().Length;
                if (b > a)
                {
                    props.Add(new Prop(tl.Start + a, tl.Start + b, "P", 2));
                }
            }
        }

        ExtractResult rules = extractor.ExtractText(text);
        var recordOf = new Dictionary<int, ContactRecord>();
        foreach (ContactRecord r in rules.Records)
        {
            foreach (ExtractedField f in r.Fields)
            {
                recordOf[f.Id] = r;
            }
        }

        foreach (ExtractedField f in rules.Fields)
        {
            recordOf.TryGetValue(f.Id, out ContactRecord? rec);
            foreach (ExtractedField m in new[] { f }.Concat(f.Duplicates))
            {
                if (!m.HasSpan || m.Span.Start < 0 || m.Span.Start + m.Span.Length > text.Length || m.Span.Length <= 0)
                {
                    continue;
                }

                string tag = f.Kind switch
                {
                    FieldKind.Email => "E",
                    FieldKind.Phone => "T",
                    FieldKind.Website => "W",
                    FieldKind.Address => "A",
                    _ => TypeOfName(f, rec) == "company" ? "O" : "P",
                };
                props.Add(new Prop(m.Span.Start, m.Span.Start + m.Span.Length, tag, tag is "P" or "O" ? 3 : 0));
            }
        }

        foreach (Match m in OrgLike.Matches(text))
        {
            if (text.IndexOf('\n', m.Index, m.Length) < 0)
            {
                props.Add(new Prop(m.Index, m.Index + m.Length, "O", 4));
            }
        }

        // Overlaps: contact fields first (rules are the model's own gate for them), then markup names, name-shaped lines, guesses, legal forms.
        var kept = new List<Prop>();
        foreach (Prop p in props.OrderBy(x => x.Priority).ThenByDescending(x => x.End - x.Start).ThenBy(x => x.Start))
        {
            if (!kept.Any(k => p.Start < k.End && k.Start < p.End))
            {
                kept.Add(p);
            }
        }

        kept = [.. kept.OrderBy(x => x.Start)];
        var windows = new HashSet<int>();
        int n = view.Lines.Count;
        for (int l = 0; l < n; l++)
        {
            TextLine tl = view.Lines[l];
            int e = Math.Min(tl.End, text.Length);
            if (e <= tl.Start)
            {
                continue;
            }

            string line = text.Substring(tl.Start, e - tl.Start);
            bool hasName = kept.Any(k => k.Tag is "P" or "O" && k.Start >= tl.Start && k.End <= e);
            bool hasField = kept.Any(k => k.Tag is "A" or "T" or "E" or "W" && k.Start >= tl.Start && k.End <= e);
            bool cue = CueLine.IsMatch(line);
            string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            bool lookalike = line.Length is >= 6 and <= 40 && words.Length is >= 2 and <= 4 && words.All(w => char.IsUpper(w[0])) && !line.Any(char.IsDigit);
            if (!(hasName || hasField || cue || lookalike))
            {
                continue;
            }

            int a = Math.Max(0, l - 1);
            int b = Math.Min(n - 1, l + 1);
            while (b > a && (b - a + 1 > WindowLines || Math.Min(view.Lines[b].End, text.Length) - view.Lines[a].Start > WindowChars))
            {
                if (b - l >= l - a)
                {
                    b--;
                }
                else
                {
                    a++;
                }
            }

            if (Math.Min(view.Lines[b].End, text.Length) - view.Lines[a].Start > WindowChars + 200 || !windows.Add(a * 100000 + b))
            {
                continue;
            }

            int start = view.Lines[a].Start;
            int end = Math.Min(view.Lines[b].End, text.Length);
            if (kept.Any(k => k.Start < end && start < k.End && (k.Start < start || k.End > end)) || end <= start)
            {
                continue;
            }

            var c = new Cand { Text = text.Substring(start, end - start) };
            foreach (Prop k in kept.Where(k => k.Start >= start && k.End <= end))
            {
                c.Props.Add(k with { Start = k.Start - start, End = k.End - start });
            }

            bool anyP = c.Props.Any(k => k.Tag == "P");
            bool anyO = c.Props.Any(k => k.Tag == "O");
            bool anyF = c.Props.Any(k => k.Tag is "A" or "T" or "E" or "W");
            c.Stratum = anyP ? "person" : anyO ? "org" : anyF ? "fields" : CueLine.IsMatch(c.Text) ? "cue" : "lookalike";
            result.Add(c);
        }

        return result;
    }

    private static int Apply(string[] args)
    {
        var o = Options(args);
        string outDir = o["out"];
        Directory.CreateDirectory(outDir);
        var cands = new Dictionary<string, (string Page, string Host, string Text, List<Prop> Props)>(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(o["cands"]))
        {
            using JsonDocument d = JsonDocument.Parse(line);
            JsonElement r = d.RootElement;
            var props = r[5].EnumerateArray().Select(x => new Prop(x[0].GetInt32(), x[1].GetInt32(), x[2].GetString()!, 0)).ToList();
            cands[r[0].GetString()!] = (r[1].GetString()!, r[2].GetString()!, r[4].GetString()!, props);
        }

        using var all = new StreamWriter(Path.Combine(outDir, "all.jsonl"), false, new UTF8Encoding(false));
        using var dev = new StreamWriter(Path.Combine(outDir, "dev.jsonl"), false, new UTF8Encoding(false));
        using var pos = new StreamWriter(Path.Combine(outDir, "train-positive.jsonl"), false, new UTF8Encoding(false));
        using var neg = new StreamWriter(Path.Combine(outDir, "train-negative.jsonl"), false, new UTF8Encoding(false));
        var decided = new HashSet<string>(StringComparer.Ordinal);
        int skipped = 0;
        int bad = 0;
        var counts = new Dictionary<string, int>();
        // "@reviewed c0001 c0080" marks a range of pieces as read: those without a line of their own count as "ok".
        var decisionLines = new List<string>(File.ReadAllLines(o["decisions"]));
        var explicitIds = new HashSet<string>(decisionLines.Where(l => l.StartsWith('c')).Select(l => l.Split(' ')[0]), StringComparer.Ordinal);
        foreach (string range in decisionLines.Where(l => l.StartsWith("@reviewed ", StringComparison.Ordinal)).ToList())
        {
            string[] ends = range.Split(' ');
            int from = int.Parse(ends[1][1..]);
            int to = int.Parse(ends[2][1..]);
            for (int n = from; n <= to; n++)
            {
                string id = "c" + n.ToString("0000", System.Globalization.CultureInfo.InvariantCulture);
                if (!explicitIds.Contains(id))
                {
                    decisionLines.Add(id + " ok");
                }
            }
        }

        foreach (string raw in decisionLines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or '@')
            {
                continue;
            }

            string[] ops = line.Split(" ; ", StringSplitOptions.RemoveEmptyEntries);
            string[] head = ops[0].Split(' ', 2);
            string id = head[0];
            if (!cands.TryGetValue(id, out var c) || !decided.Add(id))
            {
                Console.WriteLine($"unknown or repeated id {id}");
                bad++;
                continue;
            }

            // Operations, separated by " ; ": "ok", "skip", "-N" removes proposal N (numbers as in review.txt), "-P" / "-O" / "-all" remove all
            // person / organization / both proposals, "+X:text" labels every whole-word occurrence of text with X (P person, O organization,
            // A address, T phone, E email, W website). The first operation may follow the id after a space.
            var opList = new List<string>();
            if (head.Length > 1)
            {
                opList.Add(head[1].Trim());
            }

            opList.AddRange(ops.Skip(1).Select(x => x.Trim()));
            if (opList.Contains("skip"))
            {
                skipped++;
                continue;
            }

            var props = c.Props.OrderBy(x => x.Start).ToList();
            var removed = new HashSet<int>();
            var added = new List<Prop>();
            foreach (string part in opList)
            {
                if (part == "ok" || part.Length == 0)
                {
                    continue;
                }

                if (part.StartsWith('-'))
                {
                    string what = part[1..];
                    for (int i = 0; i < props.Count && what is "P" or "O" or "all"; i++)
                    {
                        if (what == "all" ? props[i].Tag is "P" or "O" : props[i].Tag == what)
                        {
                            removed.Add(i);
                        }
                    }

                    if (what is not ("P" or "O" or "all"))
                    {
                        foreach (string num in what.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (int.TryParse(num, out int k) && k >= 1 && k <= props.Count)
                            {
                                removed.Add(k - 1);
                            }
                            else
                            {
                                Console.WriteLine($"{id}: bad removal {part}");
                                bad++;
                            }
                        }
                    }
                }
                else if (part.StartsWith('+') && part.Length > 3 && part[2] == ':')
                {
                    string tag = part[1].ToString();
                    string needle = part[3..];
                    int found = 0;
                    int from = 0;
                    while (from < c.Text.Length)
                    {
                        int at = c.Text.IndexOf(needle, from, StringComparison.Ordinal);
                        if (at < 0)
                        {
                            break;
                        }

                        int end = at + needle.Length;
                        from = end;
                        if ((at > 0 && char.IsLetterOrDigit(c.Text[at - 1]) && char.IsLetterOrDigit(needle[0])) || (end < c.Text.Length && char.IsLetterOrDigit(c.Text[end]) && char.IsLetterOrDigit(needle[^1])))
                        {
                            continue;
                        }

                        added.Add(new Prop(at, end, tag, 0));
                        found++;
                    }

                    if (found == 0)
                    {
                        Console.WriteLine($"{id}: text not found for {part}");
                        bad++;
                    }
                }
                else
                {
                    Console.WriteLine($"{id}: bad operation {part}");
                    bad++;
                }
            }

            var final = new List<Prop>();
            foreach (Prop a in added.OrderByDescending(x => x.End - x.Start))
            {
                // An added span replaces the proposals it overlaps.
                for (int i = 0; i < props.Count; i++)
                {
                    if (props[i].Start < a.End && a.Start < props[i].End)
                    {
                        removed.Add(i);
                    }
                }
            }

            for (int i = 0; i < props.Count; i++)
            {
                if (!removed.Contains(i))
                {
                    final.Add(props[i]);
                }
            }

            foreach (Prop a in added.OrderByDescending(x => x.End - x.Start).ThenBy(x => x.Start))
            {
                if (!final.Any(f => a.Start < f.End && f.Start < a.End))
                {
                    final.Add(a);
                }
            }

            final = [.. final.OrderBy(x => x.Start)];
            var piece = new Piece { Text = c.Text };
            var recordOfName = new Dictionary<string, int>(StringComparer.Ordinal);
            var nameSpans = new List<(int Start, int End, int Record)>();
            foreach (Prop f in final.Where(x => x.Tag is "P" or "O"))
            {
                string key = f.Tag + c.Text.Substring(f.Start, f.End - f.Start).ToLowerInvariant();
                if (!recordOfName.TryGetValue(key, out int rec))
                {
                    rec = recordOfName.Count;
                    recordOfName[key] = rec;
                }

                nameSpans.Add((f.Start, f.End, rec));
            }

            foreach (Prop f in final)
            {
                string label = f.Tag switch { "P" => "person", "O" => "company", "A" => "address", "T" => "phone", "E" => "email", "W" => "website", _ => "" };
                if (label.Length == 0)
                {
                    Console.WriteLine($"{id}: unknown tag {f.Tag}");
                    bad++;
                    continue;
                }

                int record = -1;
                if (label is "person" or "company")
                {
                    record = nameSpans.First(n => n.Start == f.Start).Record;
                }
                else if (nameSpans.Count > 0)
                {
                    // A contact value belongs to the nearest name within three lines, the one before it when both are as near.
                    int fieldLine = c.Text.Take(f.Start).Count(ch => ch == '\n');
                    var near = nameSpans.Select(n => (n.Record, D: Math.Abs(c.Text.Take(n.Start).Count(ch => ch == '\n') - fieldLine), Before: n.Start < f.Start))
                        .Where(n => n.D <= 3).OrderBy(n => n.D).ThenByDescending(n => n.Before).FirstOrDefault();
                    record = near == default ? -1 : near.Record;
                }

                piece.Spans.Add(new Span(f.Start, f.End, label, record));
            }

            bool positive = piece.Spans.Any(s => s.Label is "person" or "company");
            piece.Kind = positive ? "labelled-piece" : "negative-piece";
            string json = piece.ToJson(c.Page, c.Host);
            bool isDev = (Hash(c.Host) ^ DevSalt) % 100 < 8;
            all.WriteLine(json);
            (isDev ? dev : positive ? pos : neg).WriteLine(json);
            foreach (Span s in piece.Spans)
            {
                string k = (isDev ? "dev " : "train ") + s.Label;
                counts[k] = counts.GetValueOrDefault(k) + 1;
            }

            string pk = (isDev ? "dev " : "train ") + (positive ? "positive" : "negative") + " pieces";
            counts[pk] = counts.GetValueOrDefault(pk) + 1;
        }

        Console.WriteLine($"decided {decided.Count} skipped {skipped} problems {bad}; undecided candidates {cands.Count - decided.Count}");
        foreach (var kv in counts.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"{kv.Key}: {kv.Value}");
        }

        return bad == 0 ? 0 : 1;
    }
}
