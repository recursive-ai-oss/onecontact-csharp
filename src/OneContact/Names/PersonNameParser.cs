using System.Text;

namespace RecursiveAI.OneContact.Names;

/// <summary>The parts of one person's name as the rules found them, before they are turned into a <see cref="PersonName"/>.</summary>
internal sealed class PersonDraft
{
    internal NamePart Prefix { get; set; }

    internal NamePart Given { get; set; }

    internal NamePart Middle { get; set; }

    internal NamePart Particle { get; set; }

    internal NamePart Family { get; set; }

    internal NamePart SecondFamily { get; set; }

    internal NamePart Suffix { get; set; }

    internal NamePart ProfessionalSuffix { get; set; }

    internal NamePart Nickname { get; set; }

    internal string FamilyConnector { get; set; } = string.Empty;

    internal NameOrder Order { get; set; }

    internal SourceSpan Source { get; set; }

    internal List<ParseIssue> Issues { get; } = [];

    internal bool HasCore => !Given.IsEmpty || !Family.IsEmpty || !Middle.IsEmpty;
}

/// <summary>
/// Reads one person's name out of a range of the original input: nickname, titles, suffixes, order, particles, second family names.
/// Every part keeps its offsets into the input. Rules only; the casing is restored later, when the draft becomes a value.
/// </summary>
internal static class PersonNameParser
{
    private const int MaxCoreWords = 8;

    /// <summary>The input as it is read for one person: the tokens of one comma-separated segment, minus titles and suffixes.</summary>
    private readonly record struct Segment(int From, int To);

    internal static System.Buffers.OperationStatus TryParse(ReadOnlySpan<char> input, out PersonName value, out ParseDetail detail) =>
        TryParse(input, default, out value, out detail);

    internal static System.Buffers.OperationStatus TryParse(ReadOnlySpan<char> input, NameHints hints, out PersonName value, out ParseDetail detail)
    {
        Analysis analysis = NameAnalyzer.Analyze(input, hints);
        switch (analysis.Class)
        {
            case NameClass.Person:
            case NameClass.Unknown when analysis.Persons.Count == 1 && !analysis.HasError:
                value = PersonNameBuilder.Build(input, analysis.Persons[0], analysis.RestoreCase, hints, out detail);
                return System.Buffers.OperationStatus.Done;
            case NameClass.Company:
                value = default;
                detail = ParseDetail.Failure(ReasonCode.PersonNotAPerson, new SourceSpan(analysis.Start, analysis.End - analysis.Start));
                return System.Buffers.OperationStatus.InvalidData;
            case NameClass.Multiple:
                value = default;
                detail = ParseDetail.Failure(ReasonCode.PersonMultiple, new SourceSpan(analysis.Start, analysis.End - analysis.Start));
                return System.Buffers.OperationStatus.InvalidData;
            default:
                value = default;
                detail = analysis.Issues.Count > 0
                    ? new ParseDetail(analysis.Issues, [])
                    : ParseDetail.Failure(ReasonCode.UnrecognizedKind, new SourceSpan(analysis.Start, analysis.End - analysis.Start));
                return System.Buffers.OperationStatus.InvalidData;
        }
    }

    /// <summary>Finds quoted and parenthesized nicknames in a range; their characters (and the delimiters) are set in <paramref name="skip"/>.</summary>
    internal static List<SourceSpan> FindNicknames(ReadOnlySpan<char> whole, int start, int end, bool[] skip)
    {
        List<SourceSpan> nicknames = [];
        int i = start;
        while (i < end)
        {
            char open = whole[i];
            char close = open switch
            {
                '"' => '"',
                '\u201C' => '\u201D',
                '\u201E' => '\u201C',
                '\u00AB' => '\u00BB',
                '(' => ')',
                '\u2018' => '\u2019',
                '\'' => '\'',
                _ => '\0',
            };

            bool single = open is '\'' or '\u2018';
            if (close == '\0' || (single && i > start && !char.IsWhiteSpace(whole[i - 1])))
            {
                i++;
                continue;
            }

            int j = i + 1;
            while (j < end && whole[j] != close)
            {
                j++;
            }

            bool closed = j < end;
            if (closed && single)
            {
                // A single quote closes only at the end of a word: O'Brien and l'Heureux are not nicknames.
                closed = j + 1 >= end || char.IsWhiteSpace(whole[j + 1]) || whole[j + 1] == ',';
            }

            if (!closed)
            {
                i++;
                continue;
            }

            int a = i + 1;
            int b = j;
            while (a < b && char.IsWhiteSpace(whole[a]))
            {
                a++;
            }

            while (b > a && char.IsWhiteSpace(whole[b - 1]))
            {
                b--;
            }

            bool hasLetter = false;
            for (int k = a; k < b; k++)
            {
                hasLetter |= char.IsLetter(whole[k]);
            }

            if (hasLetter && b - a >= (single ? 2 : 1))
            {
                nicknames.Add(new SourceSpan(a, b - a));
                for (int k = i; k <= j; k++)
                {
                    skip[k] = true;
                }
            }

            i = j + 1;
        }

        return nicknames;
    }

    /// <summary>
    /// Reads the range <paramref name="start"/> to <paramref name="end"/> of <paramref name="whole"/> as one person. Returns
    /// <see langword="null"/> and adds an error issue to <paramref name="issues"/> when the range holds no person.
    /// </summary>
    /// <remarks>With <c>singleWordIsGiven</c> a single word after a title is read as a given name (a shared family name follows elsewhere).</remarks>
    internal static PersonDraft? ParseRange(ReadOnlySpan<char> whole, int start, int end, NameHints hints, string[] langs, bool singleWordIsGiven, List<ParseIssue> issues)
    {
        bool[] skip = new bool[whole.Length];
        List<SourceSpan> nicknames = FindNicknames(whole, start, end, skip);
        List<NameToken> tokens = NameTokenizer.Tokenize(whole, start, end, skip);

        PersonDraft draft = new() { Source = Trim(whole, start, end) };
        if (nicknames.Count > 0)
        {
            int a = nicknames[0].Start;
            int b = nicknames[^1].End;
            draft.Nickname = new NamePart(JoinSpans(whole, nicknames), new SourceSpan(a, b - a));
        }

        if (tokens.Count == 0)
        {
            issues.Add(new ParseIssue(ReasonCode.PersonNoName, draft.Source));
            return null;
        }

        // Segments are the comma-separated runs of words.
        List<Segment> segments = [];
        int from = 0;
        for (int i = 1; i < tokens.Count; i++)
        {
            if (tokens[i].CommaBefore)
            {
                segments.Add(new Segment(from, i));
                from = i;
            }
        }

        segments.Add(new Segment(from, tokens.Count));

        List<int> prefixTokens = [];
        List<int> generationalTokens = [];
        List<int> professionalTokens = [];
        List<Segment> core = [];
        for (int s = 0; s < segments.Count; s++)
        {
            int lo = segments[s].From;
            int hi = segments[s].To;
            List<int> segmentGenerational = [];
            List<int> segmentProfessional = [];

            // Suffixes from the end; a later segment may be nothing but suffixes ("John Smith, PhD").
            while (hi > lo)
            {
                int t = hi - 1;
                bool dotted = tokens[t].Text(whole).Contains('.');
                string folded = NameFolding.LettersAndDigits(tokens[t].Text(whole));
                bool alone = t == lo && s > 0;
                bool canPeel = t > lo || s > 0;
                if (!canPeel)
                {
                    break;
                }

                if (NameLexicon.IsGenerational(folded) && (t > lo || alone))
                {
                    segmentGenerational.Insert(0, t);
                }
                else if (NameLexicon.IsProfessional(folded, dotted || alone) && (t > lo || alone))
                {
                    segmentProfessional.Insert(0, t);
                }
                else
                {
                    break;
                }

                hi--;
            }

            generationalTokens.AddRange(segmentGenerational);
            professionalTokens.AddRange(segmentProfessional);

            // Titles from the start of every segment that still has words.
            while (lo < hi)
            {
                int size = TitleSize(whole, tokens, lo, hi, langs);
                if (size == 0)
                {
                    break;
                }

                for (int k = 0; k < size; k++)
                {
                    prefixTokens.Add(lo + k);
                }

                lo += size;
            }

            if (hi > lo)
            {
                core.Add(new Segment(lo, hi));
            }
        }

        if (prefixTokens.Count > 0)
        {
            draft.Prefix = Part(whole, tokens, prefixTokens);
        }

        if (generationalTokens.Count > 0)
        {
            draft.Suffix = Part(whole, tokens, generationalTokens);
        }

        if (professionalTokens.Count > 0)
        {
            draft.ProfessionalSuffix = Part(whole, tokens, professionalTokens);
        }

        if (core.Count == 0)
        {
            issues.Add(new ParseIssue(ReasonCode.PersonNoName, draft.Source));
            return null;
        }

        if (core.Count > 2)
        {
            issues.Add(new ParseIssue(ReasonCode.PersonMultiple, draft.Source));
            return null;
        }

        // Words with digits or at signs are not a person's name.
        foreach (Segment seg in core)
        {
            for (int i = seg.From; i < seg.To; i++)
            {
                foreach (char c in tokens[i].Text(whole))
                {
                    if (char.IsDigit(c) || c == '@')
                    {
                        issues.Add(new ParseIssue(ReasonCode.PersonUnexpectedCharacters, new SourceSpan(tokens[i].Start, tokens[i].Length)));
                        return null;
                    }
                }
            }
        }

        // Words without any letter (a dash, a slash) carry nothing.
        List<List<int>> words = [];
        foreach (Segment seg in core)
        {
            List<int> list = [];
            for (int i = seg.From; i < seg.To; i++)
            {
                if (tokens[i].HasLetterOrDigit(whole))
                {
                    list.Add(i);
                }
            }

            if (list.Count > 0)
            {
                words.Add(list);
            }
        }

        int total = 0;
        foreach (List<int> w in words)
        {
            total += w.Count;
        }

        if (words.Count == 0)
        {
            issues.Add(new ParseIssue(ReasonCode.PersonNoName, draft.Source));
            return null;
        }

        if (total > MaxCoreWords)
        {
            issues.Add(new ParseIssue(ReasonCode.PersonTooLong, draft.Source));
            return null;
        }

        // A connector word inside one name is allowed only between two family names ("Garcia y Lopez"); anywhere else it joins several names.
        foreach (List<int> list in words)
        {
            for (int j = 0; j < list.Count; j++)
            {
                ReadOnlySpan<char> text = tokens[list[j]].Text(whole);
                string folded = NameFolding.LettersAndDigits(text);
                if (!NameLexicon.IsConnector(text, folded, langs))
                {
                    continue;
                }

                bool betweenFamilies = text.Length == 1 && folded is "y" or "e" or "i" && words.Count == 1 && j > 0 && j < list.Count - 1 && !NameLexicon.FamilyFirst(langs);
                if (!betweenFamilies)
                {
                    issues.Add(new ParseIssue(ReasonCode.PersonAmbiguousConnector, new SourceSpan(tokens[list[j]].Start, tokens[list[j]].Length)));
                    return null;
                }
            }
        }

        bool twoFamilies = NameLexicon.TwoFamilyNames(langs);
        if (words.Count == 2)
        {
            // "Family, Given Middle": the first segment is the family name, the second the given names.
            draft.Order = NameOrder.FamilyCommaGiven;
            List<int> givenWords = words[1];

            // "Berg, Jan van den": the particles after the given names belong to the family name.
            int tail = givenWords.Count;
            while (tail > 1 && NameLexicon.IsParticle(NameFolding.LettersAndDigits(tokens[givenWords[tail - 1]].Text(whole)), langs))
            {
                tail--;
            }

            SplitFamily(whole, tokens, words[0], twoFamilies, langs, draft);
            if (tail < givenWords.Count)
            {
                NamePart trailing = Part(whole, tokens, givenWords.GetRange(tail, givenWords.Count - tail));
                draft.Particle = draft.Particle.IsEmpty
                    ? trailing
                    : new NamePart(draft.Particle.Text + " " + trailing.Text, new SourceSpan(draft.Particle.Span.Start, trailing.Span.End - draft.Particle.Span.Start));
            }

            SplitGiven(whole, tokens, givenWords.GetRange(0, tail), draft);
            return draft;
        }

        List<int> t0 = words[0];
        if (t0.Count == 1)
        {
            if (draft.Prefix.IsEmpty || singleWordIsGiven)
            {
                draft.Given = Part(whole, tokens, t0);
                draft.Issues.Add(new ParseIssue(ReasonCode.PersonSingleWord, draft.Given.Span, IssueSeverity.Info));
            }
            else
            {
                draft.Family = Part(whole, tokens, t0);
            }

            return draft;
        }

        if (NameLexicon.FamilyFirst(langs))
        {
            // Hungarian and other family-first usage: "Family Given Middle".
            draft.Order = NameOrder.FamilyGiven;
            draft.Family = Part(whole, tokens, [t0[0]]);
            draft.Given = Part(whole, tokens, [t0[1]]);
            if (t0.Count > 2)
            {
                draft.Middle = Part(whole, tokens, t0.GetRange(2, t0.Count - 2));
            }

            return draft;
        }

        // A connector between two family names: "Maria Garcia y Lopez".
        int connector = -1;
        for (int i = 1; i < t0.Count - 1; i++)
        {
            ReadOnlySpan<char> text = tokens[t0[i]].Text(whole);
            if (text.Length == 1 && NameLexicon.IsConnector(text, NameFolding.LettersAndDigits(text), langs) && NameFolding.LettersAndDigits(text) is "y" or "e" or "i")
            {
                connector = i;
                break;
            }
        }

        if (connector > 0)
        {
            List<int> left = t0.GetRange(0, connector);
            List<int> right = t0.GetRange(connector + 1, t0.Count - connector - 1);
            draft.FamilyConnector = tokens[t0[connector]].Text(whole).ToString();
            draft.SecondFamily = Part(whole, tokens, right);
            if (left.Count == 1)
            {
                draft.Family = Part(whole, tokens, left);
            }
            else
            {
                SplitWestern(whole, tokens, left, langs, draft);
            }

            return draft;
        }

        if (twoFamilies && t0.Count >= 3)
        {
            SplitSpanish(whole, tokens, t0, langs, draft);
            return draft;
        }

        SplitWestern(whole, tokens, t0, langs, draft);
        return draft;
    }

    /// <summary>The number of tokens of a title at <paramref name="at"/> (up to three words, or one dotted run such as <c>Prof.Dr.</c>), or zero.</summary>
    internal static int TitleSize(ReadOnlySpan<char> whole, List<NameToken> tokens, int at, int hi, string[] langs)
    {
        for (int size = Math.Min(3, hi - at); size >= 1; size--)
        {
            StringBuilder sb = new();
            for (int k = 0; k < size; k++)
            {
                sb.Append(NameFolding.LettersAndDigits(tokens[at + k].Text(whole)));
            }

            if (size == 1 ? NameLexicon.IsTitle(sb.ToString(), langs) : NameLexicon.IsMultiWordTitle(sb.ToString(), langs))
            {
                return size;
            }
        }

        // "Prof.Dr." and "Dipl.-Ing.": a single token whose dot-separated pieces are all titles.
        ReadOnlySpan<char> text = tokens[at].Text(whole);
        if (text.Contains('.') || text.Contains('-'))
        {
            bool all = true;
            bool any = false;
            int pieceStart = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                if (i == text.Length || text[i] is '.' or '-')
                {
                    ReadOnlySpan<char> piece = text[pieceStart..i];
                    if (!piece.IsEmpty)
                    {
                        any = true;
                        all &= NameLexicon.IsTitle(NameFolding.LettersAndDigits(piece), langs);
                    }

                    pieceStart = i + 1;
                }
            }

            if (any && all)
            {
                return 1;
            }
        }

        return 0;
    }

    private static void SplitGiven(ReadOnlySpan<char> whole, List<NameToken> tokens, List<int> words, PersonDraft draft)
    {
        draft.Given = Part(whole, tokens, [words[0]]);
        if (words.Count > 1)
        {
            draft.Middle = Part(whole, tokens, words.GetRange(1, words.Count - 1));
        }
    }

    // The family name block of a "Family, Given" text: leading particles, then the family name, then (Spanish) the second one.
    private static void SplitFamily(ReadOnlySpan<char> whole, List<NameToken> tokens, List<int> words, bool twoFamilies, string[] langs, PersonDraft draft)
    {
        int p = 0;
        while (p < words.Count - 1 && NameLexicon.IsParticle(NameFolding.LettersAndDigits(tokens[words[p]].Text(whole)), langs))
        {
            p++;
        }

        if (p > 0)
        {
            draft.Particle = Part(whole, tokens, words.GetRange(0, p));
        }

        List<int> rest = words.GetRange(p, words.Count - p);
        if (twoFamilies && rest.Count >= 2)
        {
            draft.Family = Part(whole, tokens, [rest[0]]);
            draft.SecondFamily = Part(whole, tokens, rest.GetRange(1, rest.Count - 1));
        }
        else
        {
            draft.Family = Part(whole, tokens, rest);
        }
    }

    // Given [Middle...] [Particle...] Family.
    private static void SplitWestern(ReadOnlySpan<char> whole, List<NameToken> tokens, List<int> words, string[] langs, PersonDraft draft)
    {
        int last = words.Count - 1;
        int p = last;
        while (p - 1 >= 0 && NameLexicon.IsParticle(NameFolding.LettersAndDigits(tokens[words[p - 1]].Text(whole)), langs))
        {
            p--;
        }

        // "Van Der Berg": nothing but particles before the family name means there is no given name.
        if (p > 0 || words.Count == 1)
        {
            draft.Family = Part(whole, tokens, [words[last]]);
            if (p < last)
            {
                draft.Particle = Part(whole, tokens, words.GetRange(p, last - p));
            }

            if (p > 0)
            {
                draft.Given = Part(whole, tokens, [words[0]]);
                if (p > 1)
                {
                    draft.Middle = Part(whole, tokens, words.GetRange(1, p - 1));
                }
            }
        }
        else
        {
            draft.Family = Part(whole, tokens, [words[last]]);
            draft.Particle = Part(whole, tokens, words.GetRange(0, last));
        }
    }

    // Spanish and Catalan: Given [Given...] Family SecondFamily, with "Juan Carlos Perez" read as one family name.
    private static void SplitSpanish(ReadOnlySpan<char> whole, List<NameToken> tokens, List<int> words, string[] langs, PersonDraft draft)
    {
        int n = words.Count;
        int lastBlock = n - 1;
        while (lastBlock - 1 >= 0 && NameLexicon.IsParticle(NameFolding.LettersAndDigits(tokens[words[lastBlock - 1]].Text(whole)), langs))
        {
            lastBlock--;
        }

        int remaining = lastBlock;
        bool two = false;
        int famBlock = -1;
        if (remaining >= 2)
        {
            famBlock = remaining - 1;
            while (famBlock - 1 >= 0 && NameLexicon.IsParticle(NameFolding.LettersAndDigits(tokens[words[famBlock - 1]].Text(whole)), langs))
            {
                famBlock--;
            }

            two = famBlock >= 1;
        }

        // "Ana Maria Garcia": three words whose middle one is the second half of a compound given name have one family name.
        if (two && famBlock == 1 && lastBlock == 2 && NameLexicon.IsCompoundGivenSecond(NameFolding.LettersAndDigits(tokens[words[1]].Text(whole))))
        {
            two = false;
        }

        if (!two)
        {
            // Given names, then one family name (with its particles).
            SplitWestern(whole, tokens, words, langs, draft);
            return;
        }

        draft.Given = Part(whole, tokens, [words[0]]);
        if (famBlock > 1)
        {
            draft.Middle = Part(whole, tokens, words.GetRange(1, famBlock - 1));
        }

        // Particles of the first family name belong to it ("de la Cruz Vega"); they are kept in the particle part.
        int famLast = remaining - 1;
        if (famLast > famBlock)
        {
            draft.Particle = Part(whole, tokens, words.GetRange(famBlock, famLast - famBlock));
        }

        draft.Family = Part(whole, tokens, [words[famLast]]);
        draft.SecondFamily = Part(whole, tokens, words.GetRange(lastBlock, n - lastBlock));
    }

    private static NamePart Part(ReadOnlySpan<char> whole, List<NameToken> tokens, List<int> indexes)
    {
        int first = tokens[indexes[0]].Start;
        int last = tokens[indexes[^1]].End;
        StringBuilder sb = new();
        foreach (int i in indexes)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(whole.Slice(tokens[i].Start, tokens[i].Length));
        }

        return new NamePart(sb.ToString(), new SourceSpan(first, last - first));
    }

    private static string JoinSpans(ReadOnlySpan<char> whole, List<SourceSpan> spans)
    {
        StringBuilder sb = new();
        foreach (SourceSpan span in spans)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(whole.Slice(span.Start, span.Length));
        }

        return sb.ToString();
    }

    internal static SourceSpan Trim(ReadOnlySpan<char> whole, int start, int end)
    {
        while (start < end && char.IsWhiteSpace(whole[start]))
        {
            start++;
        }

        while (end > start && char.IsWhiteSpace(whole[end - 1]))
        {
            end--;
        }

        return new SourceSpan(start, end - start);
    }
}
