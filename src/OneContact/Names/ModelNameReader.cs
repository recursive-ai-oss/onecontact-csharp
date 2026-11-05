using RecursiveAI.OneContact.Companies;
using RecursiveAI.OneContact.Models;

namespace RecursiveAI.OneContact.Names;

/// <summary>
/// Decides, from a name model's labels, a name string the rules left <see cref="NameClass.Unknown"/>. It runs only for such strings: a class or a
/// part a rule found is never replaced. Every part it makes says <see cref="DecisionSource.Model"/> with the model's confidence.
/// </summary>
/// <remarks>The model must be confident (more than half of the words, weighted by probability, vote for the class) and a person needs a given or a family name; otherwise the string stays unknown.</remarks>
internal static class ModelNameReader
{
    private const float MinimumClassConfidence = 0.5f;

    /// <summary>Asks the model for the class of a string the rules could not classify.</summary>
    /// <returns>The class, or <see cref="NameClass.Unknown"/> when no model is registered or it is not confident.</returns>
    internal static NameClass Classify(ReadOnlySpan<char> text, Analysis rules)
    {
        if (rules.Class != NameClass.Unknown || rules.Persons.Count > 0 || ContactModels.Name is not { } model)
        {
            return rules.Class;
        }

        NameModelResult r = model.Analyze(text.ToString());
        return r.ClassConfidence > MinimumClassConfidence ? r.Class : NameClass.Unknown;
    }

    /// <summary>Reads a string the rules left unknown, when the model is registered and confident.</summary>
    /// <returns><see langword="true"/> when the model decided the string.</returns>
    internal static bool TryRead(ReadOnlySpan<char> text, NameHints hints, Analysis rules, out NameParseResult result)
    {
        result = null!;
        if (rules.Class != NameClass.Unknown || rules.Persons.Count > 0 || ContactModels.Name is not { } model)
        {
            return false;
        }

        string whole = text.ToString();
        NameModelResult r = model.Analyze(whole);
        if (r.ClassConfidence <= MinimumClassConfidence)
        {
            return false;
        }

        switch (r.Class)
        {
            case NameClass.Company:
                CompanyName.TryParse(text, hints, out CompanyName company, out ParseDetail companyDetail);
                result = new NameParseResult(NameClass.Company, [], company, Marked(companyDetail, r.ClassConfidence));
                return true;
            case NameClass.Person:
                PersonDraft draft = Draft(whole, r.Parts, rules);
                if (!draft.HasCore)
                {
                    return false;
                }

                PersonName person = PersonNameBuilder.Build(text, draft, rules.RestoreCase, hints, out ParseDetail personDetail);
                result = new NameParseResult(NameClass.Person, [person], default, Marked(personDetail, r.ClassConfidence, r.Parts));
                return true;
            case NameClass.Multiple:
                // The rules cannot split these people and the model only labels words, so the class is reported and no person is made up.
                result = new NameParseResult(NameClass.Multiple, [], default, new ParseDetail([], []));
                return true;
            default:
                return false;
        }
    }

    private static PersonDraft Draft(string text, IReadOnlyList<ModelSpan> parts, Analysis rules)
    {
        PersonDraft draft = new() { Source = new SourceSpan(rules.Start, rules.End - rules.Start) };
        foreach (IGrouping<string, ModelSpan> group in parts.GroupBy(p => p.Label, StringComparer.Ordinal))
        {
            // A label that appears twice (a given name on both sides of a nickname) is read as one part from its first word to its last.
            int start = group.Min(p => p.Start);
            int end = group.Max(p => p.End);
            NamePart part = new(text.Substring(start, end - start), new SourceSpan(start, end - start));
            switch (group.Key)
            {
                case "Prefix": draft.Prefix = part; break;
                case "Given": draft.Given = part; break;
                case "Middle": draft.Middle = part; break;
                case "Nickname": draft.Nickname = part; break;
                case "Particle": draft.Particle = part; break;
                case "Family": draft.Family = part; break;
                case "SecondFamily": draft.SecondFamily = part; break;
                case "Suffix": draft.Suffix = part; break;
                case "ProfessionalSuffix": draft.ProfessionalSuffix = part; break;
            }
        }

        return draft;
    }

    // Every field of the detail says a model decided it, with the model's confidence for the part that covers it (the class confidence otherwise).
    private static ParseDetail Marked(ParseDetail detail, float fallback, IReadOnlyList<ModelSpan>? parts = null)
    {
        List<ParsedField> fields = [];
        foreach (ParsedField f in detail.Fields)
        {
            float confidence = fallback;
            if (parts is not null)
            {
                foreach (ModelSpan p in parts)
                {
                    if (p.Start <= f.Span.Start && p.End >= f.Span.End)
                    {
                        confidence = p.Confidence;
                        break;
                    }
                }
            }

            fields.Add(f with { Source = DecisionSource.Model, Confidence = confidence });
        }

        return new ParseDetail(detail.Issues, fields);
    }
}
