using System.Text;

namespace RecursiveAI.OneContact.Oracles.PhonesGeo;

/// <summary>Compares the two answer files case by case and field by field and reports mismatches per field, per calling code and as examples.</summary>
internal static class Diff
{
    private static readonly string[] Fields = ["geo", "geoUser", "geoValid", "geoValidUser", "carrier", "carrierValid", "carrierSafe", "timeZones", "timeZonesGeographical"];

    internal static int Run(string oursPath, string jarPath, string casesPath, int show, string? reportPath)
    {
        using IEnumerator<string> ours = File.ReadLines(oursPath).GetEnumerator();
        using IEnumerator<string> jar = File.ReadLines(jarPath).GetEnumerator();
        using IEnumerator<string> cases = File.ReadLines(casesPath).GetEnumerator();
        int total = 0;
        int mismatchedCases = 0;
        int[] perField = new int[Fields.Length];
        SortedDictionary<string, int> perCode = new(StringComparer.Ordinal);
        SortedDictionary<string, int> perLanguage = new(StringComparer.Ordinal);
        List<string> examples = [];
        while (cases.MoveNext())
        {
            if (!ours.MoveNext() || !jar.MoveNext())
            {
                Console.Error.WriteLine("An answer file is shorter than the case file.");
                return 2;
            }

            total++;
            string[] c = cases.Current.Split('\t');
            string[] a = ours.Current.Split('\t');
            string[] b = jar.Current.Split('\t');
            if (a[0] != c[0] || b[0] != c[0])
            {
                Console.Error.WriteLine($"Answer ids differ at case {c[0]}.");
                return 2;
            }

            bool bad = false;
            for (int i = 0; i < Fields.Length; i++)
            {
                if (a[i + 1] != b[i + 1])
                {
                    bad = true;
                    perField[i]++;
                    if (examples.Count < show * 40)
                    {
                        examples.Add($"{Fields[i]}\t+{c[1]} {c[2]}\t{c[3]}\t{c[4]}\tours={a[i + 1]}\tjar={b[i + 1]}");
                    }
                }
            }

            if (bad)
            {
                mismatchedCases++;
                perCode[c[1]] = perCode.GetValueOrDefault(c[1]) + 1;
                perLanguage[c[3]] = perLanguage.GetValueOrDefault(c[3]) + 1;
            }
        }

        StringBuilder sb = new();
        sb.AppendLine($"cases {total}, cases with a mismatch {mismatchedCases}");
        for (int i = 0; i < Fields.Length; i++)
        {
            sb.AppendLine($"{Fields[i]}: {perField[i]} mismatches");
        }

        sb.AppendLine("by calling code: " + string.Join(", ", perCode.Select(kv => $"{kv.Key}={kv.Value}").Take(60)));
        sb.AppendLine("by language: " + string.Join(", ", perLanguage.Select(kv => $"{kv.Key}={kv.Value}").Take(60)));
        foreach (string group in examples.GroupBy(e => e.Split('\t')[0]).SelectMany(g => g.Take(show)))
        {
            sb.AppendLine(group);
        }

        Console.WriteLine(sb.ToString().Split('\n').Take(8).Aggregate((x, y) => x + "\n" + y));
        if (reportPath is not null)
        {
            File.WriteAllText(reportPath, sb.ToString());
        }

        return mismatchedCases == 0 ? 0 : 1;
    }
}
