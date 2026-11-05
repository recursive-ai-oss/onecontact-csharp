using System.Text;

namespace RecursiveAI.OneContact.Names;

/// <summary>The parts of a <see cref="PersonName"/>: what the rules found, with casing restored where the input was all one case.</summary>
internal sealed class PersonNameParts
{
    internal NamePart Prefix { get; init; }

    internal NamePart Given { get; init; }

    internal NamePart Middle { get; init; }

    internal NamePart Particle { get; init; }

    internal NamePart Family { get; init; }

    internal NamePart SecondFamily { get; init; }

    internal NamePart Suffix { get; init; }

    internal NamePart ProfessionalSuffix { get; init; }

    internal NamePart Nickname { get; init; }

    internal string FamilyConnector { get; init; } = string.Empty;

    internal NameOrder Order { get; init; }
}

/// <summary>Turns a draft into a <see cref="PersonName"/>: restores casing, writes the canonical form and the detail.</summary>
internal static class PersonNameBuilder
{
    internal static PersonName Build(ReadOnlySpan<char> whole, PersonDraft draft, bool restoreCase, NameHints hints, out ParseDetail detail)
    {
        string[] langs = NameLexicon.Languages(hints);
        bool lowerParticles = NameLexicon.LowerParticles(langs);

        NamePart Fix(NamePart part, Kind kind) =>
            part.IsEmpty ? part : new NamePart(restoreCase ? RestorePart(part.Text, kind, lowerParticles) : part.Text, part.Span);

        PersonNameParts parts = new()
        {
            Prefix = Fix(draft.Prefix, Kind.Title),
            Given = Fix(draft.Given, Kind.Word),
            Middle = Fix(draft.Middle, Kind.Word),
            Particle = Fix(draft.Particle, Kind.Particle),
            Family = Fix(draft.Family, Kind.Word),
            SecondFamily = Fix(draft.SecondFamily, Kind.Word),
            Suffix = Fix(draft.Suffix, Kind.Suffix),
            ProfessionalSuffix = Fix(draft.ProfessionalSuffix, Kind.Suffix),
            Nickname = Fix(draft.Nickname, Kind.Word),
            FamilyConnector = draft.FamilyConnector,
            Order = draft.Order,
        };

        // Reading order: title, given, middle, nickname, particle and family name, second family name, generational suffix,
        // then professional suffixes after a comma. A family-first input (Hungarian, "Last, First") is written given first, so the
        // canonical form always parses back to the same parts.
        StringBuilder sb = new();
        Append(sb, parts.Prefix.Text);
        Append(sb, parts.Given.Text);
        Append(sb, parts.Middle.Text);
        if (!parts.Nickname.IsEmpty)
        {
            Append(sb, "\"" + parts.Nickname.Text + "\"");
        }

        Append(sb, parts.Particle.Text);
        Append(sb, parts.Family.Text);
        if (!parts.SecondFamily.IsEmpty)
        {
            if (parts.FamilyConnector.Length > 0)
            {
                Append(sb, parts.FamilyConnector);
            }

            Append(sb, parts.SecondFamily.Text);
        }

        Append(sb, parts.Suffix.Text);
        if (!parts.ProfessionalSuffix.IsEmpty)
        {
            sb.Append(", ").Append(parts.ProfessionalSuffix.Text.Replace(" ", ", ", StringComparison.Ordinal));
        }

        NameFacts flags = NameFacts.None;
        if (restoreCase)
        {
            flags |= NameFacts.CasingRestored;
        }

        if (!parts.Prefix.IsEmpty)
        {
            flags |= NameFacts.HasPrefix;
        }

        if (!parts.Suffix.IsEmpty || !parts.ProfessionalSuffix.IsEmpty)
        {
            flags |= NameFacts.HasSuffix;
        }

        if (!parts.Nickname.IsEmpty)
        {
            flags |= NameFacts.HasNickname;
        }

        if (!parts.Particle.IsEmpty)
        {
            flags |= NameFacts.HasParticle;
        }

        if (!parts.SecondFamily.IsEmpty)
        {
            flags |= NameFacts.HasSecondFamily;
        }

        if (parts.Order != NameOrder.GivenFamily)
        {
            flags |= NameFacts.FamilyFirstInput;
        }

        if (!draft.Given.IsEmpty && draft.Middle.IsEmpty && draft.Family.IsEmpty && draft.Prefix.IsEmpty)
        {
            flags |= NameFacts.SingleWord;
        }

        List<ParseIssue> issues = [.. draft.Issues];
        if (restoreCase)
        {
            issues.Add(new ParseIssue(ReasonCode.PersonCasingRestored, draft.Source, IssueSeverity.Info));
        }

        List<ParsedField> fields = [];
        AddField(fields, "prefix", draft.Prefix);
        AddField(fields, "given", draft.Given);
        AddField(fields, "middle", draft.Middle);
        AddField(fields, "nickname", draft.Nickname);
        AddField(fields, "particle", draft.Particle);
        AddField(fields, "family", draft.Family);
        AddField(fields, "second-family", draft.SecondFamily);
        AddField(fields, "suffix", draft.Suffix);
        AddField(fields, "professional-suffix", draft.ProfessionalSuffix);
        fields.Sort(static (a, b) => a.Span.Start.CompareTo(b.Span.Start));
        detail = new ParseDetail(issues, fields);

        _ = whole;
        return new PersonName(sb.ToString(), draft.Source, flags, parts);
    }

    private static void AddField(List<ParsedField> fields, string name, NamePart part)
    {
        if (!part.IsEmpty)
        {
            fields.Add(new ParsedField(name, part.Span, 1f, DecisionSource.Rule));
        }
    }

    private static void Append(StringBuilder sb, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (sb.Length > 0)
        {
            sb.Append(' ');
        }

        sb.Append(text);
    }

    private enum Kind
    {
        Word,
        Particle,
        Title,
        Suffix,
    }

    // Casing restoration: each word is lower-cased and then capitalized at its start, after a hyphen, an apostrophe or a dot (initials),
    // and after "Mc". Particles are lower case when a hint says the language writes them so. Titles and suffixes with a fixed
    // spelling (PhD, MSc, MUDr) take it. "MACDONALD" becomes "Macdonald": Mac is as often part of the name (Macintosh, Mackenzie).
    private static string RestorePart(string text, Kind kind, bool lowerParticles)
    {
        StringBuilder sb = new(text.Length);
        foreach (string word in text.Split(' '))
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(RestoreWord(word, kind, lowerParticles));
        }

        return sb.ToString();
    }

    private static string RestoreWord(string word, Kind kind, bool lowerParticles)
    {
        string folded = NameFolding.LettersAndDigits(word);
        if (kind == Kind.Particle && lowerParticles)
        {
            return word.ToLowerInvariant();
        }

        if (kind is Kind.Suffix or Kind.Title)
        {
            string? special = NameLexicon.SpecialCase(folded);
            if (special is not null)
            {
                return ApplyLetterCase(word, special);
            }
        }

        // Roman numerals written as suffixes stay in capitals even when the rest is lower case ("john smith iii").
        StringBuilder sb = new(word.Length);
        bool up = true;
        string lower = word.ToLowerInvariant();
        for (int i = 0; i < lower.Length; i++)
        {
            char c = lower[i];
            sb.Append(up ? char.ToUpperInvariant(c) : c);
            up = c is '-' or '\'' or '\u2019' or '.' or '/';
        }

        string result = sb.ToString();

        // "Mc" plus a capital: McDonald, McArthur.
        for (int i = 0; i + 2 < result.Length; i++)
        {
            bool atStart = i == 0 || result[i - 1] is '-' or ' ';
            if (atStart && result[i] == 'M' && result[i + 1] == 'c' && char.IsLetter(result[i + 2]))
            {
                result = result[..(i + 2)] + char.ToUpperInvariant(result[i + 2]) + result[(i + 3)..];
            }
        }

        return result;
    }

    // Writes the letters of <paramref name="typed"/> in the casing of <paramref name="pattern"/> (letters only), keeping the typed punctuation.
    private static string ApplyLetterCase(string typed, string pattern)
    {
        StringBuilder sb = new(typed.Length);
        int p = 0;
        foreach (char c in typed)
        {
            if (char.IsLetter(c) && p < pattern.Length)
            {
                while (p < pattern.Length && !char.IsLetter(pattern[p]))
                {
                    p++;
                }

                sb.Append(p < pattern.Length ? pattern[p] : c);
                p++;
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
