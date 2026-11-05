using System.Buffers;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Addresses.Parsing;

namespace OneContact.Data.AddressUsCa;

/// <summary>
/// Checks the written files: every example's labelled spans are in order, inside the text, not overlapping, on token edges, and
/// cover every letter and digit of the text; and a sample is run through the library's rule parser to report how often the rules
/// alone recover every labelled field (information for the model recipe: it shows where rules leave work).
/// </summary>
internal static class Checker
{
    private static readonly HashSet<string> LeadingLabels = new(StringComparer.Ordinal) { "recipient", "organization" };

    internal static int Run(Dictionary<string, string> o)
    {
        string dir = o["out"];
        int sample = int.Parse(o.GetValueOrDefault("sample", "5000"));
        int perRegion = int.Parse(o.GetValueOrDefault("per-region", "0"));
        int failures = 0;
        StringBuilder report = new();
        foreach (string name in new[] { "us.train.jsonl", "us.dev.jsonl", "ca.train.jsonl", "ca.dev.jsonl" })
        {
            string path = Path.Combine(dir, name);
            if (!File.Exists(path))
            {
                continue;
            }

            long lines = 0;
            long bad = 0;
            List<string> kept = [];
            long total = File.ReadLines(path).LongCount();
            long step = Math.Max(1, total / Math.Max(1, name.Contains("train", StringComparison.Ordinal) ? sample : sample / 10));
            foreach (string line in File.ReadLines(path))
            {
                string? problem = Validate(line);
                if (problem is not null)
                {
                    bad++;
                    if (bad <= 3)
                    {
                        report.AppendLine($"{name}: {problem} :: {line[..Math.Min(line.Length, 330)]}");
                    }
                }

                if (lines % step == 0 && name.Contains("train", StringComparison.Ordinal))
                {
                    kept.Add(line);
                }

                lines++;
            }

            failures += (int)bad;
            report.AppendLine($"{name}: {lines} examples, {bad} failed the span checks");
            if (kept.Count > 0)
            {
                report.Append(RuleReport(name[..2].ToUpperInvariant(), kept, brief: false));
            }

            if (perRegion > 0 && name.Contains("train", StringComparison.Ordinal))
            {
                report.Append(RegionReports(path, perRegion));
            }
        }

        Console.Write(report.ToString());
        File.WriteAllText(Path.Combine(dir, "check.txt"), report.ToString(), new UTF8Encoding(false));
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Null when the example is sound, otherwise the first problem found.</summary>
    internal static string? Validate(string line)
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;
        string text = root.GetProperty("text").GetString()!;
        List<(int S, int E)> tokens = root.GetProperty("tokens").EnumerateArray().Select(t => (t[0].GetInt32(), t[1].GetInt32())).ToList();
        HashSet<int> starts = tokens.Select(t => t.S).ToHashSet();
        HashSet<int> ends = tokens.Select(t => t.E).ToHashSet();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].S >= tokens[i].E || tokens[i].E > text.Length || (i > 0 && tokens[i].S < tokens[i - 1].E))
            {
                return "tokens out of order or outside the text";
            }
        }

        bool[] covered = new bool[text.Length];
        int previousEnd = 0;
        foreach (JsonElement e in root.GetProperty("entities").EnumerateArray())
        {
            int s = e[0].GetInt32();
            int en = e[1].GetInt32();
            string label = e[2].GetString()!;
            if (s < previousEnd)
            {
                return $"span {label} overlaps or is out of order";
            }

            if (s >= en || en > text.Length)
            {
                return $"span {label} outside the text";
            }

            if (!starts.Contains(s) || !ends.Contains(en))
            {
                return $"span {label} does not sit on token edges";
            }

            if (char.IsWhiteSpace(text[s]) || char.IsWhiteSpace(text[en - 1]))
            {
                return $"span {label} starts or ends in a space";
            }

            for (int k = s; k < en; k++)
            {
                covered[k] = true;
            }

            previousEnd = en;
        }

        for (int k = 0; k < text.Length; k++)
        {
            if (char.IsLetterOrDigit(text[k]) && !covered[k])
            {
                return "a letter or digit outside every span";
            }
        }

        return null;
    }

    /// <summary>The rule parser over an even sample of each region of a training file (the region is the last property of every line).</summary>
    private static string RegionReports(string path, int perRegion)
    {
        Dictionary<string, long> counts = new(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(path))
        {
            string r = RegionOf(line);
            counts[r] = counts.GetValueOrDefault(r) + 1;
        }

        Dictionary<string, long> seen = new(StringComparer.Ordinal);
        Dictionary<string, List<string>> kept = new(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(path))
        {
            string r = RegionOf(line);
            long step = Math.Max(1, counts[r] / perRegion);
            long at = seen.GetValueOrDefault(r);
            seen[r] = at + 1;
            if (at % step == 0)
            {
                if (!kept.TryGetValue(r, out List<string>? l))
                {
                    kept[r] = l = [];
                }

                l.Add(line);
            }
        }

        StringBuilder sb = new();
        foreach (KeyValuePair<string, List<string>> kv in kept.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            sb.Append(RuleReport(kv.Key, kv.Value, brief: true));
        }

        return sb.ToString();
    }

    private static string RegionOf(string line)
    {
        int at = line.LastIndexOf("\"region\":\"", StringComparison.Ordinal);
        return at < 0 ? "?" : line.Substring(at + 10, 2);
    }

    private static string RuleReport(string country, List<string> lines, bool brief)
    {
        Dictionary<string, (long Seen, long Found)> perLabel = new(StringComparer.Ordinal);
        Dictionary<string, long> statuses = new(StringComparer.Ordinal);
        long examples = 0;
        long allFound = 0;
        long allFoundNoExtra = 0;
        long fieldsSeen = 0;
        long fieldsFound = 0;
        foreach (string line in lines)
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            string text = doc.RootElement.GetProperty("text").GetString()!;
            List<(int S, int E, string L)> gold = doc.RootElement.GetProperty("entities").EnumerateArray().Select(e => (e[0].GetInt32(), e[1].GetInt32(), e[2].GetString()!)).ToList();
            examples++;
            OperationStatus status = RuleAddressParser.TryParse(text, Region.Unknown, out ParsedAddress? parsed, out ParseDetail detail);
            if (status != OperationStatus.Done || parsed is null)
            {
                string key = "failed: " + detail.PrimaryReason.Id;
                statuses[key] = statuses.GetValueOrDefault(key) + 1;
                foreach ((int _, int _, string l) in gold)
                {
                    Bump(perLabel, l, false);
                    fieldsSeen++;
                }

                continue;
            }

            statuses["parsed"] = statuses.GetValueOrDefault("parsed") + 1;
            HashSet<(string, int, int)> got = parsed.Fields.Select(f => (f.Name, f.Span.Start, f.Span.End)).ToHashSet();
            bool all = true;
            foreach ((int s, int e, string l) in gold)
            {
                string expected = LeadingLabels.Contains(l) ? "leading-line" : l;
                bool hit = got.Contains((expected, s, e));
                Bump(perLabel, l, hit);
                fieldsSeen++;
                if (hit)
                {
                    fieldsFound++;
                }
                else
                {
                    all = false;
                }
            }

            if (all)
            {
                allFound++;
                HashSet<(string, int, int)> goldKeys = gold.Select(g => (LeadingLabels.Contains(g.L) ? "leading-line" : g.L, g.S, g.E)).ToHashSet();
                if (got.All(goldKeys.Contains))
                {
                    allFoundNoExtra++;
                }
            }
        }

        StringBuilder sb = new();
        sb.AppendLine($"  rule parser on {examples} sampled {country} training examples:");
        sb.AppendLine($"    every labelled field recovered (same label and span): {allFound} ({100.0 * allFound / examples:0.0}%); with no extra field: {allFoundNoExtra} ({100.0 * allFoundNoExtra / examples:0.0}%)");
        sb.AppendLine($"    fields recovered: {fieldsFound}/{fieldsSeen} ({100.0 * fieldsFound / Math.Max(1, fieldsSeen):0.0}%)");
        if (brief)
        {
            return sb.ToString();
        }

        foreach (KeyValuePair<string, long> kv in statuses.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"    {kv.Key}: {kv.Value}");
        }

        foreach (KeyValuePair<string, (long Seen, long Found)> kv in perLabel.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            sb.AppendLine($"    label {kv.Key}: {kv.Value.Found}/{kv.Value.Seen} ({100.0 * kv.Value.Found / Math.Max(1, kv.Value.Seen):0.0}%)");
        }

        return sb.ToString();
    }

    private static void Bump(Dictionary<string, (long Seen, long Found)> d, string label, bool hit)
    {
        (long seen, long found) = d.GetValueOrDefault(label);
        d[label] = (seen + 1, found + (hit ? 1 : 0));
    }
}
