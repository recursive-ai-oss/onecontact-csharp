using RecursiveAI.OneContact.Companies;

namespace RecursiveAI.OneContact.Names;

/// <summary>What the rules found in a name string: its class, the people in it (when there are any) and the issues met on the way.</summary>
internal sealed class Analysis
{
    internal NameClass Class { get; set; }

    internal List<PersonDraft> Persons { get; } = [];

    internal List<ParseIssue> Issues { get; } = [];

    internal int Start { get; set; }

    internal int End { get; set; }

    internal bool RestoreCase { get; set; }

    internal bool HasError
    {
        get
        {
            foreach (ParseIssue issue in Issues)
            {
                if (issue.Severity == IssueSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

/// <summary>
/// Classifies a name string (person, company, several people, unknown) and splits several people apart. Rules only: legal forms of
/// the ELF list and company words make a company, connectors between name parts make several people, and what neither settles is
/// left <see cref="NameClass.Unknown"/> with a reason.
/// </summary>
internal static class NameAnalyzer
{
    private const int MaxPieces = 8;

    internal static Analysis Analyze(ReadOnlySpan<char> whole, NameHints hints)
    {
        Analysis analysis = new();
        SourceSpan span = PersonNameParser.Trim(whole, 0, whole.Length);
        analysis.Start = span.Start;
        analysis.End = span.End;
        if (span.IsEmpty)
        {
            analysis.Issues.Add(new ParseIssue(ReasonCode.Empty, new SourceSpan(0, whole.Length)));
            return analysis;
        }

        string[] langs = NameLexicon.Languages(hints);
        analysis.RestoreCase = IsAllOneCase(whole, span.Start, span.End);

        // A semicolon or a line break always separates people.
        List<(int Start, int End)> pieces = [];
        int from = span.Start;
        for (int i = span.Start; i <= span.End; i++)
        {
            if (i == span.End || whole[i] is ';' or '\n' or '\r')
            {
                SourceSpan piece = PersonNameParser.Trim(whole, from, i);
                if (!piece.IsEmpty)
                {
                    pieces.Add((piece.Start, piece.End));
                }

                from = i + 1;
            }
        }

        if (pieces.Count == 0)
        {
            // Nothing but separators.
            analysis.Issues.Add(new ParseIssue(ReasonCode.PersonNoName, span));
            return analysis;
        }

        if (pieces.Count == 1)
        {
            AnalyzePiece(whole, pieces[0].Start, pieces[0].End, hints, langs, analysis);
            return analysis;
        }

        if (pieces.Count > MaxPieces)
        {
            analysis.Issues.Add(new ParseIssue(ReasonCode.PersonTooLong, span));
            return analysis;
        }

        foreach ((int s, int e) in pieces)
        {
            Analysis part = new() { Start = s, End = e, RestoreCase = analysis.RestoreCase };
            AnalyzePiece(whole, s, e, hints, langs, part);
            if (part.Class is not (NameClass.Person or NameClass.Multiple) && !(part.Class == NameClass.Unknown && part.Persons.Count == 1 && !part.HasError))
            {
                analysis.Issues.AddRange(part.Issues);
                analysis.Persons.Clear();
                analysis.Class = NameClass.Unknown;
                if (!analysis.HasError)
                {
                    analysis.Issues.Add(new ParseIssue(ReasonCode.PersonAmbiguousConnector, new SourceSpan(s, e - s)));
                }

                return analysis;
            }

            analysis.Persons.AddRange(part.Persons);
        }

        analysis.Class = analysis.Persons.Count > 1 ? NameClass.Multiple : NameClass.Person;
        return analysis;
    }

    private static void AnalyzePiece(ReadOnlySpan<char> whole, int start, int end, NameHints hints, string[] langs, Analysis result)
    {
        if (LooksLikeCompany(whole, start, end, hints, langs))
        {
            result.Class = NameClass.Company;
            return;
        }

        bool[] skip = new bool[whole.Length];
        PersonNameParser.FindNicknames(whole, start, end, skip);
        List<NameToken> tokens = NameTokenizer.Tokenize(whole, start, end, skip);

        // A comma-separated part that is a company ("Kvarner Promet d.d., Jansen") makes the text one, because reordering the
        // parts of a person's name ("Family, Given" to "Given Family") would put its legal form where it counts.
        if (CommaPartLooksLikeCompany(whole, tokens, hints, langs))
        {
            result.Class = NameClass.Company;
            return;
        }

        // Connectors between words split the text into several people.
        List<int> connectors = [];
        for (int i = 1; i < tokens.Count - 1; i++)
        {
            ReadOnlySpan<char> text = tokens[i].Text(whole);
            if (NameLexicon.IsConnector(text, NameFolding.LettersAndDigits(text), langs) && !tokens[i].CommaBefore)
            {
                connectors.Add(i);
            }
        }

        if (connectors.Count == 0)
        {
            Single(whole, start, end, hints, langs, result);
            return;
        }

        List<(int From, int To)> ranges = [];
        int first = 0;
        foreach (int c in connectors)
        {
            ranges.Add((first, c));
            first = c + 1;
        }

        ranges.Add((first, tokens.Count));

        // "A, B and C": a left part that is a comma list of full names is several people.
        List<(int From, int To)> expanded = [];
        for (int r = 0; r < ranges.Count; r++)
        {
            (int from, int to) = ranges[r];
            if (r == ranges.Count - 1)
            {
                expanded.Add((from, to));
                continue;
            }

            List<(int From, int To)> segments = [];
            int segFrom = from;
            for (int i = from + 1; i < to; i++)
            {
                if (tokens[i].CommaBefore)
                {
                    segments.Add((segFrom, i));
                    segFrom = i;
                }
            }

            segments.Add((segFrom, to));
            bool list = segments.Count > 1;
            foreach ((int a, int b) in segments)
            {
                list &= b - a >= 2;
            }

            if (list)
            {
                expanded.AddRange(segments);
            }
            else
            {
                expanded.Add((from, to));
            }
        }

        if (expanded.Count > MaxPieces)
        {
            result.Issues.Add(new ParseIssue(ReasonCode.PersonTooLong, new SourceSpan(start, end - start)));
            return;
        }

        // "Garcia y Lopez, Maria": the connector sits between the two family names of a "Family, Given" text.
        if (connectors.Count == 1 && !NameLexicon.FamilyFirst(langs))
        {
            ReadOnlySpan<char> joiner = tokens[connectors[0]].Text(whole);
            string joinerFolded = NameFolding.LettersAndDigits(joiner);
            int comma = -1;
            for (int i = 1; i < tokens.Count && comma < 0; i++)
            {
                if (tokens[i].CommaBefore)
                {
                    comma = i;
                }
            }

            if (joiner.Length == 1 && joinerFolded is "y" or "e" or "i" && comma > connectors[0] + 1)
            {
                Single(whole, start, end, hints, langs, result);
                return;
            }
        }

        // One person with two family names joined by "y" or "e": "Maria Garcia y Lopez".
        if (expanded.Count == 2 && connectors.Count == 1)
        {
            ReadOnlySpan<char> connectorText = tokens[connectors[0]].Text(whole);
            string folded = NameFolding.LettersAndDigits(connectorText);
            if (connectorText.Length == 1 && folded is "y" or "e" or "i" && !NameLexicon.FamilyFirst(langs))
            {
                List<int> leftWords = WordsOf(whole, tokens, expanded[0], langs);
                List<int> rightWords = WordsOf(whole, tokens, expanded[1], langs);

                // The second family name is one word, optionally with particles ("de la Torre"); anything longer is another person.
                bool oneFamily = rightWords.Count >= 1;
                for (int i = 0; i < rightWords.Count - 1; i++)
                {
                    oneFamily &= NameLexicon.IsParticle(NameFolding.LettersAndDigits(tokens[rightWords[i]].Text(whole)), langs);
                }

                if (leftWords.Count >= 2 && oneFamily)
                {
                    Single(whole, start, end, hints, langs, result);
                    return;
                }
            }
        }

        // "Jansen B.V. & Partners": a part that is itself a company makes the whole text a company.
        foreach ((int from, int to) in expanded)
        {
            // The part is judged as its own text, the way it reads when it is parsed again by itself.
            int partStart = tokens[from].Start;
            int partEnd = Math.Max(partStart, tokens[to - 1].End);
            string partText = whole[partStart..partEnd].ToString();
            if (LooksLikeCompany(partText, 0, partText.Length, hints, langs))
            {
                result.Class = NameClass.Company;
                return;
            }
        }

        List<PersonDraft> drafts = [];
        List<bool> titleOnly = [];
        foreach ((int from, int to) in expanded)
        {
            int a = tokens[from].Start;
            int b = tokens[to - 1].End;
            if (AllTitles(whole, tokens, from, to, langs))
            {
                List<int> idx = [];
                for (int i = from; i < to; i++)
                {
                    idx.Add(i);
                }

                PersonDraft only = new() { Source = new SourceSpan(a, b - a) };
                only.Prefix = new NamePart(JoinTokens(whole, tokens, idx), new SourceSpan(a, b - a));
                drafts.Add(only);
                titleOnly.Add(true);
                continue;
            }

            PersonDraft? draft = PersonNameParser.ParseRange(whole, a, b, hints, langs, false, result.Issues);
            if (draft is null || !draft.HasCore)
            {
                if (draft is not null)
                {
                    result.Issues.Add(new ParseIssue(ReasonCode.PersonNoName, draft.Source));
                }

                result.Class = NameClass.Unknown;
                return;
            }

            drafts.Add(draft);
            titleOnly.Add(false);
        }

        PersonDraft last = drafts[^1];
        if (titleOnly[^1])
        {
            // A title with nothing after it ("Mr and Mrs") names nobody.
            result.Class = NameClass.Unknown;
            result.Issues.Add(new ParseIssue(ReasonCode.PersonNoName, new SourceSpan(start, end - start)));
            return;
        }

        int lastCore = CoreWords(last);
        for (int i = 0; i < drafts.Count - 1; i++)
        {
            PersonDraft d = drafts[i];
            if (titleOnly[i])
            {
                // "Mr & Mrs John Smith": the title has no name of its own and shares the whole name that follows.
                Inherit(d, last, true);
                continue;
            }

            int core = CoreWords(d);
            if (core == 1 && lastCore >= 2 && !last.Family.IsEmpty)
            {
                // "John and Jane Smith": a lone word before a full name is a given name, and the family name is shared.
                if (d.Given.IsEmpty)
                {
                    d.Given = d.Family;
                    d.Family = default;
                }

                Inherit(d, last, false);
                continue;
            }

            if (core == 1 && lastCore == 1 && d.Prefix.IsEmpty && last.Prefix.IsEmpty)
            {
                string word = NameFolding.LettersAndDigits(d.Given.Text);
                if (!(NameLexicon.IsGivenName(word) || NameLexicon.FormalName(word) != word))
                {
                    result.Class = NameClass.Unknown;
                    result.Persons.Clear();
                    result.Issues.Add(new ParseIssue(ReasonCode.PersonAmbiguousConnector, new SourceSpan(start, end - start)));
                    return;
                }
            }
        }

        // Each part must also read as a person when it is parsed again by itself; if it does not, the connector joined something else.
        for (int i = 0; i < drafts.Count; i++)
        {
            if (titleOnly[i])
            {
                continue;
            }

            string partText = whole[drafts[i].Source.Start..drafts[i].Source.End].ToString();
            Analysis alone = Analyze(partText, hints);
            if (!(alone.Class == NameClass.Person || (alone.Class == NameClass.Unknown && alone.Persons.Count == 1 && !alone.HasError)))
            {
                result.Class = NameClass.Unknown;
                result.Persons.Clear();
                result.Issues.Add(new ParseIssue(ReasonCode.PersonAmbiguousConnector, new SourceSpan(start, end - start)));
                return;
            }
        }

        foreach (PersonDraft d in drafts)
        {
            if (ReadsAsCompanyInReadingOrder(whole, d, result.RestoreCase, hints, langs))
            {
                result.Class = NameClass.Unknown;
                result.Issues.Add(new ParseIssue(ReasonCode.PersonNotAPerson, new SourceSpan(start, end - start)));
                return;
            }
        }

        result.Persons.AddRange(drafts);
        result.Class = NameClass.Multiple;
    }

    private static void Single(ReadOnlySpan<char> whole, int start, int end, NameHints hints, string[] langs, Analysis result)
    {
        PersonDraft? draft = PersonNameParser.ParseRange(whole, start, end, hints, langs, false, result.Issues);
        if (draft is null || !draft.HasCore)
        {
            // Titles and suffixes alone name nobody, and their canonical text would not read back as a person.
            if (draft is not null)
            {
                result.Issues.Add(new ParseIssue(ReasonCode.PersonNoName, draft.Source));
            }

            result.Class = NameClass.Unknown;
            return;
        }

        // The canonical form puts the parts in reading order; if that text would read as a company, so does the input.
        if (ReadsAsCompanyInReadingOrder(whole, draft, result.RestoreCase, hints, langs))
        {
            result.Class = NameClass.Unknown;
            result.Persons.Clear();
            result.Issues.Add(new ParseIssue(ReasonCode.PersonNotAPerson, new SourceSpan(start, end - start)));
            return;
        }

        result.Persons.Add(draft);

        // One bare word (no title) cannot be told from a company or a family name: rules leave it Unknown.
        bool bare = draft.Prefix.IsEmpty && draft.Family.IsEmpty && draft.Middle.IsEmpty && draft.Nickname.IsEmpty && !draft.Given.IsEmpty;
        result.Class = bare ? NameClass.Unknown : NameClass.Person;
        if (!bare && !HasPersonEvidence(whole, draft, langs))
        {
            // Two or three plain words are taken as a person because that is what name fields hold; the issue says nothing proved it.
            draft.Issues.Add(new ParseIssue(ReasonCode.PersonNoEvidence, draft.Source, IssueSeverity.Info));
        }

        result.Issues.AddRange(draft.Issues);
    }

    // A title, a suffix, a nickname, a particle, a second family name, a comma order, an initial or a known given name is evidence of a person.
    private static bool HasPersonEvidence(ReadOnlySpan<char> whole, PersonDraft d, string[] langs)
    {
        if (!d.Prefix.IsEmpty || !d.Suffix.IsEmpty || !d.ProfessionalSuffix.IsEmpty || !d.Nickname.IsEmpty || !d.Particle.IsEmpty
            || !d.SecondFamily.IsEmpty || d.Order != NameOrder.GivenFamily)
        {
            return true;
        }

        _ = langs;
        foreach (NamePart part in new[] { d.Given, d.Middle, d.Family })
        {
            foreach (string word in part.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string folded = NameFolding.LettersAndDigits(word);
                if (folded.Length == 1 || NameLexicon.IsGivenName(folded) || NameLexicon.FormalName(folded) != folded)
                {
                    return true;
                }
            }
        }

        _ = whole;
        return false;
    }

    private static bool CommaPartLooksLikeCompany(ReadOnlySpan<char> whole, List<NameToken> tokens, NameHints hints, string[] langs)
    {
        int segmentFrom = 0;
        for (int i = 1; i <= tokens.Count; i++)
        {
            if (i == tokens.Count || tokens[i].CommaBefore)
            {
                bool wholeText = segmentFrom == 0 && i == tokens.Count;
                if (!wholeText && i - segmentFrom >= 2 && LooksLikeCompany(whole, tokens[segmentFrom].Start, tokens[i - 1].End, hints, langs))
                {
                    return true;
                }

                segmentFrom = i;
            }
        }

        return false;
    }

    private static bool ReadsAsCompanyInReadingOrder(ReadOnlySpan<char> whole, PersonDraft d, bool restoreCase, NameHints hints, string[] langs)
    {
        string text = PersonNameBuilder.Build(whole, d, restoreCase, hints, out _).Canonical;
        if (text.Length == 0)
        {
            return false;
        }

        return LooksLikeCompany(text, 0, text.Length, hints, langs) || CommaPartLooksLikeCompany(text, NameTokenizer.Tokenize(text, 0, text.Length, default), hints, langs);
    }

    private static void Inherit(PersonDraft d, PersonDraft from, bool everything)
    {
        if (everything)
        {
            d.Given = from.Given;
            d.Middle = from.Middle;
        }

        d.Particle = from.Particle;
        d.Family = from.Family;
        d.SecondFamily = from.SecondFamily;
        d.FamilyConnector = from.FamilyConnector;
        d.Order = from.Order;
    }

    private static int CoreWords(PersonDraft d)
    {
        int n = 0;
        foreach (NamePart p in new[] { d.Given, d.Middle, d.Particle, d.Family, d.SecondFamily })
        {
            if (!p.IsEmpty)
            {
                n += p.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            }
        }

        return n;
    }

    private static List<int> WordsOf(ReadOnlySpan<char> whole, List<NameToken> tokens, (int From, int To) range, string[] langs)
    {
        List<int> words = [];
        int lo = range.From;
        while (lo < range.To)
        {
            int size = PersonNameParser.TitleSize(whole, tokens, lo, range.To, langs);
            if (size == 0)
            {
                break;
            }

            lo += size;
        }

        for (int i = lo; i < range.To; i++)
        {
            if (tokens[i].HasLetterOrDigit(whole))
            {
                words.Add(i);
            }
        }

        return words;
    }

    private static bool AllTitles(ReadOnlySpan<char> whole, List<NameToken> tokens, int from, int to, string[] langs)
    {
        int at = from;
        while (at < to)
        {
            int size = PersonNameParser.TitleSize(whole, tokens, at, to, langs);
            if (size == 0)
            {
                return false;
            }

            at += size;
        }

        return true;
    }

    private static string JoinTokens(ReadOnlySpan<char> whole, List<NameToken> tokens, List<int> idx)
    {
        System.Text.StringBuilder sb = new();
        foreach (int i in idx)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(whole.Slice(tokens[i].Start, tokens[i].Length));
        }

        return sb.ToString();
    }

    private static bool IsAllOneCase(ReadOnlySpan<char> whole, int start, int end)
    {
        int letters = 0;
        bool upper = false;
        bool lower = false;
        for (int i = start; i < end; i++)
        {
            char c = whole[i];
            if (char.IsUpper(c))
            {
                upper = true;
                letters++;
            }
            else if (char.IsLower(c))
            {
                lower = true;
                letters++;
            }
        }

        return letters >= 2 && upper != lower;
    }

    /// <summary>Returns whether the range is a company: a legal form of the ELF list, a company word, a trading-as or care-of marker.</summary>
    internal static bool LooksLikeCompany(ReadOnlySpan<char> whole, int start, int end, NameHints hints, string[] langs)
    {
        _ = langs;
        List<NameToken> tokens = NameTokenizer.Tokenize(whole, start, end, default);
        if (tokens.Count == 0)
        {
            return false;
        }

        if (CompanyNameParser.FindMarker(whole, tokens, out _, out _, out _) != CompanyNameParser.MarkerKind.None)
        {
            return true;
        }

        if (LegalFormMatcher.Find(whole, tokens, hints) is not null)
        {
            return true;
        }

        bool givenFirst = tokens.Count <= 3 && NameLexicon.IsGivenName(NameFolding.LettersAndDigits(tokens[0].Text(whole)));
        for (int i = 0; i < tokens.Count; i++)
        {
            string folded = NameFolding.LettersAndDigits(tokens[i].Text(whole));
            if (NameLexicon.IsCompanyWord(folded))
            {
                if (givenFirst && i == tokens.Count - 1 && i > 0 && NameLexicon.IsSurnameCompanyWord(folded))
                {
                    continue;
                }

                return true;
            }

            // "Smith & Co": the abbreviation after an ampersand.
            if (folded == "co" && i > 0 && tokens[i - 1].Text(whole) is "&" or "and")
            {
                return true;
            }
        }

        return false;
    }
}
