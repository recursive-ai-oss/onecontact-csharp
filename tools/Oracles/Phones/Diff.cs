using System.Globalization;

namespace RecursiveAI.OneContact.Oracles.Phones;

/// <summary>Compares two result files and reports mismatches per operation and per region.</summary>
internal static class Diff
{
    internal static int Run(string oursPath, string jarPath, string casesPath, int show, string? reportPath)
    {
        Dictionary<string, (string Region, string Text)> cases = new(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(casesPath))
        {
            string[] f = line.Split('\t');
            if (f.Length >= 4 && line[0] != '#')
            {
                cases[f[0]] = (f[2], f[3]);
            }
        }

        Dictionary<(string, string), string> ours = Load(oursPath);
        Dictionary<(string, string), string> jar = Load(jarPath);
        Dictionary<string, int> perOp = new(StringComparer.Ordinal);
        Dictionary<string, int> perOpTotal = new(StringComparer.Ordinal);
        Dictionary<string, int> perRegion = new(StringComparer.Ordinal);
        List<string> mismatches = [];
        Dictionary<string, List<string>> byOp = new(StringComparer.Ordinal);
        int total = 0;
        foreach (((string id, string op), string expected) in jar)
        {
            total++;
            perOpTotal[op] = perOpTotal.GetValueOrDefault(op) + 1;
            string actual = ours.GetValueOrDefault((id, op), "<missing>");
            if (string.Equals(actual, expected, StringComparison.Ordinal))
            {
                continue;
            }

            perOp[op] = perOp.GetValueOrDefault(op) + 1;
            string region = id.Contains('-', StringComparison.Ordinal) ? id[..id.IndexOf('-', StringComparison.Ordinal)] : id;
            perRegion[region] = perRegion.GetValueOrDefault(region) + 1;
            (string caseRegion, string text) = cases.GetValueOrDefault(id, ("?", "?"));
            string entry = $"{id}\t{op}\tregion={caseRegion}\ttext={text}\n    jar : {expected}\n    ours: {actual}";
            mismatches.Add(entry);
            if (!byOp.TryGetValue(op, out List<string>? list))
            {
                byOp[op] = list = [];
            }

            list.Add(entry);
        }

        int extra = ours.Keys.Count(k => !jar.ContainsKey(k));
        TextWriter report = reportPath is null ? Console.Out : new StreamWriter(reportPath);
        report.WriteLine($"compared {total} results of {cases.Count} cases: {mismatches.Count} mismatches; {extra} results only on our side");
        report.WriteLine("per operation (mismatches / compared):");
        foreach ((string op, int n) in perOpTotal.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            report.WriteLine($"  {op,-16} {perOp.GetValueOrDefault(op),7} / {n}");
        }

        report.WriteLine("per region (mismatches):");
        foreach ((string region, int n) in perRegion.OrderByDescending(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal).Take(60))
        {
            report.WriteLine($"  {region,-6} {n}");
        }

        foreach ((string op, List<string> list) in byOp.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            report.WriteLine($"== {op}: first {Math.Min(show, list.Count)} of {list.Count}");
            foreach (string m in list.Take(show))
            {
                report.WriteLine(m);
            }
        }

        if (reportPath is not null)
        {
            report.Dispose();
        }

        return mismatches.Count == 0 && extra == 0 ? 0 : 1;
    }

    private static Dictionary<(string, string), string> Load(string path)
    {
        Dictionary<(string, string), string> map = [];
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            int a = line.IndexOf('\t', StringComparison.Ordinal);
            int b = line.IndexOf('\t', a + 1);
            map[(line[..a], line[(a + 1)..b])] = line[(b + 1)..];
        }

        return map;
    }
}
