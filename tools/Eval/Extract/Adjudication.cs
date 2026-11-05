using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Extraction.Markup;
using RecursiveAI.OneContact.Extraction.Text;
using static OneContact.Eval.Extract.Io;

namespace OneContact.Eval.Extract;

/// <summary>
/// The reviewer's side of labelling. <c>review</c> writes a compact view of each item with the extractor's pre-label beside the text it
/// came from; <c>gold</c> applies the reviewer's corrections (drop a pre-labelled field, add a field by its text, replace the grouping)
/// and writes the gold file, resolving every added text to a span of the page or item. Nothing is printed except counts.
/// </summary>
internal static class Adjudication
{
    private static readonly Regex Interesting = new(
        @"\d{3,}|\b[A-Z]\d[A-Z] ?\d[A-Z]\d\b|\b[A-Z]{2},? \d{5}\b|@|©|\b(?:Inc|LLC|Ltd|Corp|Co|Company|Associates|Group|Clinic|Dental|Law|Realty|Services|Church|School|Center|Centre)\b|\b(?:Director|Manager|Owner|President|Attorney|Dr|MD|DDS|DMD|CEO|Founder|Broker|Realtor|Principal|Partner|Pastor|Coach|Agent|Staff|Contact|Phone|Tel|Fax|Email|Address|Call|Visit)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex Postal = new(@"\b[A-Z]\d[A-Z] ?\d[A-Z]\d\b|\b[A-Z]{2},? \d{5}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private sealed record Field(string Type, int Start, int End, string Text, string Source);

    private sealed record Pre(List<Field> Fields, List<List<int>> Records);

    private static Pre ReadPre(JsonElement row)
    {
        var fields = new List<Field>();
        foreach (JsonElement f in row.GetProperty("fields").EnumerateArray())
        {
            fields.Add(new Field(f.GetProperty("type").GetString()!, f.GetProperty("start").GetInt32(), f.GetProperty("end").GetInt32(),
                f.GetProperty("text").GetString()!, f.TryGetProperty("src", out JsonElement s) ? s.GetString()! : string.Empty));
        }

        var records = new List<List<int>>();
        if (row.TryGetProperty("records", out JsonElement recs))
        {
            foreach (JsonElement r in recs.EnumerateArray())
            {
                records.Add([.. r.EnumerateArray().Select(x => x.GetInt32())]);
            }
        }

        return new Pre(fields, records);
    }

    private static string Clip(string s, int n)
    {
        s = s.Replace('\n', '⏎').Replace('\r', ' ');
        return s.Length <= n ? s : s[..n] + "…";
    }

    public static int Review(string source, string prePath, string output)
    {
        bool web = Directory.Exists(source);
        var pre = Lines(prePath).ToDictionary(r => r.GetProperty("id").GetString()!, ReadPre);
        using StreamWriter o = new(output, false, new UTF8Encoding(false));
        int n = 0;
        if (web)
        {
            foreach (JsonElement page in Lines(Path.Combine(source, "web-picked.jsonl")))
            {
                string id = page.GetProperty("id").GetString()!;
                var (_, view) = ViewOf(source, page);
                Pre p = pre[id];
                o.WriteLine($"## {id} {page.GetProperty("country").GetString()} {page.GetProperty("group").GetString()}");
                WriteFields(o, p, web: true);
                string[] lines = view.Text.Split('\n');
                var shown = new SortedSet<int>(Enumerable.Range(0, Math.Min(2, lines.Length)));
                for (int i = 0; i < lines.Length; i++)
                {
                    string t = lines[i].Trim();
                    if (t.Length is > 0 and < 160 && (Interesting.IsMatch(t) || p.Fields.Any(f => f.Text.Length > 2 && t.Contains(f.Text.Trim(), StringComparison.Ordinal))))
                    {
                        shown.Add(i);
                        if (Postal.IsMatch(t) && i > 0)
                        {
                            shown.Add(i - 1); // the street line of an address sits just above its postal code
                        }
                    }
                }

                int printed = 0;
                foreach (int i in shown)
                {
                    if (printed++ >= 45)
                    {
                        o.WriteLine("  …");
                        break;
                    }

                    string t = lines[i].Trim();
                    string marks = string.Concat(p.Fields.Select((f, k) => f.Text.Length > 2 && t.Contains(f.Text.Trim(), StringComparison.Ordinal) ? $"[P{k}]" : string.Empty));
                    o.WriteLine($"  | {Clip(t, 110)} {marks}");
                }

                n++;
            }
        }
        else
        {
            foreach (JsonElement item in Lines(source))
            {
                string id = item.GetProperty("id").GetString()!;
                Pre p = pre[id];
                o.WriteLine($"## {id} {item.GetProperty("country").GetString()} {item.GetProperty("stratum").GetString()}");
                WriteFields(o, p, web: false);
                o.WriteLine("  | " + item.GetProperty("text").GetString()!.Replace("\n", "\n  | "));
                n++;
            }
        }

        Console.WriteLine($"reviewed {n}");
        return 0;
    }

    private static void WriteFields(StreamWriter o, Pre p, bool web)
    {
        for (int k = 0; k < p.Fields.Count; k++)
        {
            Field f = p.Fields[k];
            int rec = p.Records.FindIndex(r => r.Contains(k));
            o.WriteLine($"  P{k} {f.Type} {(web ? f.Source : string.Empty)} r{rec} \"{Clip(f.Text, 70)}\"");
        }
    }

    /// <summary>Writes lines <paramref name="from"/> to <paramref name="from"/> + <paramref name="count"/> of one page's visible text.</summary>
    public static int ShowLines(string dir, string id, int from, int count)
    {
        JsonElement page = Pages(dir).First(p => p.GetProperty("id").GetString() == id);
        var (_, view) = ViewOf(dir, page);
        string[] lines = view.Text.Split('\n');
        for (int i = from; i < Math.Min(lines.Length, from + count); i++)
        {
            Console.WriteLine($"{i}| {Clip(lines[i].Trim(), 140)}");
        }

        return 0;
    }

    public static int BuildGold(string source, string prePath, string correctionsPath, string output)
    {
        bool web = Directory.Exists(source);
        var pre = Lines(prePath).ToDictionary(r => r.GetProperty("id").GetString()!, ReadPre);
        var corrections = File.Exists(correctionsPath)
            ? Lines(correctionsPath).ToDictionary(r => r.GetProperty("id").GetString()!)
            : [];
        using StreamWriter o = new(output, false, new UTF8Encoding(false));
        int n = 0, agreed = 0, skipped = 0, corrected = 0, unresolved = 0;
        IEnumerable<JsonElement> items = web ? Lines(Path.Combine(source, "web-picked.jsonl")) : Lines(source);
        foreach (JsonElement item in items)
        {
            string id = item.GetProperty("id").GetString()!;
            corrections.TryGetValue(id, out JsonElement corr);
            if (corr.ValueKind == JsonValueKind.Object && corr.TryGetProperty("skip", out JsonElement sk) && sk.GetBoolean())
            {
                skipped++;
                continue;
            }

            Pre p = pre[id];
            var drop = new HashSet<int>();
            if (corr.ValueKind == JsonValueKind.Object && corr.TryGetProperty("drop", out JsonElement d))
            {
                drop.UnionWith(d.EnumerateArray().Select(x => x.GetInt32()));
            }

            if (corr.ValueKind == JsonValueKind.Object && corr.TryGetProperty("dropType", out JsonElement dt))
            {
                var types = dt.EnumerateArray().Select(x => x.GetString()).ToHashSet();
                for (int k = 0; k < p.Fields.Count; k++)
                {
                    if (types.Contains(p.Fields[k].Type))
                    {
                        drop.Add(k);
                    }
                }
            }

            // Where an added text is searched: the visible text of a page (mapped back to bytes), or the decoded page for markup-only text.
            TextView? view = null;
            PageContext? ctx = null;
            if (web)
            {
                (ctx, view) = ViewOf(source, item);
            }

            string plain = web ? string.Empty : item.GetProperty("text").GetString()!;
            var fields = new List<(string Id, Field F, bool Pre)>();
            for (int k = 0; k < p.Fields.Count; k++)
            {
                if (!drop.Contains(k) && p.Fields[k].Start >= 0)
                {
                    fields.Add(("P" + k, p.Fields[k], true));
                }
            }

            int added = 0;
            var joins = new List<(string Ref, int Rec, string? Group)>();
            if (corr.ValueKind == JsonValueKind.Object && corr.TryGetProperty("add", out JsonElement adds))
            {
                foreach (JsonElement a in adds.EnumerateArray())
                {
                    string type = a.GetProperty("type").GetString()!;
                    string text = a.GetProperty("text").GetString()!;
                    bool all = a.TryGetProperty("all", out JsonElement al) && al.GetBoolean();
                    bool raw = a.TryGetProperty("raw", out JsonElement rw) && rw.GetBoolean();
                    int nth = a.TryGetProperty("nth", out JsonElement nt) ? nt.GetInt32() : 0;
                    string? context = a.TryGetProperty("ctx", out JsonElement cx) ? cx.GetString() : null;
                    var spans = Resolve(web, view, ctx, plain, text, raw, context).ToList();

                    if (spans.Count == 0)
                    {
                        unresolved++;
                        added++;
                        continue;
                    }

                    var pick = all ? spans : spans.Skip(nth).Take(1).ToList();
                    foreach ((int s, int e) in pick)
                    {
                        if (fields.Any(x => x.F.Type == type && x.F.Start == s && x.F.End == e))
                        {
                            continue; // already a field (the markup value is the visible text itself)
                        }

                        fields.Add(("A" + added, new Field(type, s, e, text, raw ? "Markup" : "Text"), false));
                    }

                    if (a.TryGetProperty("rec", out JsonElement rj))
                    {
                        joins.Add(("A" + added, rj.GetInt32(), null));
                    }
                    else if (a.TryGetProperty("grp", out JsonElement gj))
                    {
                        joins.Add(("A" + added, -1, gj.GetString()));
                    }

                    added++;
                }
            }

            bool regrouped = corr.ValueKind == JsonValueKind.Object && corr.TryGetProperty("records", out _);
            List<List<string>> records = regrouped
                ? [.. corr.GetProperty("records").EnumerateArray().Select(r => r.EnumerateArray().Select(x => x.GetString()!).ToList())]
                : [.. p.Records.Select(r => r.Select(x => "P" + x).ToList())];
            if (!regrouped)
            {
                foreach (var j in joins.Where(j => j.Rec >= 0 && j.Rec < records.Count))
                {
                    records[j.Rec].Add(j.Ref);
                }

                foreach (var g in joins.Where(j => j.Group is not null).GroupBy(j => j.Group))
                {
                    records.Add([.. g.Select(j => j.Ref)]);
                }
            }

            // Several fields can carry one reference (an added text found in more than one place with "all"): they all join the record.
            var idOf = new Dictionary<string, List<int>>();
            for (int i = 0; i < fields.Count; i++)
            {
                if (!idOf.TryGetValue(fields[i].Id, out List<int>? l))
                {
                    idOf[fields[i].Id] = l = [];
                }

                l.Add(i);
            }

            bool unchanged = drop.Count == 0 && added == 0 && !regrouped;
            if (unchanged)
            {
                agreed++;
            }
            else
            {
                corrected++;
            }

            o.WriteLine(Json(w =>
            {
                w.WriteStartObject();
                w.WriteString("id", id);
                w.WriteString("kind", web ? "web" : "text");
                w.WriteString("country", item.GetProperty("country").GetString());
                w.WriteString(web ? "group" : "stratum", item.GetProperty(web ? "group" : "stratum").GetString());
                w.WriteBoolean("rules_agreed", unchanged);
                if (web)
                {
                    w.WriteString("file", "pages/" + id + ".html");
                }
                else
                {
                    w.WriteString("text", plain);
                }

                w.WriteStartArray("fields");
                for (int i = 0; i < fields.Count; i++)
                {
                    w.WriteStartObject();
                    w.WriteNumber("id", i);
                    w.WriteString("type", fields[i].F.Type);
                    w.WriteNumber("start", fields[i].F.Start);
                    w.WriteNumber("end", fields[i].F.End);
                    w.WriteString("text", fields[i].F.Text);
                    w.WriteString("src", fields[i].F.Source);
                    w.WriteBoolean("pre", fields[i].Pre);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
                w.WriteStartArray("records");
                foreach (List<string> r in records)
                {
                    var members = r.SelectMany(x => idOf.TryGetValue(x, out List<int>? l) ? l : []).Distinct().Order().ToList();
                    if (members.Count == 0)
                    {
                        continue;
                    }

                    w.WriteStartArray();
                    foreach (int m in members)
                    {
                        w.WriteNumberValue(m);
                    }

                    w.WriteEndArray();
                }

                w.WriteEndArray();
                w.WriteEndObject();
            }));
            n++;
        }

        Console.WriteLine($"gold items {n} (pre-label accepted unchanged {agreed}, corrected {corrected}, skipped {skipped}); added texts not found {unresolved}");
        return unresolved == 0 ? 0 : 3;
    }

    /// <summary>
    /// Every place <paramref name="text"/> occurs. With <paramref name="context"/>, only the occurrences inside that longer phrase count
    /// ("Copyright © Acme" picks out the name in the copyright line).
    /// </summary>
    private static IEnumerable<(int Start, int End)> Resolve(bool web, TextView? view, PageContext? ctx, string plain, string text, bool raw, string? context)
    {
        string needle = context ?? text;
        int inner = context is null ? 0 : Math.Max(0, context.IndexOf(text, StringComparison.Ordinal));
        if (!web)
        {
            for (int i = plain.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = plain.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            {
                yield return (i + inner, i + inner + text.Length);
            }

            yield break;
        }

        if (raw)
        {
            string page = ctx!.Text;
            for (int i = page.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = page.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            {
                SourceSpan s = ctx.ToSpan(new TextRange(i + inner, i + inner + text.Length));
                yield return (s.Start, s.Start + s.Length);
            }

            yield break;
        }

        string v = view!.Text;
        for (int i = v.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = v.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
        {
            TextRange r = view.RangeOf(i + inner, i + inner + text.Length, out _, out _);
            if (r.IsPresent)
            {
                SourceSpan s = ctx!.ToSpan(r);
                yield return (s.Start, s.Start + s.Length);
            }
        }
    }
}
