using System.Buffers;
using System.Text;
using RecursiveAI.OneContact.Names;

namespace RecursiveAI.OneContact.Companies;

/// <summary>The parts of a <see cref="CompanyName"/>.</summary>
internal sealed class CompanyNameParts
{
    internal NamePart LeadingArticle { get; init; }

    internal NamePart Base { get; init; }

    internal CompanyLegalForm? LegalForm { get; init; }

    internal NamePart TradingAs { get; init; }

    internal NamePart CareOf { get; init; }

    internal string TradingMarker { get; init; } = string.Empty;
}

/// <summary>
/// Reads a company name: a leading "The", the base name, the legal form (ELF list), "trading as" and "c/o" parts. Rules only; the
/// legal form is looked up in the compiled ISO 20275 list, so adding a jurisdiction is a data update.
/// </summary>
internal static class CompanyNameParser
{
    internal enum MarkerKind
    {
        None,
        TradingAs,
        CareOf,
    }

    // Markers written as one word, in lower case. "Co." and "Ta." are not markers: only the slash and dotted spellings are.
    private static readonly string[] TradingSingle = ["dba", "d/b/a", "d.b.a", "d.b.a.", "t/a", "t/as", "t.a.", "t.a"];

    private static readonly string[] CareOfSingle = ["c/o", "c.o.", "c.o"];

    internal static OperationStatus TryParse(ReadOnlySpan<char> input, NameHints hints, out CompanyName value, out ParseDetail detail)
    {
        SourceSpan span = PersonNameParser.Trim(input, 0, input.Length);
        if (span.IsEmpty)
        {
            value = default;
            detail = ParseDetail.Failure(ReasonCode.Empty, new SourceSpan(0, input.Length));
            return OperationStatus.InvalidData;
        }

        List<NameToken> tokens = NameTokenizer.Tokenize(input, span.Start, span.End, default);

        // Split at the first trading-as marker and the first care-of marker.
        List<(MarkerKind Kind, int First, int Count, string Canonical)> markers = [];
        int scan = 0;
        while (scan < tokens.Count)
        {
            MarkerKind kind = FindMarkerFrom(input, tokens, scan, out int at, out int count, out string canonical);
            if (kind == MarkerKind.None)
            {
                break;
            }

            markers.Add((kind, at, count, canonical));
            scan = at + count;
        }

        List<ParseIssue> issues = [];
        int mainEnd = markers.Count > 0 ? markers[0].First : tokens.Count;
        List<NameToken> main = tokens.GetRange(0, mainEnd);

        NamePart tradingAs = default;
        string tradingMarker = string.Empty;
        NamePart careOf = default;
        for (int m = 0; m < markers.Count; m++)
        {
            int from = markers[m].First + markers[m].Count;
            int to = m + 1 < markers.Count ? markers[m + 1].First : tokens.Count;
            if (to <= from)
            {
                if (markers[m].Kind == MarkerKind.TradingAs)
                {
                    issues.Add(new ParseIssue(ReasonCode.CompanyEmptyTradingName, new SourceSpan(tokens[markers[m].First].Start, tokens[markers[m].First].Length), IssueSeverity.Warning));
                }

                continue;
            }

            NamePart part = TextOf(input, tokens, from, to);
            if (markers[m].Kind == MarkerKind.TradingAs && tradingAs.IsEmpty)
            {
                tradingAs = part;
                tradingMarker = markers[m].Canonical;
            }
            else if (markers[m].Kind == MarkerKind.CareOf && careOf.IsEmpty)
            {
                careOf = part;
            }
        }

        // "c/o Acme Corp": nothing before the marker, so the text after it is the company.
        if (main.Count == 0 && !careOf.IsEmpty)
        {
            issues.Add(new ParseIssue(ReasonCode.CompanyCareOfOnly, careOf.Span, IssueSeverity.Warning));
            int careFrom = markers[0].First + markers[0].Count;
            int careTo = markers.Count > 1 ? markers[1].First : tokens.Count;
            main = tokens.GetRange(careFrom, careTo - careFrom);
            careOf = default;
        }

        if (main.Count == 0)
        {
            value = default;
            issues.Add(new ParseIssue(ReasonCode.CompanyNoBase, span));
            detail = new ParseDetail(issues, []);
            return OperationStatus.InvalidData;
        }

        NamePart article = default;
        int offset = 0;
        if (main.Count > 1 && NameFolding.LettersAndDigits(main[0].Text(input)) == "the")
        {
            article = new NamePart(main[0].Text(input).ToString(), new SourceSpan(main[0].Start, main[0].Length));
            offset = 1;
        }

        List<NameToken> body = main.GetRange(offset, main.Count - offset);
        CompanyLegalForm? form = null;
        int baseFrom = 0;
        int baseTo = body.Count;
        CompanyFacts flags = CompanyFacts.None;
        LegalFormMatcher.Hit? hit = LegalFormMatcher.Find(input, body, hints);

        // "DBA" and the like are listed as legal forms but are read as trading-as markers everywhere else; a stray character glued to
        // one must not turn it into a form whose canonical text the marker rule then takes apart.
        if (hit is { } probe && IsMarkerSpelling(input, body, probe))
        {
            hit = null;
        }

        if (hit is { } h)
        {
            form = LegalFormMatcher.Resolve(input, body, h, hints, out bool outside, out bool inactive);
            flags |= CompanyFacts.HasLegalForm;
            if (form.IsAmbiguous)
            {
                flags |= CompanyFacts.LegalFormAmbiguous;
                issues.Add(new ParseIssue(ReasonCode.CompanyLegalFormAmbiguous, form.Span, IssueSeverity.Info));
            }

            if (outside)
            {
                issues.Add(new ParseIssue(ReasonCode.CompanyLegalFormOutsideCountry, form.Span, IssueSeverity.Info));
            }

            if (inactive)
            {
                flags |= CompanyFacts.LegalFormInactive;
                issues.Add(new ParseIssue(ReasonCode.CompanyLegalFormInactive, form.Span, IssueSeverity.Info));
            }

            if (h.IsPrefix)
            {
                flags |= CompanyFacts.LegalFormBeforeName;
                baseFrom = h.LastToken + 1;
            }
            else
            {
                baseTo = h.FirstToken;
            }
        }

        NamePart baseName = TextOf(input, body, baseFrom, baseTo);

        // The comma that usually sits before a legal form ("Acme, Inc.") is not a token, so the base ends at its last word.
        if (!article.IsEmpty)
        {
            flags |= CompanyFacts.HasLeadingArticle;
        }

        if (!tradingAs.IsEmpty)
        {
            flags |= CompanyFacts.HasTradingName;
        }

        if (!careOf.IsEmpty)
        {
            flags |= CompanyFacts.HasCareOf;
        }

        StringBuilder sb = new();
        Append(sb, article.Text);
        // The listed abbreviation replaces what was written, except when the two differ only in case ("Oy" is not rewritten to "oy").
        string formText = form is null
            ? string.Empty
            : form.Abbreviation.Length == 0 || string.Equals(form.Text, form.Abbreviation, StringComparison.OrdinalIgnoreCase) ? form.Text : form.Abbreviation;
        if (form is { IsPrefix: true })
        {
            Append(sb, formText);
        }

        Append(sb, baseName.Text);
        if (form is { IsPrefix: false })
        {
            Append(sb, formText);
        }

        if (!tradingAs.IsEmpty)
        {
            Append(sb, tradingMarker);
            Append(sb, tradingAs.Text);
        }

        if (!careOf.IsEmpty)
        {
            Append(sb, "c/o");
            Append(sb, careOf.Text);
        }

        CompanyNameParts parts = new()
        {
            LeadingArticle = article,
            Base = baseName,
            LegalForm = form,
            TradingAs = tradingAs,
            CareOf = careOf,
            TradingMarker = tradingMarker,
        };

        List<ParsedField> fields = [];
        AddField(fields, "leading-article", article);
        AddField(fields, "base", baseName);
        if (form is not null)
        {
            fields.Add(new ParsedField("legal-form", form.Span, 1f, DecisionSource.Rule));
        }

        AddField(fields, "trading-as", tradingAs);
        AddField(fields, "care-of", careOf);
        fields.Sort(static (a, b) => a.Span.Start.CompareTo(b.Span.Start));
        detail = new ParseDetail(issues, fields);
        value = new CompanyName(sb.ToString(), span, flags, parts);
        return OperationStatus.Done;
    }

    /// <summary>Finds the first trading-as or care-of marker in <paramref name="tokens"/>.</summary>
    internal static MarkerKind FindMarker(ReadOnlySpan<char> whole, List<NameToken> tokens, out int index, out int count, out string canonical) =>
        FindMarkerFrom(whole, tokens, 0, out index, out count, out canonical);

    private static MarkerKind FindMarkerFrom(ReadOnlySpan<char> whole, List<NameToken> tokens, int from, out int index, out int count, out string canonical)
    {
        for (int i = from; i < tokens.Count; i++)
        {
            ReadOnlySpan<char> text = tokens[i].Text(whole);
            string folded = NameFolding.LettersAndDigits(text);

            // "doing business as", "trading as", "trading under", "care of": two or three words.
            if (i + 2 < tokens.Count && folded == "doing" && Is(whole, tokens, i + 1, "business") && Is(whole, tokens, i + 2, "as"))
            {
                (index, count, canonical) = (i, 3, "d/b/a");
                return MarkerKind.TradingAs;
            }

            if (i + 1 < tokens.Count && folded == "trading" && (Is(whole, tokens, i + 1, "as") || Is(whole, tokens, i + 1, "under")))
            {
                (index, count, canonical) = (i, 2, "t/a");
                return MarkerKind.TradingAs;
            }

            if (i + 1 < tokens.Count && folded == "care" && Is(whole, tokens, i + 1, "of"))
            {
                (index, count, canonical) = (i, 2, "c/o");
                return MarkerKind.CareOf;
            }

            string lower = text.ToString().TrimEnd(':').ToLowerInvariant();
            if (Array.IndexOf(TradingSingle, lower) >= 0)
            {
                (index, count, canonical) = (i, 1, lower.StartsWith('t') ? "t/a" : "d/b/a");
                return MarkerKind.TradingAs;
            }

            if (Array.IndexOf(CareOfSingle, lower) >= 0)
            {
                (index, count, canonical) = (i, 1, "c/o");
                return MarkerKind.CareOf;
            }
        }

        (index, count, canonical) = (-1, 0, string.Empty);
        return MarkerKind.None;
    }

    private static bool IsMarkerSpelling(ReadOnlySpan<char> whole, List<NameToken> body, LegalFormMatcher.Hit hit)
    {
        for (int i = hit.FirstToken; i <= hit.LastToken; i++)
        {
            string lower = NameFolding.LettersAndDigits(body[i].Text(whole));
            if (lower == "dba")
            {
                return true;
            }
        }

        return false;
    }

    private static bool Is(ReadOnlySpan<char> whole, List<NameToken> tokens, int i, string word) =>
        NameFolding.LettersAndDigits(tokens[i].Text(whole)) == word;

    private static NamePart TextOf(ReadOnlySpan<char> whole, List<NameToken> tokens, int from, int to)
    {
        if (to <= from)
        {
            return default;
        }

        int start = tokens[from].Start;
        int end = tokens[to - 1].End;
        StringBuilder sb = new();
        for (int i = from; i < to; i++)
        {
            if (sb.Length > 0)
            {
                // Keep the comma between words that had one: "Smith, Jones & Partners".
                sb.Append(tokens[i].CommaBefore ? ", " : " ");
            }

            sb.Append(whole.Slice(tokens[i].Start, tokens[i].Length));
        }

        return new NamePart(sb.ToString(), new SourceSpan(start, end - start));
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
}
