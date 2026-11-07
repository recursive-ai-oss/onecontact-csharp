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

    // How sure the model must be to replace the rules' default reading of a name as a person; chosen on the development names (docs/models.md).
    internal const float DefaultOverrideConfidence = 0.6f;

    // Language codes whose names the Tier 1 and the Tier 1 + Tier 2 models learned from large open lists (English, French, Spanish: the United States and Canada).
    private static bool IsUsCaLanguage(string? language) => language is null || language.Length == 0 || language.Equals("en", StringComparison.OrdinalIgnoreCase)
        || language.Equals("fr", StringComparison.OrdinalIgnoreCase) || language.Equals("es", StringComparison.OrdinalIgnoreCase);

    // Countries whose rules-default person readings the Tier 1 + Tier 2 model may overrule, at the higher confidence below. In each the override gained at least 0.3
    // points of class accuracy on the development names and did not lower any name row on the real-page sets (docs/maintainers/models-superset.md). Every other Tier 2
    // country reads only what the rules leave open.
    private static readonly HashSet<string> OverrideCountries = new(StringComparer.Ordinal) { "AU", "EE", "FR", "GB", "IT", "LI", "MT" };

    // How sure the model must be to replace a rules-default person reading in those countries.
    internal const float Tier2OverrideConfidence = 0.8f;

    /// <summary>
    /// The confidence the model needs to replace the rules' default reading of a string as a person, or <see langword="null"/> when it may not. With the Tier 1 package
    /// and for United States, Canadian and unhinted English, French and Spanish text it is <see cref="DefaultOverrideConfidence"/>. With the Tier 1 + Tier 2 package a
    /// Tier 2 text is read open-only (the model reads what the rules leave <see cref="NameClass.Unknown"/>, and never replaces a rule's person reading, which would drop
    /// the person parts the rules found) except in <see cref="OverrideCountries"/>.
    /// </summary>
    private static float? OverrideConfidence(NameHints hints)
    {
        if (ContactModels.PackageId is not { } package || package == ContactModels.Tier1PackageId)
        {
            return DefaultOverrideConfidence;
        }

        if (hints.Country.IsUnknown)
        {
            return IsUsCaLanguage(hints.Language) ? DefaultOverrideConfidence : null;
        }

        if (hints.Country == Region.US || hints.Country == Region.CA)
        {
            return DefaultOverrideConfidence;
        }

        return OverrideCountries.Contains(hints.Country.ToString()) ? Tier2OverrideConfidence : null;
    }

    /// <summary>True when the rules took the string as one person only because nothing marked it as a company: no title, suffix, nickname, particle, comma order, initial or known given name proved it (the <c>person.no-evidence</c> issue).</summary>
    internal static bool IsDefaultPerson(Analysis rules)
    {
        if (rules.Class != NameClass.Person || rules.Persons.Count != 1)
        {
            return false;
        }

        foreach (ParseIssue issue in rules.Issues)
        {
            if (issue.Reason.Equals(ReasonCode.PersonNoEvidence))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Asks the model for the class of a string the rules could not classify, or whose person reading was only a default.</summary>
    /// <returns>The class, or the rules' own class when no model is registered or it is not confident enough.</returns>
    internal static NameClass Classify(ReadOnlySpan<char> text, Analysis rules, NameHints hints = default)
    {
        if (IsDefaultPerson(rules) && OverrideConfidence(hints) is { } cut && ContactModels.Name is { } defaultModel)
        {
            NameModelResult m = defaultModel.Analyze(text.ToString());
            return m.Class != NameClass.Person && m.Class != NameClass.Multiple && m.ClassConfidence >= cut ? m.Class : rules.Class;
        }

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
        bool isDefault = IsDefaultPerson(rules);
        float? allowed = isDefault ? OverrideConfidence(hints) : null;
        if (isDefault && allowed is null)
        {
            return false;
        }

        if (isDefault && ContactModels.Name is { } defaultModel)
        {
            float cut = allowed!.Value;
            string text0 = text.ToString();
            NameModelResult m = defaultModel.Analyze(text0);
            if (m.ClassConfidence >= cut && m.Class == NameClass.Company)
            {
                CompanyName.TryParse(text, hints, out CompanyName c, out ParseDetail cd);
                result = new NameParseResult(NameClass.Company, [], c, Marked(cd, m.ClassConfidence));
                return true;
            }

            if (m.ClassConfidence >= cut && m.Class == NameClass.Unknown)
            {
                result = new NameParseResult(NameClass.Unknown, [], default, new ParseDetail([], []));
                return true;
            }

            return false;
        }

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
