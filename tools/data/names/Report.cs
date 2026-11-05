using System.Text;
using System.Text.Json;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Names;

namespace OneContact.Data.Names;

/// <summary>
/// Runs the rule-based name parser over a deterministic sample of the dev split and says how often rules alone give the gold class and the gold
/// parts. This is information for the model unit (what is left for the head); it never feeds back into the labels.
/// </summary>
internal static class RuleReport
{
    private sealed record Gold(string Id, string Text, string Class, string Pattern, string Lang, string Region, string Noise, HashSet<(string, int, int)> Parts);

    private sealed class Tally
    {
        internal int N;
        internal int ClassOk;
        internal int PartsOk;
        internal int AllOk;
    }

    internal static void Run(string outDir, int sampleSize)
    {
        List<string> files = Directory.GetFiles(Path.Combine(outDir, "dev"), "*.jsonl").OrderBy(f => f, StringComparer.Ordinal).ToList();
        long total = 0;
        foreach (string f in files)
        {
            total += File.ReadLines(f).LongCount();
        }

        long step = Math.Max(1, total / sampleSize);
        List<Gold> sample = [];
        foreach (string f in files)
        {
            foreach (string line in File.ReadLines(f))
            {
                using JsonDocument doc = JsonDocument.Parse(line);
                JsonElement root = doc.RootElement;
                string id = root.GetProperty("id").GetString()!;
                if ((long)(Prng.Hash(id, 77) % (ulong)step) != 0)
                {
                    continue;
                }

                HashSet<(string, int, int)> parts = [];
                foreach (JsonElement p in root.GetProperty("parts").EnumerateArray())
                {
                    parts.Add((p[0].GetString()!, p[1].GetInt32(), p[2].GetInt32()));
                }

                sample.Add(new Gold(
                    id,
                    root.GetProperty("text").GetString()!,
                    root.GetProperty("class").GetString()!,
                    root.GetProperty("pattern").GetString()!,
                    root.GetProperty("lang").GetString()!,
                    root.GetProperty("region").GetString()!,
                    root.TryGetProperty("noise", out JsonElement nz) ? nz.GetString()! : "",
                    parts));
            }
        }

        SortedDictionary<string, Tally> byClass = new(StringComparer.Ordinal);
        SortedDictionary<string, Tally> byPattern = new(StringComparer.Ordinal);
        SortedDictionary<string, Tally> byLang = new(StringComparer.Ordinal);
        SortedDictionary<string, (int Gold, int Found)> byLabel = new(StringComparer.Ordinal);
        SortedDictionary<string, int> confusion = new(StringComparer.Ordinal);
        List<string> misses = [];
        foreach (Gold g in sample)
        {
            NameHints hints = new(Region.Parse(g.Region), g.Lang);
            NameParseResult res = NameParsing.Split(g.Text, hints);
            string predicted = res.Class.ToString().ToLowerInvariant();
            HashSet<(string, int, int)> got = Predicted(res);

            bool classOk = predicted == g.Class;
            bool partsOk = g.Class == "unknown" || got.SetEquals(g.Parts);
            bool all = classOk && partsOk;
            foreach (string key in new[] { g.Class })
            {
                Count(byClass, key, classOk, partsOk, all);
            }

            Count(byPattern, g.Class + "/" + g.Pattern, classOk, partsOk, all);
            Count(byLang, g.Class + "/" + g.Lang + "-" + g.Region, classOk, partsOk, all);
            string conf = g.Class + " -> " + predicted;
            confusion[conf] = confusion.GetValueOrDefault(conf) + 1;
            foreach ((string label, int s, int e) in g.Parts)
            {
                (int gc, int fc) = byLabel.GetValueOrDefault(label);
                byLabel[label] = (gc + 1, fc + (got.Contains((label, s, e)) ? 1 : 0));
            }

            if (!all && misses.Count < 40 && Prng.Hash(g.Id, 5) % 7 == 0)
            {
                misses.Add($"{g.Class}/{g.Pattern} [{g.Lang}-{g.Region}] \"{g.Text.Replace("\n", "\\n", StringComparison.Ordinal)}\" rules: {predicted}; gold {Fmt(g.Text, g.Parts)} got {Fmt(g.Text, got)}");
            }
        }

        StringBuilder md = new();
        md.AppendLine($"Rule parser over {sample.Count} dev strings (every {step}th by id hash, of {total} dev examples).");
        md.AppendLine();
        md.AppendLine("| class | n | class right | parts right (given gold) | class and parts right |");
        md.AppendLine("|---|---|---|---|---|");
        foreach ((string k, Tally t) in byClass)
        {
            md.AppendLine($"| {k} | {t.N} | {Pct(t.ClassOk, t.N)} | {Pct(t.PartsOk, t.N)} | {Pct(t.AllOk, t.N)} |");
        }

        Tally allT = new();
        foreach (Tally t in byClass.Values)
        {
            allT.N += t.N;
            allT.ClassOk += t.ClassOk;
            allT.PartsOk += t.PartsOk;
            allT.AllOk += t.AllOk;
        }

        md.AppendLine($"| all | {allT.N} | {Pct(allT.ClassOk, allT.N)} | {Pct(allT.PartsOk, allT.N)} | {Pct(allT.AllOk, allT.N)} |");
        md.AppendLine();
        md.AppendLine("Class confusion (gold -> rules):");
        foreach ((string k, int v) in confusion)
        {
            md.AppendLine($"- {k}: {v}");
        }

        md.AppendLine();
        md.AppendLine("By language and region:");
        md.AppendLine("| class/lang | n | class right | parts right | both |");
        md.AppendLine("|---|---|---|---|---|");
        foreach ((string k, Tally t) in byLang)
        {
            md.AppendLine($"| {k} | {t.N} | {Pct(t.ClassOk, t.N)} | {Pct(t.PartsOk, t.N)} | {Pct(t.AllOk, t.N)} |");
        }

        md.AppendLine();
        md.AppendLine("By pattern:");
        md.AppendLine("| class/pattern | n | class right | parts right | both |");
        md.AppendLine("|---|---|---|---|---|");
        foreach ((string k, Tally t) in byPattern)
        {
            md.AppendLine($"| {k} | {t.N} | {Pct(t.ClassOk, t.N)} | {Pct(t.PartsOk, t.N)} | {Pct(t.AllOk, t.N)} |");
        }

        md.AppendLine();
        md.AppendLine("Gold parts found exactly (label, span) by the rules:");
        foreach ((string k, (int gc, int fc)) in byLabel)
        {
            md.AppendLine($"- {k}: {fc}/{gc} ({Pct(fc, gc)})");
        }

        md.AppendLine();
        md.AppendLine("Examples where rules alone were wrong (synthetic strings):");
        foreach (string m in misses)
        {
            md.AppendLine("- " + m);
        }

        string path = Path.Combine(outDir, "rule-report.md");
        File.WriteAllText(path, md.ToString());
        Console.WriteLine(md.ToString());
    }

    private static void Count(SortedDictionary<string, Tally> map, string key, bool classOk, bool partsOk, bool all)
    {
        if (!map.TryGetValue(key, out Tally? t))
        {
            map[key] = t = new Tally();
        }

        t.N++;
        t.ClassOk += classOk ? 1 : 0;
        t.PartsOk += partsOk ? 1 : 0;
        t.AllOk += all ? 1 : 0;
    }

    private static string Pct(int a, int n) => n == 0 ? "-" : (100.0 * a / n).ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "%";

    private static string Fmt(string text, HashSet<(string, int, int)> parts)
    {
        StringBuilder sb = new("{");
        foreach ((string l, int s, int e) in parts.OrderBy(p => p.Item2).ThenBy(p => p.Item3))
        {
            if (e <= text.Length && s >= 0 && s <= e)
            {
                sb.Append(l).Append('=').Append(text.AsSpan(s, e - s)).Append("; ");
            }
        }

        return sb.Append('}').ToString();
    }

    private static HashSet<(string, int, int)> Predicted(NameParseResult res)
    {
        HashSet<(string, int, int)> got = [];
        void Add(string label, NamePart part)
        {
            if (!part.IsEmpty)
            {
                got.Add((label, part.Span.Start, part.Span.End));
            }
        }

        foreach (PersonName p in res.Persons)
        {
            Add("Prefix", p.Prefix);
            Add("Given", p.Given);
            Add("Middle", p.Middle);
            Add("Nickname", p.Nickname);
            Add("Particle", p.Particle);
            Add("Family", p.Family);
            Add("SecondFamily", p.SecondFamily);
            Add("Suffix", p.Suffix);
            Add("ProfessionalSuffix", p.ProfessionalSuffix);
        }

        if (res.Class == NameClass.Company)
        {
            CompanyName c = res.Company;
            Add("LeadingArticle", c.LeadingArticle);
            Add("Base", c.Base);
            if (c.LegalForm is { } lf && lf.Span.Length > 0)
            {
                got.Add(("LegalForm", lf.Span.Start, lf.Span.End));
            }

            Add("TradingAs", c.TradingAs);
            Add("CareOf", c.CareOf);
        }

        return got;
    }
}
