using System.Collections.Frozen;
using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneContact.Names;

namespace RecursiveAI.OneContact.Extraction.Text;

/// <summary>A person's or an organization's name that the rules found in visible text.</summary>
/// <param name="Start">Index of the first character in the view's text.</param>
/// <param name="End">Index past the last character.</param>
/// <param name="Organization">True for an organization, false for a person.</param>
/// <param name="Line">The line the name begins on.</param>
internal readonly record struct NameHit(int Start, int End, bool Organization, int Line);

/// <summary>
/// Finds the people and organizations a text states without a label. A bare run of capitalized words is not a name: nothing here accepts
/// one on its shape alone. A person is a run of two to five name words that has evidence next to it: an email address or phone number it
/// introduces or is introduced by, a job title beside it, a word that points at a person ("contact", "ask for"), a closing line above it
/// ("Regards,"), or a line of its own over a job title or contact lines (a signature, a staff card). An organization is a run that ends
/// in a legal form the core parser accepts (<c>Acme Widgets, Inc.</c>), the name after a copyright sign, the employer after a job title
/// and "at", or the line under a person's name and job title. Job titles are evidence and are never returned.
/// The text comes only from the page; nothing is made up from an email address or a domain.
/// </summary>
internal sealed class NameCandidates
{
    private const byte KindEmail = 1;
    private const byte KindPhone = 2;
    private const byte KindWeb = 4;
    private const byte KindAddress = 8;
    private const int MaxRun = 5;

    /// <summary>Set by the evaluation tools to see which evidence accepted a person; null in the library.</summary>
    internal static Action<string>? Trace { get; set; }

    private readonly TextView _view;
    private readonly string _text;
    private readonly List<(FieldKind Kind, int Start, int End, int Line)> _fields;
    private readonly Func<int, int, bool> _isTaken;
    private readonly Func<int, int, bool> _separated;
    private readonly byte[] _kinds;
    private readonly List<Tok>?[] _tokens;
    private readonly List<NameHit> _hits = [];
    private readonly bool[] _own;
    private readonly int[] _lastPersonEnd;
    private readonly bool[] _aloneName;

    // The free-text rules (a given name beside a family name, a verb after the name, lists, lower case, "Last, First") are for text a person
    // typed: notes, messages, comments. On a web page the same shapes are bylines, testimonials and product names, so a page keeps the
    // rules that need a label, a title or an address next to the name.
    private readonly bool _loose;
    private readonly List<(FieldKind Kind, int Start, int End)>?[] _linked;

    private NameCandidates(TextView view, List<(FieldKind, int, int, int)> fields, Func<int, int, bool> isTaken, Func<int, int, bool> separated)
    {
        _separated = separated;
        _view = view;
        _loose = view.IsPlain;
        _text = view.Text;
        _fields = fields;
        _isTaken = isTaken;
        _kinds = new byte[view.Lines.Count];
        _tokens = new List<Tok>?[view.Lines.Count];
        _own = new bool[view.Text.Length];
        _lastPersonEnd = new int[view.Lines.Count];
        Array.Fill(_lastPersonEnd, -1);
        _aloneName = new bool[view.Lines.Count];
        _linked = new List<(FieldKind, int, int)>?[view.Lines.Count];
        foreach ((FieldKind kind, int fieldStart, int fieldEnd, int line) in fields)
        {
            if (line < 0 || line >= _kinds.Length)
            {
                continue;
            }

            if (kind is FieldKind.Email or FieldKind.Phone or FieldKind.Address)
            {
                (_linked[line] ??= []).Add((kind, fieldStart, fieldEnd));
            }

            _kinds[line] |= kind switch
            {
                FieldKind.Email => KindEmail,
                FieldKind.Phone => KindPhone,
                FieldKind.Website => KindWeb,
                FieldKind.Address => KindAddress,
                _ => (byte)0,
            };
        }
    }

    /// <summary>Finds the names of a view.</summary>
    /// <param name="view">The visible text.</param>
    /// <param name="fields">The other fields already read from it (kind, start, end, line), which give names their evidence.</param>
    /// <param name="isTaken">Whether any character of a range already belongs to a field.</param>
    /// <param name="separated">Whether the characters at two text indexes sit in elements that are neither the same nor nested (two spans side by side), which ends an organization's name.</param>
    /// <returns>The names in order of position, without overlap.</returns>
    public static List<NameHit> Find(TextView view, List<(FieldKind, int, int, int)> fields, Func<int, int, bool> isTaken, Func<int, int, bool> separated)
    {
        var finder = new NameCandidates(view, fields, isTaken, separated);
        finder.Run();
        return finder._hits;
    }

    // A word of a line. The folded and lower-case spellings and the kind are worked out once, on first use: the passes below ask for them
    // over and over for the same word.
    private sealed class Tok(int start, int end, int coreStart, int coreEnd, bool leadPunct, bool trailPunct)
    {
        public readonly int Start = start;
        public readonly int End = end;
        public readonly int CoreStart = coreStart;
        public readonly int CoreEnd = coreEnd;
        public readonly bool LeadPunct = leadPunct;
        public bool TrailPunct = trailPunct;
        public string? Text;
        public string? Lower;
        public string? Fold;
        public Kind? KindCache;
    }

    // ---------------------------------------------------------------- tokens

    private List<Tok> TokensOf(int line)
    {
        if (_tokens[line] is { } cached)
        {
            return cached;
        }

        var list = new List<Tok>(8);
        TextLine l = _view.Lines[line];
        int i = l.Start;
        bool breakBefore = false;
        while (i < l.End)
        {
            while (i < l.End && char.IsWhiteSpace(_text[i]))
            {
                i++;
            }

            int s = i;
            while (i < l.End && !char.IsWhiteSpace(_text[i]))
            {
                i++;
            }

            if (i == s)
            {
                break;
            }

            int cs = s;
            while (cs < i && IsLeadPunct(_text[cs]))
            {
                cs++;
            }

            int ce = i;
            while (ce > cs && IsTrailPunct(_text[ce - 1]))
            {
                ce--;
            }

            if (ce <= cs)
            {
                breakBefore = true;
                if (list.Count > 0)
                {
                    list[^1].TrailPunct = true;
                }

                continue;
            }

            list.Add(new Tok(s, i, cs, ce, cs > s || breakBefore, ce < i));
            breakBefore = false;
        }

        _tokens[line] = list;
        return list;
    }

    // A word's core keeps its letters, digits and the marks that belong inside names ("O'Brien", "Smith-Jones", "Inc.", "AT&T"); every
    // other mark at its front or back (quotes, brackets, bullets, stars, commas) is not part of it.
    private static bool IsLeadPunct(char c) => !(char.IsLetterOrDigit(c) || c is '&' or '@' or '+' or '$' or '#');

    private static bool IsTrailPunct(char c) => !(char.IsLetterOrDigit(c) || c is '.' or '&' or '+' or '\'' or '\u2019');

    private string Word(Tok t) => t.Text ??= _text.Substring(t.CoreStart, t.CoreEnd - t.CoreStart);

    private string LowerWord(Tok t) => t.Lower ??= Word(t).ToLowerInvariant();

    private string Folded(Tok t)
    {
        if (t.Fold is { } cached)
        {
            return cached;
        }

        ReadOnlySpan<char> w = _text.AsSpan(t.CoreStart, t.CoreEnd - t.CoreStart);
        return t.Fold = NameFolding.LettersAndDigits(w.TrimEnd('.'));
    }

    // Words in a run are joined by white space alone: no comma, bracket or other mark between them.
    private bool Joined(Tok a, Tok b)
    {
        if (a.TrailPunct || b.LeadPunct || b.Start - a.End != 1 || _text[a.End] is not (' ' or ' '))
        {
            return false;
        }

        // A full stop after a long word ends the sentence; after an initial, a title or an ending it belongs to the word.
        return _text[a.CoreEnd - 1] != '.' || KindOf(a) is Kind.Initial or Kind.Honorific or Kind.Ending;
    }

    // ---------------------------------------------------------------- the run

    private void Run()
    {
        int lines = _view.Lines.Count;
        for (int l = 0; l < lines; l++)
        {
            OrganizationsWithLegalForm(l);
            CopyrightOrganization(l);
            EmployerAfterJob(l);
        }

        for (int l = 0; l < lines; l++)
        {
            People(l);
        }

        for (int l = 0; l < lines; l++)
        {
            SingleNames(l);
            if (_loose)
            {
                LastFirst(l);
                LowerCaseNames(l);
                CuedSingles(l);
            }
        }

        for (int l = 0; l < lines; l++)
        {
            EmployerAfterPerson(l);
            OrganizationAfterCue(l);
        }

        for (int l = 0; l < lines; l++)
        {
            SignatureCompany(l);
        }

        _hits.Sort((a, b) => a.Start.CompareTo(b.Start));
    }

    private void Add(int start, int end, bool organization, int line)
    {
        for (int i = start; i < end; i++)
        {
            if (_own[i])
            {
                return;
            }
        }

        for (int i = start; i < end; i++)
        {
            _own[i] = true;
        }

        _hits.Add(new NameHit(start, end, organization, line));
        if (!organization && line >= 0 && line < _lastPersonEnd.Length)
        {
            _lastPersonEnd[line] = Math.Max(_lastPersonEnd[line], end);
            TextLine tl = _view.Lines[line];
            _aloneName[line] = _text.AsSpan(tl.Start, start - tl.Start).Trim().IsEmpty && IsOnlySeparators(_text.AsSpan(end, tl.End - end));
        }
    }

    private bool Free(int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (_own[i])
            {
                return false;
            }
        }

        return !_isTaken(start, end);
    }

    // ---------------------------------------------------------------- organizations

    private bool IsLegalToken(Tok t, out bool abbreviation)
    {
        abbreviation = false;
        ReadOnlySpan<char> core = _text.AsSpan(t.CoreStart, t.CoreEnd - t.CoreStart);
        ReadOnlySpan<char> bare = core.TrimEnd('.');
        if (bare.Length == 0 || bare.Length > 14)
        {
            return false;
        }

        string folded = Folded(t);
        if (!NameVocabulary.LegalForms.Contains(folded))
        {
            // "L.L.C." and "S.A." fold to the same letters.
            return false;
        }

        if (NameVocabulary.LegalFormsUpperOnly.Contains(folded) && !NameFolding.IsAllUpper(bare.ToString().Replace(".", string.Empty, StringComparison.Ordinal)))
        {
            return false;
        }

        // "limited" or "company" in lower case is an ordinary word in a sentence; "inc" and "ltée" are not.
        if (char.IsLower(bare[0]) && folded is not ("inc" or "ltd" or "ltee" or "lt\u00e9e" or "llc" or "llp" or "corp" or "gmbh" or "plc" or "pty"))
        {
            return false;
        }

        abbreviation = folded is "inc" or "ltd" or "corp" or "co" or "llc" or "llp" or "lp" or "pty" or "ltee" || bare.Contains('.');
        return true;
    }

    // Two neighbouring words are apart when more than one space lies between them or they sit in side-by-side elements ("<span>Jane Doe</span> <span>Acme Inc</span>").
    private bool Apart(Tok a, Tok b) => b.Start - a.End != 1 || _separated(a.CoreEnd - 1, b.CoreStart);

    private bool IsOrgWord(Tok t)
    {
        char c = _text[t.CoreStart];
        return char.IsUpper(c) || char.IsDigit(c) || c == '&';
    }

    private bool IsConnector(Tok t)
    {
        return NameVocabulary.Connectors.Contains(LowerWord(t));
    }

    private void OrganizationsWithLegalForm(int line)
    {
        List<Tok> toks = TokensOf(line);
        for (int k = 0; k < toks.Count; k++)
        {
            if (!IsLegalToken(toks[k], out bool abbreviation))
            {
                continue;
            }

            // The last of several legal words: "Pty Ltd", "Co. Ltd".
            int last = k;
            while (last + 1 < toks.Count && Joined(toks[last], toks[last + 1]) && IsLegalToken(toks[last + 1], out bool nextAbbreviation))
            {
                last++;
                abbreviation = nextAbbreviation;
            }

            int first = last;
            // Walk left over the words of the name.
            int m = k - 1;
            while (m >= 0)
            {
                Tok t = toks[m];
                Tok after = toks[m + 1];
                bool commaBeforeForm = t.TrailPunct && _text[t.CoreEnd] == ',' && m + 1 == k;
                if ((t.TrailPunct && !commaBeforeForm) || after.LeadPunct || Apart(t, after))
                {
                    break;
                }

                if (IsOrgWord(t) || (IsConnector(t) && m > 0 && IsOrgWord(toks[m - 1]) && !toks[m - 1].TrailPunct))
                {
                    first = m;
                    m--;
                    continue;
                }

                break;
            }

            if (first == last || first >= k)
            {
                k = last;
                continue;
            }

            // Leave out what is not part of a name: a word that points at it, a year, a sign.
            int begin = CutAtConnector(toks, first, k);
            while (begin < k)
            {
                string f = Folded(toks[begin]);
                bool year = f.Length == 4 && f.All(char.IsAsciiDigit) && f[0] is '1' or '2';
                if ((NameVocabulary.Cues.Contains(f) && f != "the") || IsChromeWord(f) || year || f.Length == 0)
                {
                    begin++;
                    continue;
                }

                break;
            }

            if (begin >= k)
            {
                k = last;
                continue;
            }

            int end = OrgEnd(toks[last]);
            int start = toks[begin].CoreStart;
            string candidate = _text.Substring(start, end - start);
            if (!HasBase(toks, begin, k) || FormIsRegionCode(toks, begin, last) || !CoreAcceptsLegalForm(candidate) || !Free(start, end))
            {
                k = last;
                continue;
            }

            end = ExtendOverTradingName(toks, last, end);
            Add(start, end, true, line);
            k = last;
        }
    }

    // "Ms. Dina Rung of BB&T Funds Inc": when two or more plain words and a connector stand in front of the name, they are somebody
    // who is "of" the company; the name begins after the connector. "Bank of America Corp" and "Law Offices of Smith LLP" keep theirs.
    private int CutAtConnector(List<Tok> toks, int first, int formStart)
    {
        for (int i = formStart - 1; i > first; i--)
        {
            string c = LowerWord(toks[i]);
            if (c is not ("of" or "de" or "du" or "von" or "van" or "at" or "from") || i - first < 2)
            {
                continue;
            }

            bool industryLeft = false;
            for (int x = first; x < i; x++)
            {
                industryLeft |= IsIndustryWord(Folded(toks[x]), 3) || KindOf(toks[x]) == Kind.Honorific;
            }

            if (!industryLeft || KindOf(toks[first]) == Kind.Honorific)
            {
                return i + 1;
            }
        }

        return first;
    }

    // "Acme Inc dba Rocket Pizza", "Acme LLC c/o Jane Smith": the trading name or care-of name is part of the name as written.
    private int ExtendOverTradingName(List<Tok> toks, int last, int end)
    {
        int i = last + 1;
        if (i >= toks.Count || toks[last].TrailPunct && _text[toks[last].CoreEnd] != ',')
        {
            return end;
        }

        string marker = LowerWord(toks[i]);
        bool two = false;
        if (marker is "d/b/a" or "dba" or "t/a" or "c/o" or "a/k/a" or "aka" or "fka" or "f/k/a")
        {
        }
        else if (i + 1 < toks.Count && ((marker == "doing" && Word(toks[i + 1]).Equals("business", StringComparison.OrdinalIgnoreCase)) || (marker == "trading" && Word(toks[i + 1]).Equals("as", StringComparison.OrdinalIgnoreCase)) || (marker == "care" && Word(toks[i + 1]).Equals("of", StringComparison.OrdinalIgnoreCase))))
        {
            two = true;
        }
        else
        {
            return end;
        }

        int j = i + (two ? 2 : 1) + (two && i + 2 < toks.Count && Word(toks[i + 2]).Equals("as", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
        int lastWord = -1;
        for (int n = j; n < toks.Count && n - j < 8; n++)
        {
            Tok t = toks[n];
            if (!(IsOrgWord(t) || (IsConnector(t) && n > j)) || (n > j && (t.LeadPunct || Apart(toks[n - 1], t))))
            {
                break;
            }

            lastWord = n;
            if (t.TrailPunct || (_text[t.CoreEnd - 1] == '.' && !IsLegalToken(t, out _)))
            {
                break;
            }
        }

        if (lastWord < j)
        {
            return end;
        }

        return OrgEnd(toks[lastWord]);
    }

    // "Edmonton AB" and "Charlottetown PE" end in a province code that is also a legal form (Swedish AB, ...): a place, not a company.
    private bool FormIsRegionCode(List<Tok> toks, int begin, int last)
    {
        string form = Word(toks[last]);
        if (form.Length != 2 || !NameVocabulary.RegionCodes.Contains(form))
        {
            return false;
        }

        for (int i = begin; i < last; i++)
        {
            if (NameVocabulary.Places.Contains(Folded(toks[i])) || toks[i].TrailPunct)
            {
                return true;
            }
        }

        return last - begin < 2;
    }

    // Where an organization name ends. The dots after a legal form that is written with one ("Inc.", "L.P.") belong to it, unless they
    // end a sentence ("Acme Inc. All rights reserved."); one dot of a doubled "Inc.." belongs to the name, the other ends the sentence.
    private int OrgEnd(Tok last)
    {
        int e = last.CoreEnd;
        int dots = 0;
        while (e > last.CoreStart && _text[e - 1] == '.')
        {
            dots++;
            e--;
        }

        if (dots == 0 || !(IsLegalToken(last, out bool abbreviation) && abbreviation))
        {
            return e;
        }

        if (dots >= 2 || _text.AsSpan(last.CoreStart, e - last.CoreStart).Contains('.'))
        {
            return e + 1;
        }

        int n = last.CoreEnd;
        while (n < _text.Length && _text[n] is ' ' or '\t' or '\u00A0')
        {
            n++;
        }

        bool newSentence = n < _text.Length && _text[n] != '\n' && char.IsUpper(_text[n]);
        return newSentence ? e : e + 1;
    }

    private bool HasBase(List<Tok> toks, int begin, int formStart)
    {
        for (int i = begin; i < formStart; i++)
        {
            string f = Folded(toks[i]);
            if (f.Length > 0 && !NameVocabulary.Connectors.Contains(f))
            {
                return true;
            }
        }

        return false;
    }

    // Words of web page chrome that begin a line next to a name ("Welcome", "Visit", "Copyright").
    private static bool IsChromeWord(string folded) =>
        folded is "copyright" or "welcome" or "visit" or "about" or "contact" or "call" or "email" or "powered" or "designed" or "website" or "site"
            or "privacy" or "terms" or "thanks" or "thank" or "hello" or "hi" or "dear" or "attn" or "from" or "reserved" or "rights" or "all" or "c";

    private static bool CoreAcceptsLegalForm(string candidate)
    {
        try
        {
            return CompanyName.TryParse(candidate, out CompanyName c) && c.LegalForm is not null && !c.Base.IsEmpty;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private void CopyrightOrganization(int line)
    {
        TextLine l = _view.Lines[line];
        int sign = IndexOfCopyright(l.Start, l.End, out int signEnd);
        if (sign < 0)
        {
            return;
        }

        List<Tok> toks = TokensOf(line);
        int i = 0;
        while (i < toks.Count && toks[i].CoreEnd <= signEnd)
        {
            i++;
        }

        // Years, ranges and "by" between the sign and the name.
        while (i < toks.Count)
        {
            string f = Folded(toks[i]);
            string w = Word(toks[i]);
            bool number = IsYearOrRange(w);
            if (number || f is "by" or "copyright" or "c" || w is "\u00A9" or "(c)" or "(C)")
            {
                i++;
                continue;
            }

            break;
        }

        int firstIdx = i;
        int lastIdx = -1;
        while (i < toks.Count && i - firstIdx < 8)
        {
            Tok t = toks[i];
            string f = Folded(t);
            if (f is "all" or "powered" or "designed" or "developed" or "built" or "website" or "site" or "terms" or "privacy" or "rights" or "reserved" or "by" || f.Length == 0)
            {
                break;
            }

            if (!(IsOrgWord(t) || (IsConnector(t) && i > firstIdx)))
            {
                break;
            }

            if (i > firstIdx && t.LeadPunct)
            {
                break;
            }

            lastIdx = i;
            // A comma ends the name, except in front of a legal form ("Acme Widgets, Inc.").
            if (t.TrailPunct)
            {
                if (_text[t.CoreEnd] == ',' && i + 1 < toks.Count && IsLegalToken(toks[i + 1], out _))
                {
                    i++;
                    continue;
                }

                break;
            }

            // A full stop ends the sentence, unless the word is a legal form or an abbreviation of one.
            if (_text[t.CoreEnd - 1] == '.' && !IsLegalToken(t, out _) && Folded(t).Length > 2)
            {
                break;
            }

            i++;
        }

        if (lastIdx < firstIdx || firstIdx >= toks.Count)
        {
            return;
        }

        string firstFolded = Folded(toks[firstIdx]);
        if (NameVocabulary.Stops.Contains(firstFolded) || IsChromeWord(firstFolded) || NameVocabulary.Cues.Contains(firstFolded))
        {
            return;
        }

        int start = toks[firstIdx].CoreStart;
        int end = OrgEnd(toks[lastIdx]);

        if (end - start < 2 || !Free(start, end))
        {
            return;
        }

        Add(start, end, true, line);
    }

    // 2024, 2019-2024, 2019\u20132024, 2019,2024: the years of a copyright line, and nothing else with digits in it.
    private static bool IsYearOrRange(string w)
    {
        string t = w.Trim(',', '.');
        if (t.Length == 0)
        {
            return false;
        }

        foreach (string part in t.Split(['-', '\u2013', ','], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!(part.Length is 2 or 4 && part.All(char.IsAsciiDigit)))
            {
                return false;
            }
        }

        return true;
    }

    private int IndexOfCopyright(int start, int end, out int signEnd)
    {
        signEnd = -1;
        ReadOnlySpan<char> s = _text.AsSpan(start, end - start);
        int at = s.IndexOf('\u00A9');
        if (at >= 0)
        {
            signEnd = start + at + 1;
            return start + at;
        }

        at = s.IndexOf("copyright", StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
        {
            signEnd = start + at + "copyright".Length;
            // "Copyright (c)" and "Copyright ©": the sign after the word is skipped as a token.
            return start + at;
        }

        at = s.IndexOf("(c)", StringComparison.OrdinalIgnoreCase);
        if (at >= 0)
        {
            signEnd = start + at + 3;
            return start + at;
        }

        return -1;
    }

    // "Sales Manager at Acme Widgets", "works for Acme Widgets": the employer a person is said to have.
    private void EmployerAfterJob(int line)
    {
        List<Tok> toks = TokensOf(line);
        for (int k = 1; k + 1 < toks.Count; k++)
        {
            string f = Folded(toks[k]);
            if (f is not ("at" or "for" or "with" or "by") && !(toks[k].CoreEnd - toks[k].CoreStart == 1 && _text[toks[k].CoreStart] == '@'))
            {
                continue;
            }

            Tok prev = toks[k - 1];
            bool job = NameVocabulary.Roles.Contains(Folded(prev)) && !prev.TrailPunct;
            bool works = k >= 2 && Folded(toks[k - 2]) is "works" or "work" or "worked" or "working" or "employed" or "employee" or "no";
            works |= Folded(prev) is "now" or "longer" or "joined" or "left";
            if (!(job || works) || toks[k].TrailPunct || toks[k + 1].LeadPunct)
            {
                continue;
            }

            int first = k + 1;
            int last = -1;
            for (int i = first; i < toks.Count && i - first < 7; i++)
            {
                Tok t = toks[i];
                if (!(IsOrgWord(t) || (IsConnector(t) && i > first)) || (i > first && t.LeadPunct))
                {
                    break;
                }

                last = i;
                if (t.TrailPunct)
                {
                    if (_text[t.CoreEnd] == ',' && i + 1 < toks.Count && IsLegalToken(toks[i + 1], out _))
                    {
                        continue;
                    }

                    break;
                }

                if (_text[t.CoreEnd - 1] == '.' && !IsLegalToken(t, out _))
                {
                    break;
                }
            }

            if (last < first)
            {
                continue;
            }

            string firstFolded = Folded(toks[first]);
            if (NameVocabulary.Stops.Contains(firstFolded) || NameVocabulary.Cues.Contains(firstFolded) || IsChromeWord(firstFolded))
            {
                continue;
            }

            int start = toks[first].CoreStart;
            int end = OrgEnd(toks[last]);

            if (end - start >= 2 && Free(start, end))
            {
                Add(start, end, true, line);
            }

            return;
        }
    }

    // ---------------------------------------------------------------- people

    private enum Kind { None, Word, Initial, Particle, Honorific, Ending }

    private Kind KindOf(Tok t) => t.KindCache ??= ComputeKind(t);

    private Kind ComputeKind(Tok t)
    {
        ReadOnlySpan<char> w = _text.AsSpan(t.CoreStart, t.CoreEnd - t.CoreStart);
        ReadOnlySpan<char> bare = w.TrimEnd('.');
        if (bare.Length == 0)
        {
            return Kind.None;
        }

        string folded = Folded(t);
        if (char.IsLower(bare[0]))
        {
            return folded.Length > 0 && NameVocabulary.Particles.Contains(folded) && !t.TrailPunct ? Kind.Particle : Kind.None;
        }

        if (!char.IsUpper(bare[0]))
        {
            return Kind.None;
        }

        foreach (char c in bare)
        {
            if (!(char.IsLetter(c) || c is '\'' or '\u2019' or '-' or '.'))
            {
                return Kind.None;
            }
        }

        if (NameVocabulary.Honorifics.Contains(folded) && (w.Length > bare.Length || folded.Length > 1 && (folded is not ("sr" or "rep" or "m" or "don" or "gen" or "col" or "lt" or "hon" or "fr"))))
        {
            return Kind.Honorific;
        }

        if (NameVocabulary.NameEndings.Contains(folded) && (w.Length > bare.Length || bare.Contains('.') || folded is "jr" or "sr" or "ii" or "iii" or "iv" or "md" or "phd" or "esq" or "cpa"))
        {
            return Kind.Ending;
        }

        if (folded.Length == 1)
        {
            // "K. Smith"; or a middle initial written without its dot ("Cedric K Gouard"), which "A" and "I" are too likely to be words for.
            return w.Length > bare.Length || (bare.Length == 1 && bare[0] is not ('A' or 'I')) ? Kind.Initial : Kind.None;
        }

        if (bare.Contains('.') && folded.Length <= 3 && NameFolding.IsAllUpper(bare.ToString().Replace(".", string.Empty, StringComparison.Ordinal)))
        {
            return Kind.Initial;
        }

        // A dot inside a word is not a name ("St.Louis" is two). A trailing dot after a long word ends a sentence.
        if (bare.Contains('.'))
        {
            return Kind.None;
        }

        return folded.Length >= 2 ? Kind.Word : Kind.None;
    }

    private void People(int line)
    {
        List<Tok> toks = TokensOf(line);
        int i = 0;
        while (i < toks.Count)
        {
            Kind k = KindOf(toks[i]);
            if (k is not (Kind.Word or Kind.Initial or Kind.Honorific))
            {
                i++;
                continue;
            }

            int j = i;
            while (j + 1 < toks.Count && Joined(toks[j], toks[j + 1]) && KindOf(toks[j + 1]) != Kind.None)
            {
                j++;
            }

            ReadRun(line, toks, i, j);
            i = j + 1;
        }
    }

    private void ReadRun(int line, List<Tok> toks, int from, int to)
    {
        // A word that points at a name, in front of it, is not part of it.
        while (from <= to && (NameVocabulary.Cues.Contains(Folded(toks[from])) && KindOf(toks[from]) != Kind.Honorific))
        {
            from++;
        }

        // "CUSTOMER SHANE WILFLEY": a word that says what the person is to the writer is a label, not a word of the name.
        while (to - from >= 2 && NameLists.FrontLabels.Contains(Folded(toks[from])) && KindOf(toks[from]) != Kind.Honorific)
        {
            from++;
        }

        // Endings at the back are part of the name when it is written with them ("Jr.", "Esq", ", CPA"); they are not words of it.
        while (to >= from && KindOf(toks[to]) == Kind.Ending)
        {
            to--;
        }

        int endingTo = to;

        int honorific = -1;
        if (from <= to && KindOf(toks[from]) == Kind.Honorific)
        {
            honorific = from;
            from++;
        }

        bool peeledJob = false;
        while (to - from + 1 > 2 && (NameVocabulary.Roles.Contains(Folded(toks[to])) || NameVocabulary.RoleLeads.Contains(Folded(toks[to]))))
        {
            to--;
            peeledJob = true;
        }

        // "SPOKE WITH DWAINE PIESKE RE: QUOTE", "ELIAN MESSER CALLED": a reference word or a verb at the back of three or more words.
        while (to - from >= 2 && (NameLists.TrailWords.Contains(Folded(toks[to])) || NameLists.PersonVerbs.Contains(LowerWord(toks[to]))))
        {
            to--;
        }

        // "Yesterday Maria Lopez": a sentence's first word in front of a given name and a family name is not part of the name.
        if (_loose && to - from == 2 && honorific < 0 && KindOf(toks[from]) == Kind.Word && (NameLists.Openers.Contains(Folded(toks[from])) || NameLists.TimeWords.Contains(Folded(toks[from])))
            && KindOf(toks[from + 1]) == Kind.Word && StrongGivenName(toks[from + 1]) && KindOf(toks[to]) == Kind.Word && KnownFamily(toks[to]))
        {
            from++;
        }

        int words = to - from + 1;
        bool industry = false;
        for (int x = from; x <= to; x++)
        {
            industry |= IsIndustryWord(Folded(toks[x]), to - from + 1);
        }

        if (words < 1 || words > (industry ? 8 : MaxRun) || (words < 2 && honorific < 0))
        {
            return;
        }

        // A state or province code at the end: the last line of an address ("MONCTON NB").
        string lastWord = Word(toks[to]);
        if (lastWord.Length == 2 && NameVocabulary.RegionCodes.Contains(lastWord))
        {
            return;
        }

        if (KindOf(toks[from]) != Kind.Word || KindOf(toks[to]) != Kind.Word)
        {
            // "J. Smith" has an initial first; the last word must be a word.
            if (!(KindOf(toks[from]) == Kind.Initial && KindOf(toks[to]) == Kind.Word && words >= 2))
            {
                return;
            }
        }

        for (int x = from; x <= to; x++)
        {
            string f = Folded(toks[x]);
            if ((NameVocabulary.Stops.Contains(f) && !IsIndustryWord(f, words)) || NameVocabulary.Places.Contains(f) || (x > from && NameVocabulary.Cues.Contains(f) && KindOf(toks[x]) == Kind.Word && f is "and" or "or" or "the" or "for" or "to" or "with" or "by"))
            {
                return;
            }
        }

        int start = honorific >= 0 ? toks[honorific].CoreStart : toks[from].CoreStart;
        int end = toks[to].CoreEnd;
        if (_text[end - 1] == '.' && KindOf(toks[to]) != Kind.Initial)
        {
            end--;
        }

        // The endings that follow, after a space or a comma.
        int e = endingTo;
        if (e == to)
        {
            while (e + 1 < toks.Count && KindOf(toks[e + 1]) == Kind.Ending && !toks[e + 1].LeadPunct && (!toks[e].TrailPunct || _text[toks[e].CoreEnd] == ','))
            {
                e++;
            }

            if (e > to)
            {
                end = toks[e].CoreEnd;
            }
        }

        if (!Free(start, end))
        {
            return;
        }

        for (int x = from; x <= to; x++)
        {
            if (KindOf(toks[x]) is not (Kind.Word or Kind.Initial or Kind.Particle))
            {
                return;
            }
        }

        if (NameVocabulary.Roles.Contains(Folded(toks[to])) || (peeledJob && !StartsJobPhrase(_text.AsSpan(toks[to].CoreEnd, _view.Lines[line].End - toks[to].CoreEnd), false)))
        {
            return;
        }

        string why = "job-words";
        bool strong = peeledJob || Evidence(line, toks, from, to, start, end, honorific, words, out why);
        if (!strong)
        {
            return;
        }

        string candidate = _text.Substring(start, end - start);
        NameClass cls;
        try
        {
            cls = NameParsing.Classify(candidate);
        }
        catch (ArgumentException)
        {
            return;
        }

        if (cls == NameClass.Multiple)
        {
            return;
        }

        if (industry)
        {
            cls = NameClass.Company;
        }

        Trace?.Invoke($"{(cls == NameClass.Company ? "org" : "person")} [{candidate}] {why}");
        Add(start, end, cls == NameClass.Company, line);
    }

    // True when something next to the run says it is a person's name; why names the evidence (for the evaluation tools).
    private bool Evidence(int line, List<Tok> toks, int from, int to, int start, int end, int honorificAt, int words, out string why)
    {
        TextLine l = _view.Lines[line];
        why = string.Empty;

        // An email address or a phone number that introduces or follows the name.
        foreach ((FieldKind kind, int fs, int fe) in _linked[line] ?? NoFields)
        {
            if ((fs >= end && GapIsLink(end, fs, kind == FieldKind.Address)) || (fe <= start && GapIsLink(fe, start, kind == FieldKind.Address)))
            {
                why = "next-to-" + kind;
                return true;
            }
        }

        ReadOnlySpan<char> after = _text.AsSpan(end, l.End - end);
        ReadOnlySpan<char> before = _text.AsSpan(l.Start, start - l.Start);

        // A job title next to the name: "Jane Doe, Sales Manager", "Sales Manager: Jane Doe", "Director Jane Doe".
        bool honorific = honorificAt >= 0;
        if ((words >= 2 || honorific) && (StartsJobPhrase(after, true) || EndsWithJob(before)))
        {
            why = "job-title";
            return true;
        }

        // A byline or the signature under a quoted testimonial names the writer or the customer quoted, who is not a contact.
        bool attributed = Attribution(before) || (before.Trim().IsEmpty && line > 0 && SameSection(line - 1, line) && EndsWithQuote(line - 1));

        // "Dr. Patel", "Mrs. Alvarez": a title in front of a name says it is a person's.
        if (honorific && !attributed && StrongHonorific(toks, honorificAt))
        {
            why = "honorific";
            return true;
        }

        // "Maria Lopez", "John R. Smith": a given name that people commonly have, then the words of a family name.
        if (_loose && words is 2 or 3 && GivenLead(toks, from, to) && !attributed)
        {
            why = "given-name";
            return true;
        }

        // "Nguyen Tran" next to nguyen.tran@example.com: the address spells the name.
        if (_loose && words >= 2 && EmailLocalMatches(line, toks, from, to))
        {
            why = "email-local-part";
            return true;
        }

        // "From: Jane Doe", "Attendees: ...", "Ref: ...": a label for people, a colon, the name.
        if (_loose && HeaderLabelBefore(before))
        {
            why = "header-label";
            return true;
        }

        // "talked to Maria Lopez", "approved by Maria Lopez": a preposition that takes people is not enough by itself; the name must also be one.
        if (_loose && words is 2 or 3 && WeakCueBefore(before) && !attributed && (LikelyGiven(toks[from]) || KnownFamily(toks[to])))
        {
            why = "cue-and-list";
            return true;
        }

        // "Maria Lopez called": what a sentence says a person did. Words about speaking and writing take a name as they stand; the rest need the lists.
        if (_loose && words is 2 or 3 && NextWord(after) is { Length: > 0 } verb && (NameLists.PersonVerbs.Contains(verb) || NameLists.SpeechVerbs.Contains(verb)))
        {
            if (NameLists.SpeechVerbs.Contains(verb) || LikelyGiven(toks[from]) || KnownFamily(toks[to]))
            {
                why = "verb-after";
                return true;
            }
        }

        // "Maria Lopez, Dan Smith and Pat Doe": the name beside a name already taken, with only list marks between.
        if (_loose && words is 2 or 3 && ListNeighbor(line, start, end, before, after))
        {
            why = "list-neighbor";
            return true;
        }

        if (CuePhraseBefore(before))
        {
            why = "cue";
            return true;
        }

        // The name stands on a line of its own.
        bool alone = before.Trim().Length == 0 && (IsOnlySeparators(after) || AllEndings(after));
        if (!alone || words < 2)
        {
            return false;
        }

        bool next = line + 1 < _view.Lines.Count && SameSection(line, line + 1);
        bool previous = line > 0 && SameSection(line - 1, line);
        if (next && IsJobLine(line + 1))
        {
            why = "job-line-below";
        }
        else if (previous && IsClosing(line - 1))
        {
            why = "closing-above";
        }
        else if (previous && IsJobLine(line - 1) && ContactAhead(line, 2))
        {
            why = "job-line-above";
        }
        else if (next && (_kinds[line + 1] & (KindEmail | KindPhone)) != 0)
        {
            why = "contact-below";
        }
        else if (ContactAhead(line, 3) && KnownGivenName(toks[from]))
        {
            why = "given-name-and-contact";
        }

        return why.Length > 0;
    }

    private static readonly FrozenSet<string> StrongHonorifics = new[] { "mr", "mrs", "ms", "mx", "miss", "dr", "prof", "professor", "rev", "mme", "mlle", "herr", "frau", "sra", "srta" }
        .ToFrozenSet(StringComparer.Ordinal);

    // A title counts as evidence unless it is "Dr" with no dot after a capitalized word ("Oak Dr Suite"), which is a street.
    private bool StrongHonorific(List<Tok> toks, int at)
    {
        string f = Folded(toks[at]);
        if (!StrongHonorifics.Contains(f))
        {
            return false;
        }

        bool dot = _text[toks[at].CoreEnd - 1] == '.';
        if (f == "dr" && !dot && at > 0 && !toks[at - 1].TrailPunct && KindOf(toks[at - 1]) == Kind.Word)
        {
            return false;
        }

        return true;
    }

    private bool LikelyGiven(Tok t)
    {
        string f = Folded(t);
        return f.Length > 1 && (NameLexicon.IsGivenName(f) || NameLists.Given.Contains(f));
    }

    private bool StrongGivenName(Tok t)
    {
        string f = Folded(t);
        return f.Length > 1 && (NameLexicon.IsGivenName(f) || NameLists.StrongGiven.Contains(f));
    }

    private bool KnownFamily(Tok t)
    {
        string f = Folded(t);
        return f.Length > 2 && NameLists.Family.Contains(f);
    }

    // Two or three words, the first a given name: the words after it must not be words of a business or a place.
    private bool GivenLead(List<Tok> toks, int from, int to)
    {
        Tok first = toks[from];
        if (KindOf(first) != Kind.Word || !LikelyGiven(first))
        {
            return false;
        }

        // A given name that is also an everyday word ("Rose Hill", "Will Call") says nothing on its own.
        bool ambiguous = NameLists.WordGiven.Contains(Folded(first));
        if (ambiguous)
        {
            return false;
        }

        for (int x = from + 1; x <= to; x++)
        {
            string f = Folded(toks[x]);
            if (NameLexicon.IsCompanyWord(f) || NameVocabulary.Industry.Contains(f))
            {
                return false;
            }

        }

        // The family name: a family name people commonly have, a given name (Mary Anne) or a word that does not read as ordinary text.
        Tok last = toks[to];
        return KnownFamily(last) || LikelyGiven(last) || StrongGivenName(first);
    }

    // An email address in this line or the next or previous line of the same section whose local part is made of the name's words.
    private bool EmailLocalMatches(int line, List<Tok> toks, int from, int to)
    {
        string first = Folded(toks[from]);
        string last = Folded(toks[to]);
        for (int d = -1; d <= 1; d++)
        {
            int l = line + d;
            if (l < 0 || l >= _linked.Length || (d != 0 && !SameSection(line, l)) || _linked[l] is not { } fields)
            {
                continue;
            }

            foreach ((FieldKind kind, int fs, int fe) in fields)
            {
                if (kind != FieldKind.Email)
                {
                    continue;
                }

                ReadOnlySpan<char> address = _text.AsSpan(fs, fe - fs);
                int at = address.IndexOf('@');
                if (at <= 0)
                {
                    continue;
                }

                if (LocalPartSpells(address[..at], first, last))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool LocalPartSpells(ReadOnlySpan<char> local, string first, string last)
    {
        int i = 0;
        while (i < local.Length)
        {
            while (i < local.Length && !char.IsLetter(local[i]))
            {
                i++;
            }

            int s = i;
            while (i < local.Length && char.IsLetter(local[i]))
            {
                i++;
            }

            if (i == s)
            {
                break;
            }

            string piece = NameFolding.LettersAndDigits(local[s..i]);
            if ((last.Length >= 3 && (piece == last || piece == first + last || piece == last + first)) || (first.Length >= 3 && piece == first))
            {
                return true;
            }

            // "mlopez", "lopezm": the first letter of the first name with the family name.
            if (last.Length >= 3 && first.Length >= 1 && piece.Length == last.Length + 1 && ((piece[0] == first[0] && piece.EndsWith(last, StringComparison.Ordinal)) || (piece[^1] == first[0] && piece.StartsWith(last, StringComparison.Ordinal))))
            {
                return true;
            }
        }

        return false;
    }

    private bool EndsWithQuote(int line)
    {
        TextLine l = _view.Lines[line];
        ReadOnlySpan<char> t = _text.AsSpan(l.Start, l.End - l.Start).TrimEnd();
        return t.Length > 0 && t[^1] is '”' or '"' or '»';
    }

    // A byline or an attribution: "By Dana Reyes | May 3", "— Dana Reyes, Toronto". These name the writer or the quoted customer, who is not a contact.
    private static bool Attribution(ReadOnlySpan<char> before)
    {
        ReadOnlySpan<char> t = before.Trim();
        if (t.Length == 0)
        {
            return false;
        }

        if (t.Length <= 2 && t[0] is '\u2014' or '\u2013' or '-' or '~' or '*' or '\u2022')
        {
            return true;
        }

        return t.Equals("by", StringComparison.OrdinalIgnoreCase) || t.Equals("par", StringComparison.OrdinalIgnoreCase) || t.EndsWith("posted by", StringComparison.OrdinalIgnoreCase)
            || t.EndsWith("written by", StringComparison.OrdinalIgnoreCase) || t.EndsWith("reviewed by", StringComparison.OrdinalIgnoreCase) || t.EndsWith("photo by", StringComparison.OrdinalIgnoreCase);
    }

    // "From:", "Attendees:", "Ref:": the last word before a colon at the end of what stands in front of the name.
    private static bool HeaderLabelBefore(ReadOnlySpan<char> before)
    {
        ReadOnlySpan<char> t = before.TrimEnd();
        if (t.Length < 2 || t[^1] != ':')
        {
            return false;
        }

        t = t[..^1].TrimEnd();
        int s = t.Length;
        while (s > 0 && (char.IsLetter(t[s - 1]) || t[s - 1] == '-'))
        {
            s--;
        }

        return s < t.Length && NameLists.HeaderLabels.Contains(NameFolding.LettersAndDigits(t[s..]));
    }

    // A preposition or word that takes a person as its object ("to", "with", "by", "for", "from", "per", "via"), as the last word in front.
    private static bool WeakCueBefore(ReadOnlySpan<char> before)
    {
        ReadOnlySpan<char> t = before.TrimEnd(" \t\u00A0:,-\u2013");
        int s = t.Length;
        while (s > 0 && char.IsLetter(t[s - 1]))
        {
            s--;
        }

        if (s == t.Length || (s > 0 && t[s - 1] == '\''))
        {
            return false;
        }

        return NameLists.WeakCues.Contains(t[s..].ToString().ToLowerInvariant());
    }

    // The word that follows, lower-cased; empty when something other than white space and a letter follows.
    private static string NextWord(ReadOnlySpan<char> after)
    {
        int i = 0;
        while (i < after.Length && after[i] is ' ' or '\t' or '\u00A0')
        {
            i++;
        }

        int s = i;
        while (i < after.Length && char.IsLetter(after[i]))
        {
            i++;
        }

        return i > s && (i == after.Length || !(char.IsLetterOrDigit(after[i]) || after[i] is '@' or '.' && i + 1 < after.Length && char.IsLetterOrDigit(after[i + 1]))) ? after[s..i].ToString().ToLowerInvariant() : string.Empty;
    }

    // The line already holds a name that ends before this run, and only list marks ("," ";" "&" "/" "and") lie between the two; or the
    // line above, in the same section, is a name on a line of its own and so is this one.
    private bool ListNeighbor(int line, int start, int end, ReadOnlySpan<char> before, ReadOnlySpan<char> after)
    {
        int e = _lastPersonEnd[line];
        if (e >= 0 && e <= start)
        {
            ReadOnlySpan<char> gap = _text.AsSpan(e, start - e);
            if (ListGap(gap))
            {
                return true;
            }
        }

        return line > 0 && _aloneName[line - 1] && SameSection(line - 1, line) && before.Trim().IsEmpty && IsOnlySeparators(after) && end > start;
    }

    private static bool ListGap(ReadOnlySpan<char> gap)
    {
        bool mark = false;
        int i = 0;
        while (i < gap.Length)
        {
            char c = gap[i];
            if (c is ',' or ';' or '&' or '/' or '\u00B7' or '|')
            {
                mark = true;
                i++;
            }
            else if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (gap[i..].StartsWith("and", StringComparison.OrdinalIgnoreCase) && (i + 3 == gap.Length || !char.IsLetter(gap[i + 3])))
            {
                mark = true;
                i += 3;
            }
            else
            {
                return false;
            }
        }

        return mark;
    }

    private static bool IsOnlySeparators(ReadOnlySpan<char> s)
    {
        foreach (char c in s)
        {
            if (!(char.IsWhiteSpace(c) || c is ',' or '.' or ':' or ';' or '-' or '\u2013' or '\u2014' or '|' or ')' or '('))
            {
                return false;
            }
        }

        return true;
    }

    // Words that are also family names ("Matt Glass", "Jane Law") make a company only inside a longer name.
    private static readonly string[] SurnameLike = ["law", "glass", "title", "sons", "college", "club", "spa", "inn", "church", "kitchen", "signs", "bros", "brothers", "capital", "moving", "learning", "lodge", "gallery", "farms", "gardens", "coffee", "cafe", "studio", "school", "group", "company", "limited"];

    private static bool IsIndustryWord(string folded, int words)
    {
        bool industry = NameVocabulary.Industry.Contains(folded) || (NameLexicon.IsCompanyWord(folded) && !NameLexicon.IsSurnameCompanyWord(folded));
        return industry && (words >= 3 || Array.IndexOf(SurnameLike, folded) < 0);
    }

    private bool KnownGivenName(Tok t) => LikelyGiven(t);

    private static bool AllEndings(ReadOnlySpan<char> after)
    {
        // The words between separators; each must be a name ending.
        int i = 0;
        while (i < after.Length)
        {
            while (i < after.Length && after[i] is ' ' or ',' or '.' or '(' or ')' or ';' or '-' or '\u2013' or '\u2014' or '|')
            {
                i++;
            }

            int s = i;
            while (i < after.Length && !(after[i] is ' ' or ',' or '.' or '(' or ')' or ';' or '-' or '\u2013' or '\u2014' or '|'))
            {
                i++;
            }

            if (i > s)
            {
                string f = NameFolding.LettersAndDigits(after[s..i]);
                if (f.Length > 0 && !NameVocabulary.NameEndings.Contains(f))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool SameSection(int a, int b)
    {
        TextLine x = _view.Lines[a];
        TextLine y = _view.Lines[b];
        return x.Container == y.Container && x.Heading == y.Heading && x.Landmark == y.Landmark;
    }

    // An email address, a phone number or an address within the next lines of the same section.
    private bool ContactAhead(int line, int lines)
    {
        for (int d = 0; d <= lines && line + d < _view.Lines.Count; d++)
        {
            if (d > 0 && !SameSection(line, line + d))
            {
                return false;
            }

            if ((_kinds[line + d] & (KindEmail | KindPhone | KindAddress)) != 0)
            {
                return true;
            }
        }

        return false;
    }

    // Words that may stand between a name and the address or number it goes with: "Jane Doe - jane@x.com", "call Jane on 555 0100".
    private static readonly string[] LinkWords =
    [
        "at", "on", "is", "email", "e-mail", "mail", "tel", "telephone", "phone", "call", "cell", "mobile", "direct", "office", "fax", "reach", "reached",
        "via", "contact", "number", "no", "ph", "t", "e", "m", "p", "c", "w", "h", "o", "my", "our", "his", "her", "line", "ext", "by", "can", "be", "or",
        "text", "work", "home", "main", "toll", "free", "address", "addr", "at:", "tel.", "ph.", "me", "us", "directly", "emailed", "called",
    ];

    // Words between a name and an address: "Jane Doe moved to 1 Main St", "Jane Doe, now at 1 Main St".
    private static readonly string[] AddressLinkWords =
    [
        "moved", "to", "has", "relocated", "now", "at", "is", "lives", "live", "located", "based", "a", "d\u00e9m\u00e9nag\u00e9", "d\u00e9m\u00e9nag\u00e9e", "au", "\u00e0", "vit", "habite",
        "address", "addr", "office", "home", "mail", "c/o", "co",
    ];

    private bool GapIsLink(int from, int to, bool addressField)
    {
        if (to < from || to - from > 48)
        {
            return false;
        }

        ReadOnlySpan<char> gap = _text.AsSpan(from, to - from);
        int i = 0;
        int words = 0;
        while (i < gap.Length)
        {
            char c = gap[i];
            if (char.IsLetter(c))
            {
                int s = i;
                while (i < gap.Length && (char.IsLetter(gap[i]) || gap[i] is '-' or '.' or '\''))
                {
                    i++;
                }

                string w = gap[s..i].ToString().ToLowerInvariant().TrimEnd('.');
                if (Array.IndexOf(addressField ? AddressLinkWords : LinkWords, w) < 0)
                {
                    return false;
                }

                words++;
                continue;
            }

            if (char.IsDigit(c))
            {
                return false;
            }

            i++;
        }

        return words <= 4;
    }

    private static bool CuePhraseBefore(ReadOnlySpan<char> before)
    {
        ReadOnlySpan<char> t = before.TrimEnd(" \t\u00A0:,-\u2013");
        if (t.Length == 0)
        {
            return false;
        }

        foreach (string cue in CuePhrases)
        {
            if (t.EndsWith(cue, StringComparison.OrdinalIgnoreCase) && (t.Length == cue.Length || !char.IsLetter(t[t.Length - cue.Length - 1])))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly string[] CuePhrases =
    [
        "contact", "contact is", "contact person is", "ask for", "asked for", "asking for", "attn", "attention", "call", "please call", "speak to",
        "speak with", "spoke to", "spoke with", "speaking with", "talk to", "talk with", "talked to", "talked with", "reach out to", "meet", "met with",
        "contacted", "emailed", "email", "e-mail", "my name is", "manager is", "owner is", "agent is", "your contact is", "your agent is",
        "your representative is", "reps name is", "representative is", "you will be working with", "direct line for", "text",
        "met", "meeting with", "meet with", "referred by", "referral from", "thanks to", "thank you", "thanks", "thx", "per", "courtesy of", "assigned to",
        "handled by", "approved by", "signed by", "sent by", "forwarded to", "forward to", "cc", "bcc", "attendees", "participants", "present", "guests",
        "visit with", "chat with", "chatted with", "heard from", "hear from", "left a message for", "message for",
        "rappeler", "appeler", "contacter", "contactez", "demandez", "demander", "joindre", "parler \u00e0", "parlez \u00e0", "contacto", "llamar a", "hablar con", "preguntar por",
    ];

    // ---------------------------------------------------------------- jobs

    // After a name: a separator, then words that begin a job title.
    private static bool StartsJobPhrase(ReadOnlySpan<char> after, bool needSeparator)
    {
        int i = 0;
        bool separated = false;
        int blanks = 0;
        while (i < after.Length && (after[i] is ' ' or '\t' or '\u00A0' or ',' or ';' or ':' or '-' or '\u2013' or '\u2014' or '|' or '/' or '(' or '\u00B7' or '\u2022' or '[' or '\\'))
        {
            blanks = after[i] is ' ' or '\u00A0' ? blanks + 1 : 0;
            separated |= after[i] is not (' ' or '\u00A0') || blanks >= 2;
            i++;
        }

        if (needSeparator && !separated)
        {
            return false;
        }

        return IsJobWords(after[i..], 4);
    }

    private static bool EndsWithJob(ReadOnlySpan<char> before)
    {
        ReadOnlySpan<char> t = before.TrimEnd(" \t\u00A0:,-\u2013\u2014|");
        if (t.Length == 0)
        {
            return false;
        }

        int e = t.Length;
        int s = e;
        while (s > 0 && (char.IsLetter(t[s - 1]) || t[s - 1] is '-' or '.' or '\''))
        {
            s--;
        }

        string last = NameFolding.LettersAndDigits(t[s..e]);
        return NameVocabulary.Roles.Contains(last);
    }

    // True when the words begin with a job title: a job noun among the first few words, with only capitalized words, lead words and
    // connectors in front of it ("Property Manager", "Vice President of Sales", "Responsable du service").
    private static bool IsJobWords(ReadOnlySpan<char> s, int max)
    {
        int count = 0;
        int i = 0;
        while (i < s.Length && count < max)
        {
            while (i < s.Length && !char.IsLetter(s[i]))
            {
                i++;
            }

            int w = i;
            while (i < s.Length && (char.IsLetter(s[i]) || s[i] is '-' or '\'' or '&' or '.'))
            {
                i++;
            }

            if (i == w)
            {
                break;
            }

            string f = NameFolding.LettersAndDigits(s[w..i].ToString().TrimEnd('.'));
            if (NameVocabulary.Roles.Contains(f))
            {
                return true;
            }

            if (!(char.IsUpper(s[w]) || NameVocabulary.RoleLeads.Contains(f) || NameVocabulary.JobConnectors.Contains(f)))
            {
                return false;
            }

            count++;
        }

        return false;
    }

    private bool IsJobLine(int line)
    {
        TextLine l = _view.Lines[line];
        if (l.End - l.Start > 70 || _kinds[line] != 0)
        {
            return false;
        }

        string text = _text.Substring(l.Start, l.End - l.Start);
        foreach (char c in text)
        {
            if (char.IsDigit(c) || c == '@')
            {
                return false;
            }
        }

        return IsJobWords(text, 4) && text.Split([' ', '\u00A0'], StringSplitOptions.RemoveEmptyEntries).Length <= 8;
    }

    private bool IsClosing(int line)
    {
        TextLine l = _view.Lines[line];
        string t = _text.Substring(l.Start, l.End - l.Start).Trim().TrimEnd(',', '.', '!', ':', ';', '-', ' ').ToLowerInvariant();
        return t.Length > 0 && NameVocabulary.Closings.Contains(t);
    }

    // ---------------------------------------------------------------- employers by position

    // The people found before the organization passes begin, by line (later passes add organizations only), in the order they were found.
    private List<NameHit>?[]? _peopleByLine;

    private List<NameHit> PeopleOn(int line)
    {
        if (_peopleByLine is null)
        {
            _peopleByLine = new List<NameHit>?[_view.Lines.Count];
            foreach (NameHit h in _hits)
            {
                if (!h.Organization && h.Line >= 0 && h.Line < _peopleByLine.Length)
                {
                    (_peopleByLine[h.Line] ??= []).Add(h);
                }
            }
        }

        return _peopleByLine[line] ?? NoHits;
    }

    private static readonly List<NameHit> NoHits = [];
    private static readonly List<(FieldKind Kind, int Start, int End)> NoFields = [];

    private static readonly string[] EmployerWords = ["at", "of", "from", "chez", "with", "for", "@"];

    // "Lara Mendez at Caisse Populaire", "Thomas Orsi de 6659 Québec": the capitalized words after a person and "at" are the employer.
    private void EmployerAfterPerson(int line)
    {
        List<Tok> toks = TokensOf(line);
        foreach (NameHit h in PeopleOn(line))
        {
            int k = 0;
            while (k < toks.Count && toks[k].CoreEnd < h.End)
            {
                k++;
            }

            // k is the last token of the name; the connector follows.
            int c = k + 1;
            if (k >= toks.Count || toks[k].CoreEnd != h.End || c >= toks.Count || toks[k].TrailPunct)
            {
                continue;
            }

            string w = Word(toks[c]);
            bool connector = Array.IndexOf(EmployerWords, LowerWord(toks[c])) >= 0 || (w.Equals("de", StringComparison.OrdinalIgnoreCase));
            if (!connector || toks[c].TrailPunct || c + 1 >= toks.Count)
            {
                continue;
            }

            AddOrganizationWords(toks, c + 1, line, 7);
        }
    }

    private static readonly string[] OrganizationCues =
    [
        "visit", "recommend", "recommended", "recommande", "called", "call", "contact", "email", "stop by", "thanks to", "thank you", "booked", "hired",
        "using", "used", "kiosque de", "chez", "spoke with", "from", "no longer at", "now at", "work at", "works at", "works for", "work for",
    ];

    // The last word of every cue above, so that a word that ends none of them costs no string building.
    private static readonly HashSet<string> OrganizationCueEnds = new(OrganizationCues.Select(static c => c[(c.LastIndexOf(' ') + 1)..]), StringComparer.Ordinal);

    // "Highly recommend Smith Plumbing", "Kiosque de Gestion Alpha": the cue is no proof; the words must also read as a business (a word like Plumbing or a legal form).
    private void OrganizationAfterCue(int line)
    {
        List<Tok> toks = TokensOf(line);
        for (int k = 0; k + 1 < toks.Count; k++)
        {
            if (!IsOrgWord(toks[k + 1]) || toks[k].TrailPunct)
            {
                continue;
            }

            string one = LowerWord(toks[k]);
            if (!OrganizationCueEnds.Contains(one))
            {
                continue;
            }

            bool cue = false;
            string two = k > 0 ? LowerWord(toks[k - 1]) + " " + one : one;
            string three = k > 1 ? LowerWord(toks[k - 2]) + " " + two : two;
            foreach (string c in OrganizationCues)
            {
                cue |= c == one || c == two || c == three;
            }

            if (cue)
            {
                AddOrganizationWords(toks, k + 1, line, 7, requireBusiness: true);
            }
        }
    }

    private void AddOrganizationWords(List<Tok> toks, int first, int line, int max, bool requireBusiness = false)
    {
        int last = -1;
        bool business = false;
        for (int i = first; i < toks.Count && i - first < max; i++)
        {
            Tok t = toks[i];
            if (!(IsOrgWord(t) || (IsConnector(t) && i > first)) || (i > first && (t.LeadPunct || Apart(toks[i - 1], t))))
            {
                break;
            }

            string f = Folded(t);
            business |= IsIndustryWord(f, 3) || IsLegalToken(t, out _);
            last = i;
            if (t.TrailPunct)
            {
                if (_text[t.CoreEnd] == ',' && i + 1 < toks.Count && IsLegalToken(toks[i + 1], out _))
                {
                    continue;
                }

                break;
            }

            if (_text[t.CoreEnd - 1] == '.' && !IsLegalToken(t, out _) && f.Length > 2)
            {
                break;
            }
        }

        if (last < first || (requireBusiness && !business))
        {
            return;
        }

        string firstFolded = Folded(toks[first]);
        if ((NameVocabulary.Stops.Contains(firstFolded) && !IsIndustryWord(firstFolded, 3)) || NameVocabulary.Cues.Contains(firstFolded) || IsChromeWord(firstFolded)
            || NameVocabulary.Places.Contains(firstFolded) || char.IsDigit(_text[toks[first].CoreStart]) && !business)
        {
            return;
        }

        // A name is not its own employer: a run that is the name of a person already taken is left alone (Add refuses overlaps).
        int start = toks[first].CoreStart;
        int end = OrgEnd(toks[last]);

        if (end - start >= 2 && Free(start, end))
        {
            Add(start, end, true, line);
        }
    }

    // ---------------------------------------------------------------- a first name on its own

    private static readonly string[] Greetings = ["hi", "hello", "hey", "dear", "hola", "bonjour", "salut", "bonsoir", "cher", "ch\u00e8re", "estimado", "estimada", "morning", "afternoon", "evening", "thanks", "thx", "cheers", "you"];

    // "Hi Lucile," and "Dear Mr. Smith," address a person by the name; so does a name alone under "Thanks," or "Regards,".
    private void SingleNames(int line)
    {
        List<Tok> toks = TokensOf(line);
        for (int k = 0; k + 1 < toks.Count; k++)
        {
            string g = Folded(toks[k]);
            if (Array.IndexOf(Greetings, g) < 0)
            {
                continue;
            }

            // "Thank you, Dan." and "Hi, Dan": a comma after the opener is fine for thanks and for a name the lists know.
            bool comma = toks[k].TrailPunct && _text[toks[k].CoreEnd] == ',' && toks[k + 1].Start - toks[k].End == 1;
            if (toks[k].TrailPunct && !(comma && _loose && (g is "you" or "thanks" or "thx" or "cheers" or "morning" or "afternoon" or "evening" || StrongGivenName(toks[k + 1]))))
            {
                continue;
            }

            // "Good morning Dan", "Thank you Dan": the opener has a word of its own in front of it.
            bool opens = k == 0 || toks[k - 1].TrailPunct || (g is "morning" or "afternoon" or "evening" && Folded(toks[k - 1]) == "good") || (g == "you" && Folded(toks[k - 1]) == "thank");
            if (!opens || (g == "you" && k == 0))
            {
                continue;
            }

            int n = k + 1;
            Tok first = toks[n];
            bool honorific = KindOf(first) == Kind.Honorific && n + 1 < toks.Count && Joined(first, toks[n + 1]);
            Tok name = honorific ? toks[n + 1] : first;
            if (KindOf(name) == Kind.Word && SingleNameOk(name, toks, honorific ? n + 1 : n))
            {
                int start = first.CoreStart;
                int end = name.CoreEnd;
                if (_text[end - 1] == '.')
                {
                    end--;
                }

                if (Free(start, end))
                {
                    Add(start, end, false, line);
                }
            }
        }

        if (line > 0 && toks.Count == 1 && SameSection(line - 1, line) && IsClosing(line - 1) && KindOf(toks[0]) == Kind.Word && SingleNameOk(toks[0], toks, 0))
        {
            int end = toks[0].CoreEnd;
            if (_text[end - 1] == '.')
            {
                end--;
            }

            if (Free(toks[0].CoreStart, end))
            {
                Add(toks[0].CoreStart, end, false, line);
            }
        }
    }

    // A single capitalized word that ends its phrase and is not a word of page chrome, a place or a job.
    private bool SingleNameOk(Tok t, List<Tok> toks, int index)
    {
        string f = Folded(t);
        if (f.Length < 2 || NameVocabulary.Stops.Contains(f) || NameVocabulary.Places.Contains(f) || NameVocabulary.Roles.Contains(f) || NameVocabulary.Cues.Contains(f)
            || NameVocabulary.JobConnectors.Contains(f) || IsChromeWord(f))
        {
            return false;
        }

        if (t.TrailPunct)
        {
            return _text[t.CoreEnd] is ',' or '!';
        }

        // Without punctuation the word must be the last on its line: "Hi Lucile" and nothing after it.
        return index == toks.Count - 1;
    }

    // ---------------------------------------------------------------- names written in other ways

    // Words that cannot be the first word of a family name written in front of a comma.
    private static bool NotFamilyLead(string folded) =>
        NameVocabulary.Stops.Contains(folded) || NameVocabulary.Cues.Contains(folded) || NameVocabulary.Places.Contains(folded) || NameVocabulary.Roles.Contains(folded)
        || NameVocabulary.Industry.Contains(folded) || NameLists.Openers.Contains(folded) || IsChromeWord(folded);

    // "Lopez, Maria" and "Smith, John A.": the family name, a comma, the given name. A pair like this stands at the front of a line or
    // after a mark, and the phrase ends with it (or with a mark, a job title or a number), so that a greeting like "Great, Jim, I'll call" is no name.
    private void LastFirst(int line)
    {
        List<Tok> toks = TokensOf(line);
        for (int k = 0; k + 1 < toks.Count; k++)
        {
            Tok a = toks[k];
            Tok b = toks[k + 1];
            if (!a.TrailPunct || _text[a.CoreEnd] != ',' || b.LeadPunct || KindOf(a) != Kind.Word || KindOf(b) != Kind.Word)
            {
                continue;
            }

            bool listed = LikelyGiven(b);
            if (!listed && NotFamilyLead(Folded(b)))
            {
                continue;
            }

            // The comma straight after the family name and one space before the given name is the only form read.
            if (a.End != a.CoreEnd + 1 || b.Start - a.End != 1)
            {
                continue;
            }

            string fa = Folded(a);
            if (NotFamilyLead(fa) || (listed && LikelyGiven(a) && !KnownFamily(a)) || (NameLists.AmbiguousGiven.Contains(Folded(b)) && !KnownFamily(a)))
            {
                continue;
            }

            int j = k + 1;
            // A middle name or initial: "Smith, John A.", "Smith, John Michael".
            if (j + 1 < toks.Count && !toks[j].TrailPunct && !toks[j + 1].LeadPunct && toks[j + 1].Start - toks[j].End == 1)
            {
                Kind mk = KindOf(toks[j + 1]);
                if (mk == Kind.Initial || (mk == Kind.Word && LikelyGiven(toks[j + 1]) && !KnownFamily(toks[j + 1])))
                {
                    j++;
                }
            }

            bool front = k == 0 || toks[k - 1].TrailPunct;
            int start = a.CoreStart;
            int end = toks[j].CoreEnd;
            if (_text[end - 1] == '.' && KindOf(toks[j]) != Kind.Initial)
            {
                end--;
            }

            TextLine l = _view.Lines[line];
            bool cue = CuePhraseBefore(_text.AsSpan(l.Start, start - l.Start));
            if (!front && !cue)
            {
                continue;
            }

            // What follows: the end of the line, a mark that ends a phrase, a job title, or a contact field.
            bool endsPhrase = j == toks.Count - 1;
            bool strongEnd = false;
            if (!endsPhrase || !listed)
            {
                int at = toks[j].CoreEnd;
                while (at < l.End - 1 && _text[at] is ' ' or '\t' or '\u00A0')
                {
                    at++;
                }

                char c = at < _text.Length ? _text[at] : '\n';
                ReadOnlySpan<char> rest = _text.AsSpan(toks[j].CoreEnd, l.End - toks[j].CoreEnd);
                strongEnd = StartsJobPhrase(rest, true) || FieldFollows(line, toks[j].CoreEnd) || (rest.TrimStart().Length > 0 && rest.TrimStart()[0] is '-' or '\u2013' && FieldFollows(line, toks[j].CoreEnd + 2));
                endsPhrase = toks[j].TrailPunct && (c is ';' or '|' or ')' or ']' or '-' or '\u2013' or ':' or '.' || strongEnd);
            }
            else
            {
                strongEnd = true;
            }

            strongEnd |= HeaderLabelBefore(_text.AsSpan(l.Start, start - l.Start)) || cue;
            if (!endsPhrase || (!listed && !strongEnd) || !Free(start, end))
            {
                continue;
            }

            Trace?.Invoke($"person [{_text.Substring(start, end - start)}] last-first");
            Add(start, end, false, line);
            k = j;
        }
    }

    // A contact field of the line begins after the index, with only link words between.
    private bool FieldFollows(int line, int from)
    {
        foreach ((FieldKind kind, int fs, int fe) in _linked[line] ?? NoFields)
        {
            if (kind != FieldKind.Address && fs >= from && GapIsLink(from, fs, false))
            {
                return true;
            }
        }

        return false;
    }

    // "john smith 555-0100", "spoke with maria lopez": names in lower case, which count only with a cue in front of them or a contact field
    // beside them, and only for a given name and a family name people commonly have.
    private void LowerCaseNames(int line)
    {
        List<Tok> toks = TokensOf(line);
        TextLine l = _view.Lines[line];
        for (int k = 0; k + 1 < toks.Count; k++)
        {
            Tok a = toks[k];
            Tok b = toks[k + 1];
            if (!IsLowerWord(a) || !IsLowerWord(b) || a.TrailPunct || !Joined(a, b))
            {
                continue;
            }

            string fa = Folded(a);
            string fb = Folded(b);
            bool givenFirst = NameLists.Given.Contains(fa) || NameLexicon.IsGivenName(fa);
            bool familySecond = NameLists.Family.Contains(fb);
            if ((!givenFirst && !familySecond) || fb.Length < 3 || fa.Length < 3 || NotNameWord(fa) || NotNameWord(fb) || NameLists.TimeWords.Contains(fb))
            {
                continue;
            }

            int start = a.CoreStart;
            int end = b.CoreEnd;
            ReadOnlySpan<char> ahead = _text.AsSpan(l.Start, start - l.Start);
            bool both = givenFirst && familySecond;
            string verb = NextWord(_text.AsSpan(end, l.End - end));
            bool evidence = CuePhraseBefore(ahead) || (_linked[line] is { } fields && AdjacentField(fields, start, end)) || (both && WeakCueBefore(ahead))
                || (verb.Length > 0 && (NameLists.SpeechVerbs.Contains(verb) || verb == "left")) || EmailLocalMatches(line, toks, k, k + 1);
            if (!evidence || !Free(start, end))
            {
                continue;
            }

            Trace?.Invoke($"person [{_text.Substring(start, end - start)}] lower-case");
            Add(start, end, false, line);
            k++;
        }
    }

    private bool AdjacentField(List<(FieldKind Kind, int Start, int End)> fields, int start, int end)
    {
        foreach ((FieldKind kind, int fs, int fe) in fields)
        {
            if (kind == FieldKind.Address)
            {
                continue;
            }

            if ((fs >= end && GapIsLink(end, fs, false)) || (fe <= start && GapIsLink(fe, start, false)))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsLowerWord(Tok t)
    {
        ReadOnlySpan<char> w = _text.AsSpan(t.CoreStart, t.CoreEnd - t.CoreStart);
        if (w.Length < 2 || !char.IsLower(w[0]))
        {
            return false;
        }

        foreach (char c in w)
        {
            if (!(char.IsLetter(c) || c is '-' or '\'' or '\u2019'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool NotNameWord(string folded) =>
        NameVocabulary.Stops.Contains(folded) || NameVocabulary.Cues.Contains(folded) || NameVocabulary.Places.Contains(folded) || NameVocabulary.Roles.Contains(folded)
        || NameVocabulary.Industry.Contains(folded) || NameVocabulary.JobConnectors.Contains(folded);

    // Words in front of a single given name that make it a person: "ask for", "spoke with", "my name is".
    private static readonly string[] SingleCues =
    [
        "ask for", "asked for", "asking for", "speak to", "speak with", "spoke to", "spoke with", "speaking with", "talk to", "talk with", "talked to", "talked with",
        "call", "please call", "contact", "my name is", "name is", "this is", "i'm", "i am", "thanks to", "thank you", "thanks", "thx", "ty", "met with", "meet",
        "reach out to", "attn", "attention", "cc", "regards", "cheers", "sincerely",
    ];

    // Words that, with a cue from the strong list only, let a lower-case given name stand.
    private static readonly string[] LowerCaseCues = ["ask for", "asked for", "asking for", "speak to", "speak with", "spoke to", "spoke with", "talk to", "talk with", "talked to", "talked with", "my name is", "this is", "i'm", "i am"];

    // "Thanks Maria!", "spoke with Dan", "Sofia called": a given name on its own with a cue in front of it or a verb after it.
    private void CuedSingles(int line)
    {
        List<Tok> toks = TokensOf(line);
        TextLine l = _view.Lines[line];
        for (int k = 0; k < toks.Count; k++)
        {
            Tok t = toks[k];
            Kind kind = KindOf(t);
            bool lower = kind == Kind.None && IsLowerWord(t);
            if (kind != Kind.Word && !lower)
            {
                continue;
            }

            string f = Folded(t);
            if (f.Length < 3 || !StrongGivenName(t) || NotNameWord(f) || IsChromeWord(f) || (lower && NameLists.AmbiguousGiven.Contains(f)))
            {
                continue;
            }

            // On its own: no capitalized word joined to it on either side.
            if ((k > 0 && !toks[k - 1].TrailPunct && !t.LeadPunct && KindOf(toks[k - 1]) is Kind.Word or Kind.Initial or Kind.Honorific)
                || (k + 1 < toks.Count && !t.TrailPunct && !toks[k + 1].LeadPunct && KindOf(toks[k + 1]) is Kind.Word or Kind.Initial))
            {
                continue;
            }

            int start = t.CoreStart;
            int end = t.CoreEnd;
            if (_text[end - 1] == '.')
            {
                end--;
            }

            ReadOnlySpan<char> before = _text.AsSpan(l.Start, start - l.Start);
            bool ambiguous = NameLists.AmbiguousGiven.Contains(f);
            string why = string.Empty;
            if (!t.LeadPunct && SingleCueBefore(before, lower ? LowerCaseCues : SingleCues) && (!ambiguous || kind == Kind.Word) && EndsOrPunctuated(toks, k))
            {
                why = "cue";
            }
            else if (kind == Kind.Word && !ambiguous && k + 1 < toks.Count && !t.TrailPunct && NameLists.PersonVerbs.Contains(LowerWord(toks[k + 1])) && SentenceStart(before))
            {
                why = "verb";
            }
            else if (kind == Kind.Word && !ambiguous && _linked[line] is { } fields && AdjacentField(fields, start, end))
            {
                why = "contact-field";
            }

            if (why.Length == 0 || !Free(start, end))
            {
                continue;
            }

            Trace?.Invoke($"person [{_text.Substring(start, end - start)}] single-{why}");
            Add(start, end, false, line);
        }
    }

    private static bool SingleCueBefore(ReadOnlySpan<char> before, string[] cues)
    {
        ReadOnlySpan<char> t = before.TrimEnd(" \t\u00A0:,-\u2013");
        if (t.Length == 0)
        {
            return false;
        }

        foreach (string cue in cues)
        {
            if (t.EndsWith(cue, StringComparison.OrdinalIgnoreCase) && (t.Length == cue.Length || !(char.IsLetter(t[t.Length - cue.Length - 1]) || t[t.Length - cue.Length - 1] == '\'')))
            {
                return true;
            }
        }

        return false;
    }

    // The name ends its phrase: the last word of the line, or followed by a mark or a word that is not capitalized.
    private bool EndsOrPunctuated(List<Tok> toks, int k)
    {
        if (k == toks.Count - 1 || toks[k].TrailPunct)
        {
            return true;
        }

        Tok next = toks[k + 1];
        return !next.LeadPunct && char.IsLower(_text[next.CoreStart]);
    }

    // The name begins a sentence or a clause: the line, a mark, or a conjunction before it.
    private static bool SentenceStart(ReadOnlySpan<char> before)
    {
        ReadOnlySpan<char> t = before.TrimEnd();
        return t.Length == 0 || t[^1] is '.' or '!' or '?' or ':' or ';' or ',' or '"' or '\u201C' or '(' or '-' or '\u2013' || t.EndsWith(" and", StringComparison.OrdinalIgnoreCase)
            || t.EndsWith(" but", StringComparison.OrdinalIgnoreCase) || t.EndsWith(" then", StringComparison.OrdinalIgnoreCase) || t.EndsWith(" so", StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- a signature's company line

    private void SignatureCompany(int line)
    {
        // Only under a name on a line of its own that this reader took.
        NameHit? person = null;
        TextLine tl = _view.Lines[line];
        foreach (NameHit h in PeopleOn(line))
        {
            if (_text.AsSpan(tl.Start, h.Start - tl.Start).Trim().IsEmpty)
            {
                person = h;
                break;
            }
        }

        if (person is null)
        {
            return;
        }

        int at = line + 1;
        bool afterJob = false;
        if (at < _view.Lines.Count && SameSection(line, at) && IsJobLine(at))
        {
            afterJob = true;
            at++;
        }

        if (at >= _view.Lines.Count || !SameSection(line, at) || _kinds[at] != 0 || !ContactAhead(at, 4))
        {
            return;
        }

        TextLine l = _view.Lines[at];
        List<Tok> toks = TokensOf(at);
        if (toks.Count is 0 or > 7 || l.End - l.Start > 70 || IsJobLine(at))
        {
            return;
        }

        foreach (Tok t in toks)
        {
            string f = Folded(t);
            if (!IsOrgWord(t) && !(IsConnector(t)) || NameVocabulary.Cues.Contains(f) && !IsConnector(t) || IsChromeWord(f))
            {
                return;
            }

            if (f.All(char.IsAsciiDigit) && f.Length > 0 && f.Length < 7)
            {
                return;
            }

            if ((NameVocabulary.Stops.Contains(f) && !IsIndustryWord(f, 3)) || NameVocabulary.Places.Contains(f) || NameVocabulary.Roles.Contains(f))
            {
                return;
            }
        }

        if (Word(toks[^1]) is { Length: 2 } lastCode && NameVocabulary.RegionCodes.Contains(lastCode))
        {
            return;
        }

        int start = toks[0].CoreStart;
        int end = OrgEnd(toks[^1]);

        if (end - start < 2 || !Free(start, end))
        {
            return;
        }

        string candidate = _text.Substring(start, end - start);
        NameClass cls;
        try
        {
            cls = NameParsing.Classify(candidate);
        }
        catch (ArgumentException)
        {
            return;
        }

        // Straight under the name, the line must read as a company; under a job title it is the company by position.
        if (cls == NameClass.Company || (afterJob && cls != NameClass.Multiple))
        {
            Add(start, end, true, at);
        }
    }
}
