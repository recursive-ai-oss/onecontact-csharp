using RecursiveAI.OneContact.Names;
using RecursiveAI.OneNlp;

namespace RecursiveAI.OneContact.Models.Accurate;

/// <summary>Reads what the heads wrote into a document: entity spans for the field, part and contact heads, the category probabilities for the class head.</summary>
internal static class AccurateLabels
{
    /// <summary>Turns the entities the document holds into model spans: offsets into the text, the label and the span's mean probability.</summary>
    internal static List<ModelSpan> Spans(Doc doc)
    {
        List<ModelSpan> spans = [];
        foreach (RecursiveAI.OneNlp.Span e in doc.Ents)
        {
            spans.Add(new ModelSpan(e.StartChar, e.EndChar - e.StartChar, e.Label, e.Confidence));
        }

        return spans;
    }

    /// <summary>Takes the class with the highest probability.</summary>
    internal static (NameClass Class, float Confidence) Class(Doc doc)
    {
        NameClass best = NameClass.Unknown;
        float top = 0f;
        foreach (KeyValuePair<string, float> cat in doc.Cats)
        {
            if (cat.Value > top)
            {
                top = cat.Value;
                best = cat.Key switch
                {
                    "person" => NameClass.Person,
                    "company" => NameClass.Company,
                    "multiple" => NameClass.Multiple,
                    _ => NameClass.Unknown,
                };
            }
        }

        return (best, top);
    }
}
