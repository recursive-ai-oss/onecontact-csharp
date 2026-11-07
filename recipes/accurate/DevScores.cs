using System.Globalization;
using RecursiveAI.OneNlp;
using RecursiveAI.OneNlp.Data;

namespace OneContact.Recipe.Accurate;

/// <summary>
/// Dev scores: span F1 over the entities of the address or name-part head, and class accuracy over name strings. An example with categories is a
/// name string; one without is an address. Training selects on the mean of the scores the dev set has examples for.
/// </summary>
internal static class DevScores
{
    internal static double Mean(IReadOnlyList<Example> examples)
    {
        (double a, double p, double c) = Compute(examples);
        double sum = 0;
        int n = 0;
        foreach (double v in new[] { a, p, c })
        {
            if (!double.IsNaN(v))
            {
                sum += v;
                n++;
            }
        }

        return n == 0 ? 0 : sum / n;
    }

    internal static void Report(IReadOnlyList<Example> examples, TextWriter output)
    {
        (double a, double p, double c) = Compute(examples);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"address_field_f1={a:F4} name_part_f1={p:F4} name_class_accuracy={c:F4} examples={examples.Count}"));
    }

    private static (double Address, double Parts, double Class) Compute(IReadOnlyList<Example> examples)
    {
        long aTp = 0, aPred = 0, aGold = 0, pTp = 0, pPred = 0, pGold = 0, cOk = 0, cAll = 0;
        foreach (Example e in examples)
        {
            HashSet<(int, int, string)> gold = Read(e.Reference);
            HashSet<(int, int, string)> pred = Read(e.Predicted);
            if (e.Cats is null)
            {
                aGold += gold.Count;
                aPred += pred.Count;
                aTp += gold.Intersect(pred).Count();
                continue;
            }

            pGold += gold.Count;
            pPred += pred.Count;
            pTp += gold.Intersect(pred).Count();
            string truth = e.Cats.OrderByDescending(c => c.Value).First().Key;
            string guess = e.Predicted.HasCats ? e.Predicted.Cats.OrderByDescending(c => c.Value).First().Key : string.Empty;
            cAll++;
            if (truth == guess)
            {
                cOk++;
            }
        }

        return (F1(aTp, aPred, aGold), F1(pTp, pPred, pGold), cAll == 0 ? double.NaN : (double)cOk / cAll);
    }

    private static double F1(long tp, long pred, long gold)
    {
        if (gold == 0 && pred == 0)
        {
            return double.NaN;
        }

        double p = pred == 0 ? 0 : (double)tp / pred;
        double r = gold == 0 ? 0 : (double)tp / gold;
        return p + r == 0 ? 0 : 2 * p * r / (p + r);
    }

    private static HashSet<(int, int, string)> Read(Doc doc)
    {
        var set = new HashSet<(int, int, string)>();
        foreach (RecursiveAI.OneNlp.Span s in doc.Ents)
        {
            set.Add((s.StartChar, s.EndChar, s.Label));
        }

        return set;
    }
}
