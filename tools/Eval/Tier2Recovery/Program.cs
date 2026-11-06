using System.Buffers;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Addresses.Parsing;

// usage: eval-tier2-recovery <dir with <cc>.dev.jsonl files> <examples per country> <countries, comma separated> [misses file]
//
// For each country takes an even sample of its dev file (all of it when it is smaller), runs the rule parser alone, and reports how often every labelled field
// is recovered with the same label and the same span, with the country given as a hint and without one. A refusal to the model counts as not recovered and is
// listed by reason. The labels are the dev files' own (street.type, street.name, locality, ...); a recipient or organization line is the parser's leading line.
// A label the parser does not produce for the country (the admin area of Spain, Italy and Andorra, the postcode of Ireland) is reported on its own.
if (args.Length < 3)
{
    Console.Error.WriteLine("usage: eval-tier2-recovery <dir> <examples per country> <countries> [misses file]");
    return 64;
}

int per = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
string? misses = args.Length > 3 ? args[3] : null;
if (misses is not null)
{
    File.WriteAllText(misses, string.Empty);
    File.WriteAllText(misses + ".nohint", string.Empty);
}

HashSet<string> leading = new(StringComparer.Ordinal) { "recipient", "organization" };
foreach (string country in args[2].Split(','))
{
    string path = Path.Combine(args[0], country.ToLowerInvariant() + ".dev.jsonl");
    List<string> all = [.. File.ReadLines(path).Where(l => l.Length > 0)];
    double step = Math.Max(1.0, (double)all.Count / per);
    List<string> sample = [];
    for (double at = 0; (int)at < all.Count && sample.Count < per; at += step)
    {
        sample.Add(all[(int)at]);
    }

    // Some registers (the Czech, Croatian, Hungarian and Australian renders) label a street's type as part of its name, and a house-number marker with the number
    // ("nr. 68"). The parser keeps the type and the marker apart, so for a sample whose gold has no street.type label the parts are joined before comparing.
    bool typeInName = !sample.Any(l => l.Contains("\"street.type\"", StringComparison.Ordinal));

    // Some registers (the Lithuanian one) keep the country letters inside the postcode ("LT-02216"); the rest label them apart. The gold of the sample says which.
    bool prefixInPostcode = sample.Any(l => HasPrefixedPostcode(l));
    foreach (bool withHint in new[] { false, true })
    {
        Region hint = withHint ? Region.Parse(country) : Region.Unknown;
        int examples = 0, everyField = 0, noExtra = 0, wrongCountry = 0;
        long seen = 0, found = 0;
        Dictionary<string, int> refusals = new(StringComparer.Ordinal);
        Dictionary<string, (int Seen, int Found)> perLabel = new(StringComparer.Ordinal);
        foreach (string line in sample)
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            string text = doc.RootElement.GetProperty("text").GetString()!;
            List<(int S, int E, string L)> gold = [.. doc.RootElement.GetProperty("entities").EnumerateArray().Select(e => (e[0].GetInt32(), e[1].GetInt32(), e[2].GetString()!))];
            examples++;
            string Lab(string l) => leading.Contains(l) ? "leading-line" : l;
            if (RuleAddressParser.TryParse(text, hint, out ParsedAddress? parsed, out ParseDetail detail) != OperationStatus.Done || parsed is null)
            {
                string reason = detail.PrimaryReason.Id;
                refusals[reason] = refusals.GetValueOrDefault(reason) + 1;
                foreach ((int _, int _, string l) in gold)
                {
                    seen++;
                    perLabel[l] = (perLabel.GetValueOrDefault(l).Seen + 1, perLabel.GetValueOrDefault(l).Found);
                }

                if (misses is not null)
                {
                    File.AppendAllText(withHint ? misses : misses + ".nohint", country + "\t" + text.Replace("\n", "\\n", StringComparison.Ordinal) + "\tREFUSED " + reason + "\n");
                }

                continue;
            }

            if (parsed.Country != Region.Parse(country))
            {
                wrongCountry++;
                if (misses is not null)
                {
                    File.AppendAllText(withHint ? misses : misses + ".nohint", country + "\t" + text.Replace("\n", "\\n", StringComparison.Ordinal) + "\tWRONGCOUNTRY " + parsed.Country + "\n");
                }
            }

            HashSet<(string, int, int)> got = MergeConvention(parsed, text, typeInName, prefixInPostcode);
            bool every = true;
            foreach ((int s, int e, string l) in gold)
            {
                bool hit = got.Contains((Lab(l), s, e));
                seen++;
                found += hit ? 1 : 0;
                perLabel[l] = (perLabel.GetValueOrDefault(l).Seen + 1, perLabel.GetValueOrDefault(l).Found + (hit ? 1 : 0));
                every &= hit;
            }

            if (every)
            {
                everyField++;
                HashSet<(string, int, int)> goldKeys = [.. gold.Select(g => (Lab(g.L), g.S, g.E))];
                noExtra += got.All(goldKeys.Contains) ? 1 : 0;
            }
            else if (misses is not null && withHint)
            {
                string missed = string.Join(" | ", gold.Where(g => !got.Contains((Lab(g.L), g.S, g.E))).Select(g => string.Concat(g.L, "=", text.AsSpan(g.S, g.E - g.S))));
                string extra = string.Join(" | ", parsed.Fields.Where(f => !gold.Any(g => Lab(g.L) == f.Name && g.S == f.Span.Start && g.E == f.Span.End)).Select(f => f.Name + "=" + f.Text));
                File.AppendAllText(misses, country + "\t" + text.Replace("\n", "\\n", StringComparison.Ordinal) + "\tMISSED " + missed + "\tEXTRA " + extra + "\n");
            }
        }

        int refused = refusals.Values.Sum();
        StringBuilder sb = new();
        sb.Append($"{country} hint={(withHint ? "yes" : "no ")} n={examples} every-field={everyField} ({100.0 * everyField / examples:0.0}%) no-extra={noExtra} ({100.0 * noExtra / examples:0.0}%) fields={found}/{seen} ({100.0 * found / Math.Max(1, seen):0.0}%) refused={refused} wrong-country={wrongCountry}");
        if (refused > 0)
        {
            sb.Append(" [" + string.Join(", ", refusals.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + " " + k.Value)) + "]");
        }

        Console.WriteLine(sb.ToString());
        if (withHint)
        {
            Console.WriteLine("    " + string.Join("; ", perLabel.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => $"{k.Key} {k.Value.Found}/{k.Value.Seen}")));
        }
    }
}


static bool HasPrefixedPostcode(string line)
{
    using JsonDocument doc = JsonDocument.Parse(line);
    string text = doc.RootElement.GetProperty("text").GetString()!;
    foreach (JsonElement e in doc.RootElement.GetProperty("entities").EnumerateArray())
    {
        if (e[2].GetString() == "postal-code")
        {
            ReadOnlySpan<char> pc = text.AsSpan(e[0].GetInt32(), e[1].GetInt32() - e[0].GetInt32());
            int dash = pc.IndexOf('-');
            if (dash is 1 or 2 && pc[..dash].ToString().All(char.IsLetter))
            {
                return true;
            }
        }
    }

    return false;
}

static HashSet<(string, int, int)> MergeConvention(ParsedAddress parsed, string text, bool typeInName, bool prefixInPostcode)
{
    List<(string Name, int S, int E)> fields = [.. parsed.Fields.Select(f => (f.Name, f.Span.Start, f.Span.End))];
    if (typeInName)
    {
        bool Adjacent(int e, int s) => s >= e && text.AsSpan(e, s - e).Trim().Length == 0;
        List<(string, int, int)> types = [.. fields.Where(f => f.Name == "street.type")];
        foreach ((string, int S, int E) ty in types)
        {
            (string, int S, int E) nm = fields.FirstOrDefault(f => f.Name == "street.name" && (Adjacent(f.E, ty.S) || Adjacent(ty.E, f.S)));
            if (nm.Item1 is not null)
            {
                fields.Remove(ty);
                fields.Remove(nm);
                fields.Add(("street.name", Math.Min(ty.S, nm.S), Math.Max(ty.E, nm.E)));
            }
        }

        foreach ((string, int S, int E) other in fields.Where(f => f.Name == "street.other").ToList())
        {
            (string, int S, int E) number = fields.FirstOrDefault(f => f.Name == "street.house-number" && Adjacent(other.E, f.S));
            if (number.Item1 is not null && text.Substring(other.S, other.E - other.S).TrimEnd('.').ToLowerInvariant() is "nr" or "no" or "num" or "núm" or "č. p" or "c. p")
            {
                fields.Remove(other);
                fields.Remove(number);
                fields.Add(("street.house-number", other.S, number.E));
            }
        }
    }

    // The registers label "HR-40000" as the country letters and the postcode apart.
    foreach ((string, int S, int E) pc in fields.Where(f => f.Name == "postal-code").ToList())
    {
        if (prefixInPostcode)
        {
            break;
        }

        int dash = text.IndexOf('-', pc.S);
        if (dash > pc.S && dash < pc.E && dash - pc.S <= 2 && text.AsSpan(pc.S, dash - pc.S).ToString().All(char.IsLetter))
        {
            fields.Remove(pc);
            fields.Add(("postal-code", dash + 1, pc.E));
            fields.Add(("country", pc.S, dash));
        }
    }

    return [.. fields];
}

return 0;
