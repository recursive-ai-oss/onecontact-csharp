using System.Globalization;
using RecursiveAI.OneContact.Models.Accurate;
using RecursiveAI.OneNlp;

namespace OneContact.Recipe.Accurate;

/// <summary>Span F1 per field and for block starts on dev blocks; training selects on the mean of the overall field F1 and the block-start F1.</summary>
internal static class SpanScores
{
    internal static double Mean(IReadOnlyList<Example> examples)
    {
        Counts c = Count(examples);
        return (c.Fields.F1 + c.Starts.F1) / 2;
    }

    internal static void Report(IReadOnlyList<Example> examples, TextWriter output)
    {
        Counts c = Count(examples);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"field_f1={c.Fields.F1:F4} block_start_f1={c.Starts.F1:F4} examples={examples.Count}"));
        foreach ((string label, Tally t) in c.PerLabel.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {label}: p={t.Precision:F4} r={t.Recall:F4} f1={t.F1:F4} gold={t.Gold}"));
        }
    }

    private sealed class Tally
    {
        public long Tp, Pred, Gold;

        public double Precision => Pred == 0 ? 0 : (double)Tp / Pred;

        public double Recall => Gold == 0 ? 0 : (double)Tp / Gold;

        public double F1 => Precision + Recall == 0 ? 0 : 2 * Precision * Recall / (Precision + Recall);
    }

    private sealed record Counts(Tally Fields, Tally Starts, Dictionary<string, Tally> PerLabel);

    private static Counts Count(IReadOnlyList<Example> examples)
    {
        var fields = new Tally();
        var starts = new Tally();
        var per = new Dictionary<string, Tally>(StringComparer.Ordinal);
        foreach (Example e in examples)
        {
            (HashSet<(int S, int E, string L)> gf, HashSet<int> gs) = Read(e.Reference);
            (HashSet<(int S, int E, string L)> pf, HashSet<int> ps) = Read(e.Predicted);
            Add(fields, gf, pf);
            Add(starts, gs, ps);
            foreach (string label in gf.Concat(pf).Select(x => x.L).Distinct())
            {
                if (!per.TryGetValue(label, out Tally? t))
                {
                    per[label] = t = new Tally();
                }

                Add(t, gf.Where(x => x.L == label).ToHashSet(), pf.Where(x => x.L == label).ToHashSet());
            }
        }

        return new Counts(fields, starts, per);
    }

    private static void Add<T>(Tally t, HashSet<T> gold, HashSet<T> pred)
    {
        t.Gold += gold.Count;
        t.Pred += pred.Count;
        t.Tp += gold.Intersect(pred).Count();
    }

    private static (HashSet<(int S, int E, string L)> Fields, HashSet<int> Starts) Read(Doc doc)
    {
        var fields = new HashSet<(int, int, string)>();
        var starts = new HashSet<int>();
        foreach (RecursiveAI.OneNlp.Span s in doc.Ents)
        {
            string label = s.Label;
            if (label.EndsWith(AccuratePipeline.StartSuffix, StringComparison.Ordinal))
            {
                label = label[..^AccuratePipeline.StartSuffix.Length];
                starts.Add(s.StartChar);
            }

            fields.Add((s.StartChar, s.EndChar, label));
        }

        return (fields, starts);
    }
}
