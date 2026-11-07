using RecursiveAI.OneContact.Models.Superset;
using RecursiveAI.OneNlp;
using RecursiveAI.OneNlp.Data;

namespace OneContact.Recipe.Models;

/// <summary>
/// Dev scores: span F1 over the address fields, span F1 over the name parts, and class accuracy over name words. The score training selects
/// on is the mean of the three that the dev set has examples for.
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
        output.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"address_field_f1={a:F4} name_part_f1={p:F4} name_class_word_accuracy={c:F4} examples={examples.Count}"));
    }

    private static (double Address, double Parts, double Class) Compute(IReadOnlyList<Example> examples)
    {
        long aTp = 0, aPred = 0, aGold = 0, pTp = 0, pPred = 0, pGold = 0, cOk = 0, cAll = 0;
        foreach (Example e in examples)
        {
            Doc gold = e.Reference;
            Doc pred = e.Predicted;
            if (HasColumn(gold, TokenColumn.Tag))
            {
                Count(gold, pred, TokenColumn.Tag, ref aTp, ref aPred, ref aGold);
            }

            if (HasColumn(gold, TokenColumn.Pos))
            {
                Count(gold, pred, TokenColumn.Pos, ref pTp, ref pPred, ref pGold);
                for (int i = 0; i < gold.Length; i++)
                {
                    if (!SupersetTokenizer.IsWordPart(gold.Text[gold.GetStarts()[i]]))
                    {
                        continue;
                    }

                    cAll++;
                    if (gold.GetColumn(TokenColumn.Morph)[i] == pred.GetColumn(TokenColumn.Morph)[i] || gold.Strings[gold.GetColumn(TokenColumn.Morph)[i]] == pred.Strings[pred.GetColumn(TokenColumn.Morph)[i]])
                    {
                        cOk++;
                    }
                }
            }
        }

        return (F1(aTp, aPred, aGold), F1(pTp, pPred, pGold), cAll == 0 ? double.NaN : (double)cOk / cAll);
    }

    private static bool HasColumn(Doc doc, TokenColumn column)
    {
        foreach (int id in doc.GetColumn(column))
        {
            if (id != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static void Count(Doc gold, Doc pred, TokenColumn column, ref long tp, ref long nPred, ref long nGold)
    {
        var g = new HashSet<(int, int, string)>();
        foreach (RecursiveAI.OneContact.Models.ModelSpan s in SupersetLabels.Spans(gold, column))
        {
            g.Add((s.Start, s.Length, s.Label));
        }

        var p = new HashSet<(int, int, string)>();
        foreach (RecursiveAI.OneContact.Models.ModelSpan s in SupersetLabels.Spans(pred, column))
        {
            p.Add((s.Start, s.Length, s.Label));
        }

        nGold += g.Count;
        nPred += p.Count;
        g.IntersectWith(p);
        tp += g.Count;
    }

    private static double F1(long tp, long pred, long gold)
    {
        if (gold == 0)
        {
            return double.NaN;
        }

        double precision = pred == 0 ? 0 : (double)tp / pred;
        double recall = (double)tp / gold;
        return precision + recall == 0 ? 0 : 2 * precision * recall / (precision + recall);
    }
}
