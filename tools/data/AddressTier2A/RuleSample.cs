using System.Buffers;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Parsing;

namespace OneContact.Data.AddressTier2A;

/// <summary>
/// The library's rule parser over an even sample of each country's dev examples: what it does with free text outside the United States
/// and Canada, with no hint and with the country as the hint. Information for the Tier 2 rule and model work; nothing here is a gate.
/// </summary>
internal static class RuleSample
{
    internal static int Run(Dictionary<string, string> o)
    {
        int per = int.Parse(o.GetValueOrDefault("per-country", "300"), System.Globalization.CultureInfo.InvariantCulture);
        StringBuilder md = new();
        md.AppendLine("| Country | Sample | No hint: parsed | No hint: refused (reasons) | No hint: country right (inferred) | Hint: parsed | Hint: refused (reasons) | Hint: fields found | Gold fields | Structural check of the gold fields (examples with street, postcode and locality) |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (string file in Directory.GetFiles(o["out"], "*.dev.jsonl").OrderBy(f => f, StringComparer.Ordinal))
        {
            string cc = Path.GetFileName(file)[..2].ToUpperInvariant();
            List<string> lines = File.ReadLines(file).ToList();
            int step = Math.Max(1, lines.Count / per);
            List<string> sample = lines.Where((_, i) => i % step == 0).Take(per).ToList();
            Region hint = Region.Parse(cc);
            (long parsed, Dictionary<string, long> refused, long right, long inferred, long fields, long gold) none = Probe(sample, Region.Unknown, cc);
            (long parsed, Dictionary<string, long> refused, long right, long inferred, long fields, long gold) with = Probe(sample, hint, cc);
            md.AppendLine($"| {cc} | {sample.Count} | {none.parsed} | {Reasons(none.refused)} | {none.right} ({none.inferred}) | {with.parsed} | {Reasons(with.refused)} | {with.fields} | {with.gold} | {Structural(sample, hint)} |");
        }

        Console.Write(md.ToString());
        File.WriteAllText(Path.Combine(o["out"], "rule-sample.md"), md.ToString(), new UTF8Encoding(false));
        return 0;
    }

    // PostalAddress.Validate on the labelled fields: the structural layer's required fields and postcode pattern for the country, no lookups.
    private static string Structural(List<string> sample, Region country)
    {
        long checkedCount = 0;
        long sound = 0;
        Dictionary<string, long> issues = new(StringComparer.Ordinal);
        foreach (string line in sample)
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            string text = doc.RootElement.GetProperty("text").GetString()!;
            string Get(string label) => string.Join(" ", doc.RootElement.GetProperty("entities").EnumerateArray().Where(e => e[2].GetString() == label).Select(e => text[e[0].GetInt32()..e[1].GetInt32()]));
            JsonElement.ArrayEnumerator fields = doc.RootElement.GetProperty("spans").GetProperty("fields").EnumerateArray();
            string postal = Get("postal-code");
            string locality = Get("locality");
            if (!fields.MoveNext() || postal.Length == 0 || locality.Length == 0)
            {
                continue;
            }

            string street = text[fields.Current[0].GetInt32()..fields.Current[1].GetInt32()];
            checkedCount++;
            PostalAddress address = PostalAddress.Create(country, locality: locality, postalCode: postal, streetLines: [street]);
            IReadOnlyList<AddressIssue> found = address.Validate();
            if (found.Count == 0)
            {
                sound++;
            }

            foreach (AddressIssue issue in found)
            {
                string key = $"{issue.Field} {issue.Kind}";
                issues[key] = issues.GetValueOrDefault(key) + 1;
            }
        }

        return $"{sound}/{checkedCount} sound" + (issues.Count == 0 ? string.Empty : "; " + Reasons(issues));
    }

    private static string Reasons(Dictionary<string, long> d) => d.Count == 0 ? "none" : string.Join("; ", d.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"));

    private static (long, Dictionary<string, long>, long, long, long, long) Probe(List<string> sample, Region hint, string cc)
    {
        long parsed = 0;
        long right = 0;
        long inferred = 0;
        long fields = 0;
        long gold = 0;
        Dictionary<string, long> refused = new(StringComparer.Ordinal);
        foreach (string line in sample)
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            string text = doc.RootElement.GetProperty("text").GetString()!;
            List<(int S, int E, string L)> entities = doc.RootElement.GetProperty("entities").EnumerateArray().Select(e => (e[0].GetInt32(), e[1].GetInt32(), e[2].GetString()!)).ToList();
            gold += entities.Count;
            OperationStatus status = RuleAddressParser.TryParse(text, hint, out ParsedAddress? address, out ParseDetail detail);
            if (status != OperationStatus.Done || address is null)
            {
                string id = detail.PrimaryReason.Id;
                refused[id] = refused.GetValueOrDefault(id) + 1;
                continue;
            }

            parsed++;
            if (address.Country == Region.Parse(cc))
            {
                right++;
                if (address.CountryInferred)
                {
                    inferred++;
                }
            }

            HashSet<(string, int, int)> got = address.Fields.Select(f => (f.Name, f.Span.Start, f.Span.End)).ToHashSet();
            foreach ((int s, int e, string l) in entities)
            {
                if (got.Contains((l is "recipient" or "organization" ? "leading-line" : l, s, e)))
                {
                    fields++;
                }
            }
        }

        return (parsed, refused, right, inferred, fields, gold);
    }
}
