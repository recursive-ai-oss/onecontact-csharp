using RecursiveAI.OneContact.Names;
using RecursiveAI.OneNlp;

namespace RecursiveAI.OneContact.Models.Superset;

/// <summary>Reads the labels the heads wrote into a document: BIO spans for the field and part heads, a vote for the class head.</summary>
internal static class SupersetLabels
{
    /// <summary>The name of the outside label.</summary>
    internal const string Outside = "O";

    /// <summary>Joins the begin or inside marker and a field label into a tag.</summary>
    internal static string Tag(bool begin, string label) => (begin ? "B-" : "I-") + label;

    /// <summary>Reads spans out of the BIO tags in a column. A span runs over consecutive words with the same label; an inside tag after a different label starts a span.</summary>
    internal static List<ModelSpan> Spans(Doc doc, TokenColumn column)
    {
        List<ModelSpan> spans = [];
        string? label = null;
        int start = 0;
        int end = 0;
        float sum = 0;
        int count = 0;
        for (int i = 0; i < doc.Length; i++)
        {
            string tag = doc.Strings[doc.GetColumn(column)[i]];
            float conf = doc.GetConfidences()[i];
            bool begin = tag.StartsWith("B-", StringComparison.Ordinal);
            bool inside = tag.StartsWith("I-", StringComparison.Ordinal);
            string? next = begin || inside ? tag[2..] : null;
            if (label is not null && (next is null || begin || next != label))
            {
                spans.Add(new ModelSpan(start, end - start, label, sum / count));
                label = null;
            }

            if (next is null)
            {
                continue;
            }

            if (label is null)
            {
                label = next;
                start = doc.GetStarts()[i];
                sum = 0;
                count = 0;
            }

            end = doc.GetStarts()[i] + doc.GetLengths()[i];
            sum += conf;
            count++;
        }

        if (label is not null)
        {
            spans.Add(new ModelSpan(start, end - start, label, sum / count));
        }

        return spans;
    }

    /// <summary>Takes the class the words vote for, each word's vote weighted by its probability; line breaks and punctuation do not vote.</summary>
    internal static (NameClass Class, float Confidence) Vote(Doc doc)
    {
        float person = 0, company = 0, multiple = 0, unknown = 0;
        for (int i = 0; i < doc.Length; i++)
        {
            if (!SupersetTokenizer.IsWordPart(doc.Text[doc.GetStarts()[i]]))
            {
                continue;
            }

            float conf = doc.GetConfidences()[i];
            switch (doc.Strings[doc.GetColumn(TokenColumn.Morph)[i]])
            {
                case "person": person += conf; break;
                case "company": company += conf; break;
                case "multiple": multiple += conf; break;
                default: unknown += conf; break;
            }
        }

        float total = person + company + multiple + unknown;
        if (total <= 0)
        {
            return (NameClass.Unknown, 0f);
        }

        (NameClass, float) best = (NameClass.Unknown, unknown);
        if (person > best.Item2)
        {
            best = (NameClass.Person, person);
        }

        if (company > best.Item2)
        {
            best = (NameClass.Company, company);
        }

        if (multiple > best.Item2)
        {
            best = (NameClass.Multiple, multiple);
        }

        return (best.Item1, best.Item2 / total);
    }
}
