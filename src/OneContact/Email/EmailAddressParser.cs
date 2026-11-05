using System.Buffers;
using System.Text;

namespace RecursiveAI.OneContact.Email;

/// <summary>
/// Reads one mailbox: an addr-spec or a name-addr with a display name (RFC 5322 section 3.4), the obsolete forms of section 4 (flagged,
/// not refused) and the internationalized forms of RFC 6531/6532. Offsets index the original input.
/// </summary>
internal static class EmailAddressParser
{
    // Characters of RFC 5322 section 3.2.3 atext, ASCII part.
    private const string AtextSpecials = "!#$%&'*+-/=?^_`{|}~";
    private const int MaxCommentDepth = 64;

    internal static OperationStatus TryParse(ReadOnlySpan<char> input, out EmailAddress value, out ParseDetail detail)
    {
        Scanner scanner = new(input, wantDetail: true);
        if (scanner.Run())
        {
            value = scanner.Result();
            detail = scanner.Detail();
            return OperationStatus.Done;
        }

        value = default;
        detail = ParseDetail.Failure(scanner.FailReason, scanner.FailSpan);
        return OperationStatus.InvalidData;
    }

    internal static bool TryParseValue(ReadOnlySpan<char> input, out EmailAddress value)
    {
        Scanner scanner = new(input, wantDetail: false);
        if (scanner.Run())
        {
            value = scanner.Result();
            return true;
        }

        value = default;
        return false;
    }

    private static bool IsAtextAscii(char c) => char.IsAsciiLetterOrDigit(c) || AtextSpecials.Contains(c, StringComparison.Ordinal);

    private static bool IsWsp(char c) => c is ' ' or '\t';

    private static bool IsControl(char c) => c < 32 || c == 127;

    private ref struct Scanner
    {
        private readonly ReadOnlySpan<char> _s;
        private readonly bool _wantDetail;
        private EmailFacts _facts;
        private List<ParseIssue>? _issues;
        private SourceSpan _sourceSpan;
        private SourceSpan _displaySpan;
        private SourceSpan _localSpan;
        private SourceSpan _domainSpan;
        private string? _displayName;
        private string _local;
        private string _domain;

        internal Scanner(ReadOnlySpan<char> s, bool wantDetail)
        {
            _s = s;
            _wantDetail = wantDetail;
            _local = string.Empty;
            _domain = string.Empty;
        }

        internal ReasonCode FailReason { get; private set; }

        internal SourceSpan FailSpan { get; private set; }

        internal readonly EmailAddress Result()
        {
            string canonical = string.Concat(_local, "@", _domain);
            return new EmailAddress(canonical, _local.Length, _displayName, _sourceSpan, _facts);
        }

        internal readonly ParseDetail Detail()
        {
            List<ParsedField> fields = [];
            if ((_facts & EmailFacts.HasDisplayName) != 0)
            {
                fields.Add(new ParsedField("display-name", _displaySpan, 1f, DecisionSource.Rule));
            }

            fields.Add(new ParsedField("local-part", _localSpan, 1f, DecisionSource.Rule));
            fields.Add(new ParsedField("domain", _domainSpan, 1f, DecisionSource.Rule));
            return new ParseDetail(_issues ?? [], fields);
        }

        private bool Fail(ReasonCode reason, int start, int length)
        {
            FailReason = reason;
            FailSpan = new SourceSpan(Math.Max(start, 0), length);
            return false;
        }

        private void Note(ReasonCode reason, int start, int length, IssueSeverity severity)
        {
            if (_wantDetail)
            {
                (_issues ??= []).Add(new ParseIssue(reason, new SourceSpan(start, length), severity));
            }
        }

        // RFC 5322 section 4: a form that is only valid in the obsolete grammar. Reported once.
        private void Obsolete(int start, int length)
        {
            if ((_facts & EmailFacts.ObsoleteSyntax) == 0)
            {
                _facts |= EmailFacts.ObsoleteSyntax;
                Note(ReasonCode.EmailObsoleteSyntax, start, length, IssueSeverity.Info);
            }
        }

        internal bool Run()
        {
            int start = 0;
            int end = _s.Length;
            while (start < end && char.IsWhiteSpace(_s[start]))
            {
                start++;
            }

            while (end > start && char.IsWhiteSpace(_s[end - 1]))
            {
                end--;
            }

            if (start == end)
            {
                return Fail(ReasonCode.Empty, 0, _s.Length);
            }

            _sourceSpan = new SourceSpan(start, end - start);
            int addrStart = start;
            int addrEnd = end;
            bool angle = false;
            int lt = FindTopLevel('<', start, end);
            if (lt >= 0)
            {
                if (!ParseDisplayName(start, lt))
                {
                    return false;
                }

                int gt = FindTopLevel('>', lt + 1, end);
                if (gt < 0)
                {
                    return Fail(ReasonCode.EmailUnbalancedAngle, lt, 1);
                }

                // RFC 5322 section 3.4: name-addr = [display-name] angle-addr; angle-addr = [CFWS] "<" addr-spec ">" [CFWS].
                int after = gt + 1;
                if (!SkipCfws(ref after, end))
                {
                    return false;
                }

                if (after != end)
                {
                    return Fail(ReasonCode.EmailTrailingText, after, end - after);
                }

                addrStart = lt + 1;
                addrEnd = gt;
                angle = true;
            }

            if (!ParseAddrSpec(addrStart, addrEnd, angle))
            {
                return false;
            }

            Classify();
            return true;
        }

        // Finds the first target outside quoted strings, comments and domain literals; -1 when there is none.
        private readonly int FindTopLevel(char target, int from, int to)
        {
            int i = from;
            while (i < to)
            {
                char c = _s[i];
                if (c == target)
                {
                    return i;
                }

                switch (c)
                {
                    case '"':
                        i++;
                        while (i < to && _s[i] != '"')
                        {
                            i += _s[i] == '\\' ? 2 : 1;
                        }

                        break;
                    case '(':
                        int depth = 0;
                        while (i < to)
                        {
                            if (_s[i] == '\\')
                            {
                                i++;
                            }
                            else if (_s[i] == '(')
                            {
                                depth++;
                            }
                            else if (_s[i] == ')' && --depth == 0)
                            {
                                break;
                            }

                            i++;
                        }

                        break;
                    case '[':
                        while (i < to && _s[i] != ']')
                        {
                            i += _s[i] == '\\' ? 2 : 1;
                        }

                        break;
                }

                i++;
            }

            return -1;
        }

        // RFC 5322 section 3.2.2: CFWS = comments and folding white space. CRLF is only white space when white space follows it.
        private bool SkipCfws(ref int i, int to)
        {
            int folds = 0;
            while (i < to)
            {
                char c = _s[i];
                if (IsWsp(c))
                {
                    i++;
                }
                else if (c == '\r' && i + 2 < to && _s[i + 1] == '\n' && IsWsp(_s[i + 2]))
                {
                    folds++;
                    i += 2;
                }
                else if (c == '(')
                {
                    if (!SkipComment(ref i, to))
                    {
                        return false;
                    }
                }
                else
                {
                    break;
                }
            }

            if (folds > 1)
            {
                Obsolete(i, 0); // obs-FWS: several folds in a row (section 4.2)
            }

            return true;
        }

        // RFC 5322 section 3.2.2: comment = "(" *([FWS] ccontent) [FWS] ")"; ccontent = ctext / quoted-pair / comment.
        private bool SkipComment(ref int i, int to)
        {
            int open = i;
            int depth = 0;
            while (i < to)
            {
                char c = _s[i];
                if (c == '(')
                {
                    if (++depth > MaxCommentDepth)
                    {
                        return Fail(ReasonCode.EmailInvalidComment, open, 1);
                    }

                    i++;
                }
                else if (c == ')')
                {
                    i++;
                    if (--depth == 0)
                    {
                        _facts |= EmailFacts.HasComment;
                        return true;
                    }
                }
                else if (c == '\\')
                {
                    if (i + 1 >= to)
                    {
                        return Fail(ReasonCode.EmailInvalidComment, open, 1);
                    }

                    i += 2;
                    if (char.IsHighSurrogate(_s[i - 1]) && i < to && char.IsLowSurrogate(_s[i]))
                    {
                        i++;
                    }
                }
                else if (c == '\r' && i + 2 < to && _s[i + 1] == '\n' && IsWsp(_s[i + 2]))
                {
                    i += 2;
                }
                else if (c is '\r' or '\n' or '\0')
                {
                    return Fail(ReasonCode.EmailInvalidCharacter, i, 1);
                }
                else if (char.IsSurrogate(c))
                {
                    if (!char.IsHighSurrogate(c) || i + 1 >= to || !char.IsLowSurrogate(_s[i + 1]))
                    {
                        return Fail(ReasonCode.EmailInvalidCharacter, i, 1);
                    }

                    i += 2;
                }
                else
                {
                    if (IsControl(c) && !IsWsp(c))
                    {
                        Obsolete(i, 1); // obs-ctext
                    }

                    i++;
                }
            }

            return Fail(ReasonCode.EmailInvalidComment, open, 1);
        }

        // Scans atext (RFC 5322 section 3.2.3, extended by RFC 6532 section 3.2 to every non-ASCII character). Returns false after
        // recording an unpaired surrogate.
        private bool ScanAtom(int from, int to, out int end)
        {
            int i = from;
            while (i < to)
            {
                char c = _s[i];
                if (c < 0x80)
                {
                    if (!IsAtextAscii(c))
                    {
                        break;
                    }

                    i++;
                }
                else if (char.IsSurrogate(c))
                {
                    if (!char.IsHighSurrogate(c) || i + 1 >= to || !char.IsLowSurrogate(_s[i + 1]))
                    {
                        end = i;
                        return Fail(ReasonCode.EmailInvalidCharacter, i, 1);
                    }

                    i += 2;
                }
                else
                {
                    i++;
                }
            }

            end = i;
            return true;
        }

        // RFC 5322 section 3.2.4: quoted-string. With raw, the quotes and quoted-pairs are kept as written (the canonical local part);
        // otherwise the unquoted text is appended (a display name). Folding CRLF is removed, as section 2.2.3 unfolds.
        private bool ReadQuoted(ref int i, int to, StringBuilder sb, bool raw)
        {
            int open = i;
            i++;
            if (raw)
            {
                sb.Append('"');
            }

            while (i < to)
            {
                char c = _s[i];
                if (c == '"')
                {
                    i++;
                    if (raw)
                    {
                        sb.Append('"');
                    }

                    return true;
                }

                if (c == '\\')
                {
                    if (i + 1 >= to)
                    {
                        break;
                    }

                    char next = _s[i + 1];
                    int length = 2;
                    if (char.IsSurrogate(next))
                    {
                        if (!char.IsHighSurrogate(next) || i + 2 >= to || !char.IsLowSurrogate(_s[i + 2]))
                        {
                            return Fail(ReasonCode.EmailInvalidCharacter, i + 1, 1);
                        }

                        length = 3;
                    }
                    else if (IsControl(next) || next is '\r' or '\n')
                    {
                        Obsolete(i, 2); // obs-qp
                    }

                    if (raw)
                    {
                        sb.Append(_s.Slice(i, length));
                    }
                    else
                    {
                        sb.Append(_s.Slice(i + 1, length - 1));
                    }

                    i += length;
                }
                else if (c == '\r' && i + 2 < to && _s[i + 1] == '\n' && IsWsp(_s[i + 2]))
                {
                    i += 2;
                }
                else if (c is '\r' or '\n' or '\0')
                {
                    return Fail(ReasonCode.EmailInvalidCharacter, i, 1);
                }
                else if (char.IsSurrogate(c))
                {
                    if (!char.IsHighSurrogate(c) || i + 1 >= to || !char.IsLowSurrogate(_s[i + 1]))
                    {
                        return Fail(ReasonCode.EmailInvalidCharacter, i, 1);
                    }

                    sb.Append(_s.Slice(i, 2));
                    i += 2;
                }
                else
                {
                    if (IsControl(c) && !IsWsp(c))
                    {
                        Obsolete(i, 1); // obs-qtext
                    }

                    sb.Append(c);
                    i++;
                }
            }

            return Fail(ReasonCode.EmailInvalidQuotedString, open, 1);
        }

        // RFC 5322 section 3.2.5: phrase = 1*word, with the obsolete form that also allows "." (section 4.1).
        // RFC 2047 section 5 rule 3: an atom that is a whole encoded word is decoded; adjacent encoded words join without the space between.
        private bool ParseDisplayName(int from, int to)
        {
            StringBuilder sb = new();
            int i = from;
            int first = -1;
            int last = from;
            bool previousEncoded = false;
            while (true)
            {
                int before = i;
                if (!SkipCfws(ref i, to))
                {
                    return false;
                }

                bool gap = i > before;
                if (i >= to)
                {
                    break;
                }

                char c = _s[i];
                int tokenStart = i;
                bool encoded = false;
                if (c == '"')
                {
                    if (gap && sb.Length > 0)
                    {
                        sb.Append(' ');
                    }

                    if (!ReadQuoted(ref i, to, sb, raw: false))
                    {
                        return false;
                    }
                }
                else if (c == '.')
                {
                    Obsolete(i, 1);
                    sb.Append('.');
                    i++;
                }
                else
                {
                    if (!ScanAtom(i, to, out int e))
                    {
                        return false;
                    }

                    if (e == i)
                    {
                        return Fail(ReasonCode.EmailInvalidDisplayName, i, 1);
                    }

                    ReadOnlySpan<char> atom = _s[i..e];
                    encoded = EncodedWords.TryDecode(atom, out string decoded);
                    if (gap && sb.Length > 0 && !(encoded && previousEncoded))
                    {
                        sb.Append(' ');
                    }

                    sb.Append(encoded ? decoded : atom.ToString());
                    i = e;
                }

                previousEncoded = encoded;
                if (first < 0)
                {
                    first = tokenStart;
                }

                last = i;
            }

            if (first >= 0)
            {
                _facts |= EmailFacts.HasDisplayName;
                _displayName = sb.ToString();
                _displaySpan = new SourceSpan(first, last - first);
            }

            return true;
        }

        // RFC 5322 section 4.4: obs-route = obs-domain-list ":". Read and dropped; the canonical form is the addr-spec.
        private bool SkipRoute(ref int i, int to)
        {
            Obsolete(i, 1);
            while (true)
            {
                while (i < to && _s[i] == ',')
                {
                    i++;
                    if (!SkipCfws(ref i, to))
                    {
                        return false;
                    }
                }

                if (i < to && _s[i] == '@')
                {
                    i++;
                    if (!SkipCfws(ref i, to))
                    {
                        return false;
                    }

                    int routeStart = i;
                    while (i < to && (_s[i] == '.' || _s[i] == '-' || char.IsAsciiLetterOrDigit(_s[i]) || _s[i] >= 0x80))
                    {
                        i++;
                    }

                    if (i == routeStart)
                    {
                        return Fail(ReasonCode.EmailInvalidDomain, Math.Min(i, to - 1), 1);
                    }

                    if (!SkipCfws(ref i, to))
                    {
                        return false;
                    }
                }

                if (i < to && _s[i] == ',')
                {
                    continue;
                }

                if (i < to && _s[i] == ':')
                {
                    i++;
                    return true;
                }

                return Fail(ReasonCode.EmailInvalidLocalPart, Math.Min(i, to - 1), 1);
            }
        }

        // RFC 5322 section 3.4.1: addr-spec = local-part "@" domain.
        private bool ParseAddrSpec(int from, int to, bool angle)
        {
            int i = from;
            if (!SkipCfws(ref i, to))
            {
                return false;
            }

            if (i >= to)
            {
                return Fail(ReasonCode.Empty, from, to - from);
            }

            if (angle && (_s[i] == '@' || _s[i] == ','))
            {
                if (!SkipRoute(ref i, to) || !SkipCfws(ref i, to))
                {
                    return false;
                }
            }

            if (_s[from..to].IndexOf('@') < 0)
            {
                return Fail(ReasonCode.EmailMissingAt, from, to - from);
            }

            // local-part = dot-atom / quoted-string / obs-local-part (word *("." word), with CFWS allowed around the dots).
            StringBuilder local = new();
            int localStart = -1;
            int localEnd = i;
            int words = 0;
            bool quoted = false;
            int dotAt = -1;
            while (true)
            {
                int wordStart = i;
                if (i < to && _s[i] == '"')
                {
                    quoted = true;
                    if (!ReadQuoted(ref i, to, local, raw: true))
                    {
                        return false;
                    }
                }
                else
                {
                    if (!ScanAtom(i, to, out int e))
                    {
                        return false;
                    }

                    if (e == i)
                    {
                        if (words > 0)
                        {
                            return Fail(ReasonCode.EmailInvalidLocalPart, dotAt, 1);
                        }

                        return i < to && _s[i] == '@'
                            ? Fail(ReasonCode.EmailEmptyLocalPart, i, 1)
                            : Fail(ReasonCode.EmailInvalidLocalPart, i, 1);
                    }

                    local.Append(_s[i..e]);
                    i = e;
                }

                if (localStart < 0)
                {
                    localStart = wordStart;
                }

                localEnd = i;
                words++;
                int afterWord = i;
                if (!SkipCfws(ref i, to))
                {
                    return false;
                }

                if (i < to && _s[i] == '.')
                {
                    if (i > afterWord)
                    {
                        Obsolete(afterWord, i - afterWord);
                    }

                    dotAt = i;
                    local.Append('.');
                    i++;
                    int afterDot = i;
                    if (!SkipCfws(ref i, to))
                    {
                        return false;
                    }

                    if (i > afterDot)
                    {
                        Obsolete(afterDot, i - afterDot);
                    }

                    continue;
                }

                break;
            }

            if (i >= to || _s[i] != '@')
            {
                return Fail(i >= to ? ReasonCode.EmailMissingAt : ReasonCode.EmailInvalidLocalPart, Math.Min(i, to - 1), 1);
            }

            if (quoted)
            {
                _facts |= EmailFacts.QuotedLocalPart;
                if (words > 1)
                {
                    Obsolete(localStart, localEnd - localStart); // obs-local-part mixing quoted and unquoted words
                }
            }

            _local = local.ToString();
            _localSpan = new SourceSpan(localStart, localEnd - localStart);
            foreach (char c in _local)
            {
                if (c >= 0x80)
                {
                    _facts |= EmailFacts.InternationalizedLocalPart;
                    break;
                }
            }

            int atPos = i;
            i++;
            if (!SkipCfws(ref i, to))
            {
                return false;
            }

            if (i >= to)
            {
                return Fail(ReasonCode.EmailEmptyDomain, atPos, 1);
            }

            return _s[i] == '[' ? ParseLiteral(i, to) : ParseHost(i, to);
        }

        // RFC 5322 section 3.4.1: domain = dot-atom / domain-literal / obs-domain (atom *("." atom), CFWS around dots).
        private bool ParseHost(int from, int to)
        {
            int i = from;
            StringBuilder host = new();
            int domainEnd = from;
            while (true)
            {
                if (!ScanAtom(i, to, out int e))
                {
                    return false;
                }

                if (e == i)
                {
                    return Fail(ReasonCode.EmailInvalidDomain, Math.Min(i, to - 1), 1);
                }

                host.Append(_s[i..e]);
                i = e;
                domainEnd = i;
                int afterAtom = i;
                if (!SkipCfws(ref i, to))
                {
                    return false;
                }

                if (i < to && _s[i] == '.')
                {
                    if (i > afterAtom)
                    {
                        Obsolete(afterAtom, i - afterAtom);
                    }

                    host.Append('.');
                    i++;
                    int afterDot = i;
                    if (!SkipCfws(ref i, to))
                    {
                        return false;
                    }

                    if (i > afterDot)
                    {
                        Obsolete(afterDot, i - afterDot);
                    }

                    continue;
                }

                break;
            }

            if (i != to)
            {
                return Fail(ReasonCode.EmailTrailingText, i, to - i);
            }

            _domainSpan = new SourceSpan(from, domainEnd - from);
            if (!EmailDomain.TryNormalize(host.ToString(), ref _facts, out string ascii, out ReasonCode reason))
            {
                return Fail(reason, from, domainEnd - from);
            }

            _domain = ascii;
            return true;
        }

        // RFC 5322 section 3.4.1: domain-literal = [CFWS] "[" *([FWS] dtext) [FWS] "]" [CFWS]; RFC 5321 section 4.1.3: address literals.
        private bool ParseLiteral(int from, int to)
        {
            int i = from + 1;
            StringBuilder content = new();
            while (true)
            {
                if (i >= to)
                {
                    return Fail(ReasonCode.EmailInvalidAddressLiteral, from, 1);
                }

                char c = _s[i];
                if (c == ']')
                {
                    i++;
                    break;
                }

                if (IsWsp(c))
                {
                    i++;
                }
                else if (c == '\r' && i + 2 < to && _s[i + 1] == '\n' && IsWsp(_s[i + 2]))
                {
                    i += 2;
                }
                else if (c is '[' or '\r' or '\n' or '\0')
                {
                    return Fail(ReasonCode.EmailInvalidAddressLiteral, i, 1);
                }
                else if (c == '\\')
                {
                    if (i + 1 >= to)
                    {
                        return Fail(ReasonCode.EmailInvalidAddressLiteral, i, 1);
                    }

                    Obsolete(i, 2); // obs-dtext: quoted-pair
                    content.Append(_s.Slice(i, 2));
                    i += 2;
                }
                else if (char.IsSurrogate(c))
                {
                    if (!char.IsHighSurrogate(c) || i + 1 >= to || !char.IsLowSurrogate(_s[i + 1]))
                    {
                        return Fail(ReasonCode.EmailInvalidCharacter, i, 1);
                    }

                    content.Append(_s.Slice(i, 2));
                    i += 2;
                }
                else
                {
                    if (IsControl(c))
                    {
                        Obsolete(i, 1); // obs-dtext: obs-NO-WS-CTL
                    }

                    content.Append(c);
                    i++;
                }
            }

            int literalEnd = i;
            if (!SkipCfws(ref i, to))
            {
                return false;
            }

            if (i != to)
            {
                return Fail(ReasonCode.EmailTrailingText, i, to - i);
            }

            _domainSpan = new SourceSpan(from, literalEnd - from);
            if (!EmailAddressLiteral.TryNormalize(content.ToString(), out string canonical, out bool smtp))
            {
                return Fail(ReasonCode.EmailInvalidAddressLiteral, from, literalEnd - from);
            }

            if (!smtp)
            {
                Note(ReasonCode.EmailNotSmtpLiteral, from, literalEnd - from, IssueSeverity.Warning);
            }

            _facts |= EmailFacts.DomainLiteral;
            _domain = canonical;
            return true;
        }

        // Flags that follow from the value: length limits, provider lists, role and typo.
        private void Classify()
        {
            int localBytes = Encoding.UTF8.GetByteCount(_local);
            int domainBytes = Encoding.UTF8.GetByteCount(_domain);
            if (localBytes > 64)
            {
                _facts |= EmailFacts.ExceedsLengthLimit;
                Note(ReasonCode.EmailLocalPartTooLong, _localSpan.Start, _localSpan.Length, IssueSeverity.Warning);
            }

            if (domainBytes > 255)
            {
                _facts |= EmailFacts.ExceedsLengthLimit;
                Note(ReasonCode.EmailDomainTooLong, _domainSpan.Start, _domainSpan.Length, IssueSeverity.Warning);
            }

            if (localBytes + 1 + domainBytes > 254)
            {
                _facts |= EmailFacts.ExceedsLengthLimit;
                Note(ReasonCode.EmailAddressTooLong, _sourceSpan.Start, _sourceSpan.Length, IssueSeverity.Warning);
            }

            if ((_facts & EmailFacts.ExceedsLengthLimit) != 0 && localBytes <= 64 && domainBytes <= 255 && localBytes + 1 + domainBytes <= 254)
            {
                Note(ReasonCode.EmailLabelTooLong, _domainSpan.Start, _domainSpan.Length, IssueSeverity.Warning);
            }

            if ((_facts & EmailFacts.SingleLabelDomain) != 0)
            {
                Note(ReasonCode.EmailSingleLabelDomain, _domainSpan.Start, _domainSpan.Length, IssueSeverity.Info);
            }

            if (EmailRules.IsRole(_local))
            {
                _facts |= EmailFacts.RoleAccount;
            }

            if (EmailRules.IsFreeProvider(_domain))
            {
                _facts |= EmailFacts.FreeProvider;
            }

            if (EmailRules.IsDisposable(_domain))
            {
                _facts |= EmailFacts.Disposable;
            }

            if (EmailRules.IsTypoDomain(_domain))
            {
                _facts |= EmailFacts.PossibleTypo;
                Note(ReasonCode.EmailPossibleTypo, _domainSpan.Start, _domainSpan.Length, IssueSeverity.Info);
            }
        }
    }
}
