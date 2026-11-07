using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Markup;
using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneContact.Extraction.Text;
using RecursiveAI.OneHtml;

namespace OneContact.Data.CrawlSpans;

// usage: crawl-spans label --pages DIR --out DIR [--only-hosts-not FILE]
//
// DIR holds pages.jsonl and pages/<id>.html as written by tools/Eval/extraction/fetch.py. For every page the tool reads the structured markup
// (JSON-LD, microdata, RDFa, h-card, mailto/tel links) with the library's own readers, finds where its names, organizations and contact values
// repeat in the page's visible text, and writes training pieces (text plus labelled spans) as JSON Lines, split train/dev by host.
// The method is written down in docs/extraction-crawl-training.md. Page text is written only to --out, on the machine that holds the pages.
internal static class Program
{
    // Same piece size as the model reads at run time (TextFieldReader.Model.cs).
    private const int MaxPieceLines = 40;
    private const int MaxPieceChars = 3000;
    private const int WindowMargin = 2;
    private const int MaxWindowLines = 12;
    private const ulong SplitSalt = 0x6372617764303031UL; // "crawl001"

    private static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "check")
        {
            return Check(args[2..]);
        }

        if (args.Length == 0 || args[0] != "label")
        {
            Console.Error.WriteLine("usage: label --pages DIR --out DIR | check --in FILE...");
            return 2;
        }

        var o = new Dictionary<string, string>();
        for (int i = 1; i + 1 < args.Length; i += 2)
        {
            o[args[i].TrimStart('-')] = args[i + 1];
        }

        string pagesDir = o["pages"];
        string outDir = o["out"];
        Directory.CreateDirectory(outDir);
        var stats = new Stats();
        using var train = new StreamWriter(Path.Combine(outDir, "train.jsonl"), false, new UTF8Encoding(false));
        using var dev = new StreamWriter(Path.Combine(outDir, "dev.jsonl"), false, new UTF8Encoding(false));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(Path.Combine(pagesDir, "pages.jsonl")))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using JsonDocument meta = JsonDocument.Parse(line);
            JsonElement p = meta.RootElement;
            string id = p.GetProperty("id").GetString()!;
            if (!seen.Add(id))
            {
                continue;
            }

            string host = p.GetProperty("host").GetString()!;
            bool isDev = Hash(host) % 100 < 5;
            stats.Pages++;
            try
            {
                byte[] bytes = File.ReadAllBytes(Path.Combine(pagesDir, "pages", id + ".html"));
                string ctype = p.GetProperty("ctype").GetString() ?? string.Empty;
                string region = p.GetProperty("tld").GetString() == "ca" ? "CA" : "US";
                foreach (Piece piece in LabelPage(bytes, ctype, region, stats))
                {
                    (isDev ? dev : train).WriteLine(piece.ToJson(id, host));
                    stats.Count(piece, isDev);
                }
            }
            catch (Exception e) when (e is HtmlLimitExceededException or ArgumentException or FormatException or IOException)
            {
                stats.Failed++;
            }
        }

        stats.Print();
        return 0;
    }

    // Every span lies inside its piece, has a known label, is not empty, starts and ends on a non-space character and does not overlap
    // another span; hosts never appear in both the train and the dev file. Prints counts only.
    private static int Check(string[] files)
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal) { "person", "company", "address", "phone", "email", "website" };
        int pieces = 0;
        int failures = 0;
        var hostsPerFile = new List<HashSet<string>>();
        foreach (string file in files)
        {
            var hosts = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in File.ReadLines(file))
            {
                using JsonDocument json = JsonDocument.Parse(line);
                JsonElement root = json.RootElement;
                string text = root.GetProperty("text").GetString()!;
                hosts.Add(root.GetProperty("host").GetString()!);
                pieces++;
                int previousEnd = 0;
                foreach (JsonElement s in root.GetProperty("spans").EnumerateArray())
                {
                    int a = s[0].GetInt32();
                    int b = s[1].GetInt32();
                    bool ok = a >= previousEnd && b > a && b <= text.Length && allowed.Contains(s[2].GetString()!) && !char.IsWhiteSpace(text[a]) && !char.IsWhiteSpace(text[b - 1]);
                    if (!ok)
                    {
                        failures++;
                    }

                    previousEnd = b;
                }
            }

            hostsPerFile.Add(hosts);
        }

        int shared = hostsPerFile.Count > 1 ? hostsPerFile[0].Intersect(hostsPerFile[1]).Count() : 0;
        Console.WriteLine($"pieces {pieces} span-check failures {failures} hosts shared between the first two files {shared}");
        return failures == 0 && shared == 0 ? 0 : 1;
    }

    private static ulong Hash(string text)
    {
        byte[] h = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return BitConverter.ToUInt64(h, 0) ^ SplitSalt;
    }

    private sealed record Span(int Start, int End, string Label, int Record);

    private sealed class Piece
    {
        public string Kind = "";
        public string Text = "";
        public List<Span> Spans = [];

        public string ToJson(string id, string host)
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            {
                w.WriteStartObject();
                w.WriteString("page", id);
                w.WriteString("host", host);
                w.WriteString("kind", Kind);
                w.WriteString("text", Text);
                w.WriteStartArray("spans");
                foreach (Span s in Spans)
                {
                    w.WriteStartArray();
                    w.WriteNumberValue(s.Start);
                    w.WriteNumberValue(s.End);
                    w.WriteStringValue(s.Label);
                    w.WriteNumberValue(s.Record);
                    w.WriteEndArray();
                }

                w.WriteEndArray();
                w.WriteEndObject();
            }

            return Encoding.UTF8.GetString(ms.ToArray());
        }
    }

    private sealed class Stats
    {
        public int Pages, Failed, WithMarkup, WithMatchedName;
        public int PiecesSeen, DroppedUnexplained, DroppedNameLike, DroppedOverlap, DroppedOrgLike, DroppedGuessedName;
        public readonly Dictionary<string, int> Kept = [];
        public readonly Dictionary<string, int> Labels = [];

        public void Count(Piece p, bool dev)
        {
            string key = (dev ? "dev " : "train ") + p.Kind;
            Kept[key] = Kept.GetValueOrDefault(key) + 1;
            foreach (Span s in p.Spans)
            {
                string l = (dev ? "dev " : "train ") + s.Label + (s.Record >= 0 ? " (markup record)" : " (rule)");
                Labels[l] = Labels.GetValueOrDefault(l) + 1;
            }
        }

        public void Print()
        {
            Console.WriteLine($"pages {Pages} failed {Failed} with-markup-record {WithMarkup} with-matched-name {WithMatchedName}");
            Console.WriteLine($"pieces-seen {PiecesSeen} dropped-unexplained-contact {DroppedUnexplained} dropped-name-like {DroppedNameLike} dropped-overlap {DroppedOverlap} dropped-org-like {DroppedOrgLike} dropped-guessed-name-beside-contact {DroppedGuessedName}");
            foreach (var kv in Kept.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"kept {kv.Key}: {kv.Value}");
            }

            foreach (var kv in Labels.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"spans {kv.Key}: {kv.Value}");
            }
        }
    }

    private sealed record Named(string Text, string Label, int Record);

    private static IEnumerable<Piece> LabelPage(byte[] bytes, string ctype, string region, Stats stats)
    {
        Document doc = Document.Parse(bytes, new ParseOptions { ContentType = ctype });
        var ctx = new PageContext(doc, null, bytesInput: true);
        TextView view = VisibleTextReader.Read(ctx, out _);
        string text = view.Text;
        if (text.Length == 0 || view.Lines.Count == 0)
        {
            return [];
        }

        var extractor = new ContactExtractor { DefaultRegion = Region.Parse(region) };

        // The markup half: the records the structured readers make, with the values they hold.
        ExtractResult markup = new ContactExtractor { Sources = ExtractionSources.Markup, DefaultRegion = Region.Parse(region) }.ExtractHtml(bytes, ctype);
        var names = new List<Named>();
        var markupEmails = new HashSet<string>(StringComparer.Ordinal);
        var markupPhones = new HashSet<string>(StringComparer.Ordinal);
        var markupSites = new HashSet<string>(StringComparer.Ordinal);
        var markupAddressTokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (ContactRecord r in markup.Records)
        {
            foreach (ExtractedField f in r.Fields)
            {
                switch (f.Kind)
                {
                    case FieldKind.Name:
                        names.Add(new Named(f.Text, r.Kind == ContactRecordKind.Person ? "person" : "company", r.Id));
                        break;
                    case FieldKind.Organization:
                        names.Add(new Named(f.Text, "company", r.Id));
                        break;
                    case FieldKind.Email:
                        markupEmails.Add(EmailKey(f.Text));
                        break;
                    case FieldKind.Phone:
                        markupPhones.Add(PhoneKey(f.Text));
                        break;
                    case FieldKind.Website:
                        markupSites.Add(SiteKey(f.Text));
                        break;
                    case FieldKind.Address:
                        foreach (string t in AddressTokens(f.Text))
                        {
                            markupAddressTokens.Add(t);
                        }

                        break;
                }
            }
        }

        // The rules' fields in the visible text, with character offsets (the text is read as plain text, which gives the same lines).
        ExtractResult rules = extractor.ExtractText(text);
        var recordOfRule = new Dictionary<int, ContactRecord>();
        foreach (ContactRecord r in rules.Records)
        {
            foreach (ExtractedField f in r.Fields)
            {
                recordOfRule[f.Id] = r;
            }
        }

        var ruleSpans = new List<(Span Span, bool Explained)>();
        foreach (ExtractedField f in rules.Fields)
        {
            recordOfRule.TryGetValue(f.Id, out ContactRecord? rec);
            foreach (ExtractedField m in new[] { f }.Concat(f.Duplicates))
            {
                if (!m.HasSpan)
                {
                    continue;
                }

                int s = m.Span.Start;
                int e = s + m.Span.Length;
                if (s < 0 || e > text.Length || e <= s)
                {
                    continue;
                }

                string value = text.Substring(s, e - s);
                switch (f.Kind)
                {
                    case FieldKind.Email:
                        ruleSpans.Add((new Span(s, e, "email", -1), markupEmails.Contains(EmailKey(value))));
                        break;
                    case FieldKind.Phone:
                        ruleSpans.Add((new Span(s, e, "phone", -1), markupPhones.Contains(PhoneKey(value))));
                        break;
                    case FieldKind.Website:
                        // A web address is a label, but never a reason to throw a piece away: pages repeat links the markup does not list.
                        ruleSpans.Add((new Span(s, e, "website", -1), true));
                        break;
                    case FieldKind.Address:
                        ruleSpans.Add((new Span(s, e, "address", -1), AddressExplained(value, markupAddressTokens)));
                        break;
                    case FieldKind.Name:
                    case FieldKind.Organization:
                        // A name the rules guessed is not a label: on pages the rules over-read names, so it is kept only as a marker ("?"), which a piece
                        // survives if no contact data sits beside it and which never becomes a training span.
                        ruleSpans.Add((new Span(s, e, TypeOfName(f, rec) + "?", -1), true));
                        break;
                }
            }
        }

        // Where the markup's names repeat in the visible text: every whole-word occurrence is a mention of that person or organization.
        var markupSpans = new List<Span>();
        foreach (Named n in names.DistinctBy(x => (x.Text, x.Label, x.Record)))
        {
            if (!UsableName(n.Text, n.Label))
            {
                continue;
            }

            int from = 0;
            while (from < text.Length)
            {
                int at = text.IndexOf(n.Text, from, StringComparison.Ordinal);
                if (at < 0)
                {
                    break;
                }

                int end = at + n.Text.Length;
                from = end;
                if ((at > 0 && char.IsLetterOrDigit(text[at - 1])) || (end < text.Length && char.IsLetterOrDigit(text[end])) || text.IndexOf('\n', at, n.Text.Length) >= 0)
                {
                    continue;
                }

                markupSpans.Add(new Span(at, end, n.Label, n.Record));
            }
        }

        if (markup.Records.Count > 0)
        {
            stats.WithMarkup++;
        }

        if (markupSpans.Count > 0)
        {
            stats.WithMatchedName++;
        }

        // A markup name wins over a rule span it overlaps; markup spans that overlap one another keep the longest.
        markupSpans = [.. markupSpans.OrderByDescending(s => s.End - s.Start).ThenBy(s => s.Start)];
        var markupKept = new List<Span>();
        foreach (Span s in markupSpans)
        {
            if (!markupKept.Any(k => s.Start < k.End && k.Start < s.End))
            {
                markupKept.Add(s);
            }
        }

        var all = new List<(Span Span, bool Explained)>(markupKept.Select(s => (s, true)));
        foreach ((Span Span, bool Explained) r in ruleSpans)
        {
            if (!markupKept.Any(k => r.Span.Start < k.End && k.Start < r.Span.End))
            {
                all.Add(r);
            }
        }

        // Fields of the rules that line up with a markup record share its record, so a block start is learned at the right place: a rule
        // email, phone or address takes the record of the nearest markup name within three lines.
        var spans = all.OrderBy(a => a.Span.Start).ToList();
        int[] lineOf = new int[text.Length + 1];
        for (int l = 0; l < view.Lines.Count; l++)
        {
            for (int c = view.Lines[l].Start; c <= Math.Min(view.Lines[l].End, text.Length); c++)
            {
                lineOf[c] = l;
            }
        }

        var markupByLine = markupKept.Select(s => (Line: lineOf[s.Start], s.Record)).ToList();
        for (int i = 0; i < spans.Count; i++)
        {
            if (spans[i].Span.Record >= 0 || spans[i].Span.Label is "person" or "company" || spans[i].Span.Label.EndsWith('?'))
            {
                continue;
            }

            int line = lineOf[spans[i].Span.Start];
            var near = markupByLine.Where(m => Math.Abs(m.Line - line) <= 3).OrderBy(m => Math.Abs(m.Line - line)).FirstOrDefault();
            if (near != default)
            {
                spans[i] = (spans[i].Span with { Record = near.Record }, spans[i].Explained);
            }
        }

        // Pieces: the model's own pieces of the page, then short windows around each markup record.
        var result = new List<Piece>();
        int first = 0;
        var pieceRanges = new List<(int First, int Last)>();
        while (first < view.Lines.Count)
        {
            int last = first;
            int pieceStart = view.Lines[first].Start;
            int next = first + 1;
            while (next < view.Lines.Count && next - first < MaxPieceLines && view.Lines[next].End - pieceStart <= MaxPieceChars)
            {
                last = next;
                next++;
            }

            pieceRanges.Add((first, last));
            first = next;
        }

        foreach ((int a, int b) in pieceRanges)
        {
            Piece? p = Cut(view, text, spans, a, b, "piece", stats);
            if (p is not null)
            {
                result.Add(p);
            }
        }

        foreach (IGrouping<int, Span> g in markupKept.GroupBy(s => s.Record))
        {
            int a = Math.Max(0, g.Min(s => lineOf[s.Start]) - WindowMargin);
            int b = Math.Min(view.Lines.Count - 1, g.Max(s => lineOf[s.Start]) + WindowMargin);
            if (b - a + 1 > MaxWindowLines || view.Lines[b].End - view.Lines[a].Start > MaxPieceChars)
            {
                continue;
            }

            Piece? p = Cut(view, text, spans, a, b, "window", stats);
            if (p is not null)
            {
                result.Add(p);
            }
        }

        return result;
    }

    private static Piece? Cut(TextView view, string text, List<(Span Span, bool Explained)> spans, int firstLine, int lastLine, string kind, Stats stats)
    {
        stats.PiecesSeen++;
        int start = view.Lines[firstLine].Start;
        int end = Math.Min(view.Lines[lastLine].End, text.Length);
        var inside = spans.Where(s => s.Span.Start >= start && s.Span.End <= end).ToList();

        // A field of the rules that the markup does not account for may sit next to people and organizations nobody labelled; the piece
        // cannot say that the rest of its text is empty, so it is dropped.
        if (inside.Any(s => !s.Explained))
        {
            stats.DroppedUnexplained++;
            return null;
        }

        // A name the rules guessed beside contact data may be a real contact the markup does not list: that piece cannot be called empty.
        foreach ((Span g, _) in inside.Where(s => s.Span.Label.EndsWith('?')))
        {
            int gl = view.LineOf(g.Start);
            if (inside.Any(c => c.Span.Label is "email" or "phone" or "address" && Math.Abs(view.LineOf(c.Span.Start) - gl) <= 3))
            {
                stats.DroppedGuessedName++;
                return null;
            }
        }

        // A span that crosses the piece's edge would be cut in two.
        if (spans.Any(s => s.Span.Start < end && start < s.Span.End && (s.Span.Start < start || s.Span.End > end)))
        {
            stats.DroppedOverlap++;
            return null;
        }

        string body = text.Substring(start, end - start);
        bool labelled = inside.Any(s => s.Span.Label is "person" or "company" && s.Span.Record >= 0);

        // Lines that look like people's names, with nothing labelled around them, mean the page lists people the markup does not: those
        // lines cannot be called empty.
        int nameLike = 0;
        for (int l = firstLine; l <= lastLine; l++)
        {
            TextLine tl = view.Lines[l];
            string line = text.Substring(tl.Start, Math.Max(0, Math.Min(tl.End, text.Length) - tl.Start));
            if (NameLike(line) && !inside.Any(s => s.Span.Start >= tl.Start && s.Span.End <= tl.End && s.Span.Label is "person"))
            {
                nameLike++;
            }
        }

        foreach (System.Text.RegularExpressions.Match m in OrgLike.Matches(body))
        {
            int ms = start + m.Index;
            int me = ms + m.Length;
            if (!inside.Any(s => s.Span.Label == "company" && s.Span.Start <= ms && me <= s.Span.End + 1 || s.Span.Label == "company" && ms <= s.Span.Start && s.Span.End <= me))
            {
                stats.DroppedOrgLike++;
                return null;
            }
        }

        if (nameLike > 0 && (!labelled || nameLike >= 3))
        {
            stats.DroppedNameLike++;
            return null;
        }

        // Contact data with no labelled name beside it: the markup knows a name there that did not show up in the text, so the lines around
        // it cannot be called empty.
        if (!labelled && inside.Any(s => s.Span.Label is "email" or "phone" or "address"))
        {
            stats.DroppedUnexplained++;
            return null;
        }

        var piece = new Piece { Kind = labelled ? kind : "negative-" + kind, Text = body };
        foreach ((Span s, _) in inside.Where(x => !x.Span.Label.EndsWith('?')))
        {
            piece.Spans.Add(s with { Start = s.Start - start, End = s.End - start });
        }

        return piece;
    }

    private static readonly string[] Titles = ["Dr", "Dr.", "Mr", "Mr.", "Mrs", "Mrs.", "Ms", "Ms.", "Miss", "Prof", "Prof.", "Rev", "Rev.", "Hon.", "Judge", "Pastor", "Father", "Sister"];

    // A legal form after capitalized words ("Acme Plumbing LLC"): an organization the page names whether or not the markup does.
    private static readonly System.Text.RegularExpressions.Regex OrgLike = new(
        @"\b[A-Z][\w&'.-]*(?:\s+[A-Z&][\w&'.-]*)*\s+(?:Inc\.?|LLC|L\.L\.C\.|Ltd\.?|Corp\.?|Corporation|Company|LLP|PLLC|P\.C\.|Co\.|Associates|Group|Partners|Services|Realty|Dental|Law Office|Law Firm|Clinic)(?=\W|$)",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private static bool NameLike(string line)
    {
        // A given name from the library's list followed by one or two more capitalized words ("Jane Q. Smith", "Dr. Alan Turing, MD"): the
        // shape of a person in a staff list. Menu words ("About Us", "Privacy Policy") are not given names, so they do not count.
        string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int skip = words.Length > 0 && Titles.Contains(words[0]) ? 1 : 0;
        if (words.Length - skip is < 2 or > 5 || line.Length > 60)
        {
            return false;
        }

        string first = words[skip].Trim(',').ToLowerInvariant();
        string last = words[^1].Trim(',', '.').ToLowerInvariant();
        if (!RecursiveAI.OneContact.Extraction.Text.NameLists.Given.Contains(first) || words.Skip(skip).Any(w => !char.IsUpper(w[0]) || w.Any(char.IsDigit)))
        {
            return false;
        }

        return RecursiveAI.OneContact.Extraction.Text.NameLists.Family.Contains(last) || words.Length - skip >= 3 || last.Length <= 3;
    }

    private static bool UsableName(string name, string label)
    {
        if (name.Length < 3 || name.Length > 80 || name.Contains('\n', StringComparison.Ordinal) || !char.IsUpper(name[0]) && !char.IsDigit(name[0]))
        {
            return false;
        }

        if (label == "person")
        {
            string[] words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return words.Length is >= 2 and <= 5 && words.All(w => char.IsLetter(w[0]) && (char.IsUpper(w[0]) || w.Length <= 3));
        }

        return true;
    }

    private static string TypeOfName(ExtractedField f, ContactRecord? record)
    {
        if (f.Kind == FieldKind.Organization || record?.Kind == ContactRecordKind.Organization)
        {
            return "company";
        }

        if (record?.Kind == ContactRecordKind.Person)
        {
            return "person";
        }

        return f.Normalized.Kind == ContactKind.Company ? "company" : "person";
    }

    private static string EmailKey(string value)
    {
        string v = value.Trim().ToLowerInvariant();
        return v.StartsWith("mailto:", StringComparison.Ordinal) ? v[7..] : v;
    }

    private static string PhoneKey(string value)
    {
        string d = new(value.Where(char.IsDigit).ToArray());
        return d.Length > 10 ? d[^10..] : d;
    }

    private static string SiteKey(string value)
    {
        string v = value.Trim().ToLowerInvariant();
        int slash = v.IndexOf("//", StringComparison.Ordinal);
        if (slash >= 0)
        {
            v = v[(slash + 2)..];
        }

        v = v.Split('/', '?', '#')[0];
        return v.StartsWith("www.", StringComparison.Ordinal) ? v[4..] : v;
    }

    private static IEnumerable<string> AddressTokens(string value) =>
        value.Split([' ', ',', '\n', '.', '#', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .Where(t => t.Length >= 3 || t.Any(char.IsDigit));

    private static bool AddressExplained(string value, HashSet<string> markupTokens)
    {
        string[] tokens = [.. AddressTokens(value)];
        return tokens.Length > 0 && markupTokens.Count > 0 && tokens.Count(markupTokens.Contains) * 2 >= tokens.Length;
    }
}
