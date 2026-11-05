using RecursiveAI.OneContact.Addresses.Standardization;

namespace RecursiveAI.OneContact.Addresses.Streets;

/// <summary>
/// Breaks one delivery line into parts and standardizes its tokens: USPS Publication 28 sections 21 to 29 and appendices B to E and I for the United
/// States and Puerto Rico, the Canada Post Addressing Guidelines section 4.4 (English and French editions) for Canada. Rules only; a line the rules
/// cannot read returns <see langword="null"/>. Standardizing rewrites tokens (an abbreviation, a spelling, the position of a number) and never adds
/// a word the input did not have.
/// </summary>
internal sealed class StreetLineParser
{
    private readonly List<AddressToken> _t;
    private readonly bool _ca;
    private readonly bool _pr;
    private readonly bool _noNumberOk;
    private readonly List<StreetPart> _parts = [];

    private StreetLineParser(List<AddressToken> tokens, bool canada, bool puertoRico, bool allowNoNumber)
    {
        _t = tokens;
        _ca = canada;
        _pr = puertoRico;
        _noNumberOk = allowNoNumber;
    }

    /// <summary>Parses the words of one delivery line.</summary>
    /// <param name="tokens">The words of the line.</param>
    /// <param name="canada">True for Canadian rules, false for United States rules.</param>
    /// <param name="puertoRico">True when the last line names Puerto Rico (its address words and number-last forms apply).</param>
    /// <param name="allowNoNumber">True to accept a street line with no house number when it holds a street suffix: the line just above the city is the delivery line.</param>
    /// <returns>The line or lines (a rural or highway contract route with extra words puts them in a line of their own above, Publication 28 sections 245 and 254), or <see langword="null"/> when the words are not a delivery line.</returns>
    internal static List<StreetLine>? Parse(IReadOnlyList<AddressToken> tokens, bool canada, bool puertoRico, bool allowNoNumber = false)
    {
        if (tokens.Count == 0)
        {
            return null;
        }

        StreetLineParser p = new([.. tokens], canada, puertoRico, allowNoNumber);
        return p.Run();
    }

    /// <summary>Finds where the delivery line ends in words that run on into the city (<c>123 MAIN ST APT 4 SPRINGFIELD</c>), for input with no commas or line breaks.</summary>
    /// <param name="tokens">The words before the state and postal code.</param>
    /// <param name="canada">True for Canadian rules.</param>
    /// <returns>How many words belong to the delivery line, or -1 when the rules cannot tell where the city starts.</returns>
    internal static int FindDeliveryEnd(IReadOnlyList<AddressToken> tokens, bool canada)
    {
        int n = tokens.Count;
        if (n < 2)
        {
            return -1;
        }

        // A box, route, military or general delivery line: the structure itself says where it ends.
        string k0 = tokens[0].Key;
        string k1 = n > 1 ? tokens[1].Key : string.Empty;
        string k2 = n > 2 ? tokens[2].Key : string.Empty;
        int boxEnd = BoxPhraseEnd(tokens, 0, canada);
        if (boxEnd > 0 && boxEnd < n && IsNumberLike(tokens[boxEnd].Key))
        {
            int end = boxEnd + 1;
            if (canada)
            {
                end = StationEnd(tokens, end);
            }
            else if (end + 1 < n && tokens[end].Key == "PMB" && IsNumberLike(tokens[end + 1].Key))
            {
                end += 2;
            }

            return end < n ? end : -1;
        }

        if ((k0 == "RR" || (k0 == "RURAL" && k1 == "ROUTE") || k0 == "HC" || (k0 == "STAR" && k1 == "ROUTE")) || IsAttachedRoute(k0))
        {
            int i = k0 is "RURAL" or "STAR" ? 2 : 1;
            if (IsAttachedRoute(k0))
            {
                i = 1;
            }
            else
            {
                i++;
            }

            if (i < n && tokens[i].Key is "BOX" or "BZN" or "BX" or "#")
            {
                i++;
            }

            if (i < n && IsNumberLike(tokens[i].Key))
            {
                i++;
            }

            if (canada)
            {
                i = StationEnd(tokens, i);
            }

            return i < n ? i : -1;
        }

        if (canada && k0 is "GD" or "PR")
        {
            int g = StationEnd(tokens, 1);
            return g < n ? g : -1;
        }

        if (k0 is "PSC" or "CMR" or "UNIT" or "CPR" or "OPC" or "UPR" && n >= 4 && k2 == "BOX")
        {
            return n > 4 ? 4 : -1;
        }

        // A street address: the first street suffix after at least one name word ends the street (consecutive suffix words belong to
        // the name but the last one is the suffix, Publication 28 section 234.2); then a short directional and secondary units follow.
        int start = IsHouseNumber(tokens[0].Text, canada) ? 1 : 0;
        if (start == 1 && n > 2 && IsFraction(tokens[1].Text))
        {
            start = 2;
        }

        int j = -1;
        for (int i = start + 1; i < n; i++)
        {
            if (IsSuffix(tokens[i].Key, canada, out _))
            {
                j = i;
                break;
            }
        }

        if (j < 0)
        {
            return -1;
        }

        while (j + 1 < n && IsSuffix(tokens[j + 1].Key, canada, out _) && j + 2 < n)
        {
            j++;
        }

        int e = j + 1;
        if (e < n - 1 && IsDirectional(tokens[e].Key, canada, out _)
            && (tokens[e].Key.Length <= 2 || (e + 2 < n && IsUnitDesignator(tokens[e + 1].Key, canada, out bool needsUnit) && (!needsUnit || IsNumberLike(tokens[e + 2].Key)))))
        {
            e++;
        }

        while (e < n - 1 && IsUnitDesignator(tokens[e].Key, canada, out bool needs) && (!needs || IsNumberLike(tokens[e + 1].Key)))
        {
            e += needs ? 2 : 1;
        }

        if (e < n && tokens[e].Key == "#" && e + 1 < n)
        {
            e += 2;
        }

        return e < n ? e : -1;
    }

    private static int StationEnd(IReadOnlyList<AddressToken> tokens, int i)
    {
        if (i < tokens.Count && UsCaRules.CaDeliveryWords.TryGetValue(tokens[i].Key, out (string Standard, string Role) w) && w.Role is "station" or "outlet")
        {
            i++;
            if (i < tokens.Count)
            {
                i++;
            }
        }

        return i;
    }

    private static int BoxPhraseEnd(IReadOnlyList<AddressToken> tokens, int i, bool canada)
    {
        string a = i < tokens.Count ? tokens[i].Key : string.Empty;
        string b = i + 1 < tokens.Count ? tokens[i + 1].Key : string.Empty;
        string c = i + 2 < tokens.Count ? tokens[i + 2].Key : string.Empty;
        if ((a == "PO" && b == "BOX") || (a == "POST" && b == "OFFICE" && c == "BOX"))
        {
            return a == "POST" ? i + 3 : i + 2;
        }

        if (a == "POBOX")
        {
            return i + 1;
        }

        if (a == "P" && b == "O" && c == "BOX")
        {
            return i + 3;
        }

        if (a is "CALLER" or "BIN" or "LOCKBOX" or "DRAWER" or "APARTADO" && !canada)
        {
            return i + 1;
        }

        if (a == "FIRM" && b == "CALLER" && !canada)
        {
            return i + 2;
        }

        if (canada && (a == "CP" || (a == "CASE" && b == "POSTALE")))
        {
            return a == "CP" ? i + 1 : i + 2;
        }

        if (canada && a == "C" && b == "P")
        {
            return i + 2;
        }

        return 0;
    }

    private static bool IsAttachedRoute(string key) =>
        (key.StartsWith("RR", StringComparison.Ordinal) || key.StartsWith("HC", StringComparison.Ordinal)) && key.Length > 2 && key[2..].All(char.IsAsciiDigit);

    /// <summary>True for a word that can be a unit, box or route number: it holds a digit, or is one or two letters.</summary>
    internal static bool IsNumberLike(string key)
    {
        if (key.Length == 0 || key.Length > 12)
        {
            return false;
        }

        bool digit = false;
        foreach (char c in key)
        {
            if (char.IsAsciiDigit(c))
            {
                digit = true;
            }
            else if (!(char.IsLetter(c) || c is '-' or '/'))
            {
                return false;
            }
        }

        return digit || (key.Length <= 2 && key.All(char.IsLetter));
    }

    internal static bool IsFraction(string text)
    {
        int slash = text.IndexOf('/', StringComparison.Ordinal);
        return slash > 0 && slash < text.Length - 1 && text[..slash].All(char.IsAsciiDigit) && text[(slash + 1)..].All(char.IsAsciiDigit);
    }

    /// <summary>
    /// Publication 28 appendix D: house numbers include hyphenated ranges (<c>112-10</c>), alphanumeric combinations (<c>N6W23001</c>, <c>123A</c>) and
    /// grid numbers (<c>39.2</c>). Canada Post section 4.4 adds the unit prefix (<c>10-123</c>). Ordinal words (<c>5TH</c>) are street names, not numbers.
    /// </summary>
    internal static bool IsHouseNumber(string text, bool canada)
    {
        _ = canada;
        if (text.Length == 0 || text.Length > 14 || !char.IsLetterOrDigit(text[0]))
        {
            return false;
        }

        bool digit = false;
        foreach (char c in text)
        {
            if (char.IsAsciiDigit(c))
            {
                digit = true;
            }
            else if (!(char.IsAsciiLetter(c) || c is '-' or '.'))
            {
                return false;
            }
        }

        if (!digit)
        {
            return false;
        }

        if (char.IsAsciiLetter(text[0]) && !(text.Length > 1 && (char.IsAsciiDigit(text[1]) || (text[1] == '-' && text.Length > 2 && char.IsAsciiDigit(text[2])))))
        {
            return false;
        }

        return !IsOrdinal(text);
    }

    internal static bool IsOrdinal(string text)
    {
        string u = text.ToUpperInvariant();
        int d = 0;
        while (d < u.Length && char.IsAsciiDigit(u[d]))
        {
            d++;
        }

        if (d == 0 || d == u.Length)
        {
            return false;
        }

        string s = u[d..];
        return s is "ST" or "ND" or "RD" or "TH" or "RE" or "ERE" or "ÈRE" or "E" or "ER" or "EME" or "ÈME" or "IÈME" or "IEME" or "D" or "DE" or "ND" or "NDE";
    }

    private static bool IsSuffix(string key, bool canada, out string standard)
    {
        if (canada)
        {
            if (UsCaRules.CaStreetTypesEnglish.TryGetValue(key, out UsCaRules.CaStreetType t))
            {
                standard = t.Standard;
                return true;
            }
        }
        else if (UsCaRules.UsSuffixes.TryGetValue(key, out (string Standard, string Primary) s))
        {
            standard = s.Standard;
            return true;
        }

        standard = string.Empty;
        return false;
    }

    private static bool IsDirectional(string key, bool canada, out string abbreviation)
    {
        if (canada)
        {
            return UsCaRules.CaDirections.TryGetValue(key, out abbreviation!);
        }

        if (UsCaRules.UsDirectionals.TryGetValue(key, out (string Abbreviation, string Full) d))
        {
            abbreviation = d.Abbreviation;
            return true;
        }

        abbreviation = string.Empty;
        return false;
    }

    private static bool IsUnitDesignator(string key, bool canada, out bool needsRange)
    {
        if (canada)
        {
            needsRange = true;
            return UsCaRules.CaUnits.ContainsKey(key);
        }

        if (UsCaRules.UsUnits.TryGetValue(key, out (string Abbreviation, bool NeedsRange) u))
        {
            needsRange = u.NeedsRange;
            return true;
        }

        needsRange = false;
        return false;
    }

    private List<StreetLine>? Run()
    {
        return TryMilitary()
            ?? TryBox()
            ?? TryRoute()
            ?? TryGeneralDelivery()
            ?? TryAdditionalAddress()
            ?? TrySecondaryOnly()
            ?? TryStreet();
    }

    private static List<StreetLine> One(StreetLineKind kind, List<StreetPart> parts) => [new StreetLine(kind, parts)];

    private void Add(StreetPartRole role, int first, int last, string standard, string rule)
    {
        AddressToken a = _t[first];
        AddressToken b = _t[last];
        int start = a.Start;
        int end = b.Start + b.Length;
        string text = first == last ? a.Text : string.Join(' ', _t.Skip(first).Take(last - first + 1).Select(x => x.Text));
        _parts.Add(new StreetPart(role, text, standard, new SourceSpan(start, end - start), rule));
    }

    private void Add(StreetPartRole role, int index, string standard, string rule) => Add(role, index, index, standard, rule);

    private string Key(int i) => _t[i].Key;

    // Publication 28 section 238.1: CPR, OPC, PSC, UPR or UNIT with its assigned number, then BOX and a box number.
    private List<StreetLine>? TryMilitary()
    {
        if (_ca || _t.Count != 4)
        {
            return null;
        }

        string k = Key(0);
        if (k is "CPR" or "OPC" or "PSC" or "UPR" or "UNIT" && IsNumberLike(Key(1)) && Key(2) == "BOX" && IsNumberLike(Key(3)))
        {
            Add(StreetPartRole.MilitaryDesignator, 0, k, "pub28-238.1");
            Add(StreetPartRole.MilitaryNumber, 1, Key(1), "pub28-238.1");
            Add(StreetPartRole.MilitaryDesignator, 2, "BOX", "pub28-238.1");
            Add(StreetPartRole.MilitaryNumber, 3, Key(3), "pub28-238.1");
            return One(StreetLineKind.Military, _parts);
        }

        return null;
    }

    // Publication 28 section 281 to 283: PO BOX ##, with CALLER, FIRM CALLER, BIN, LOCKBOX and DRAWER changed to PO BOX, a leading hyphen of
    // the ZIP+4 file turned into a zero. Canada Post section 4.4: the box number, then the station information.
    private List<StreetLine>? TryBox()
    {
        int end = BoxPhraseEnd(_t, 0, _ca);
        if (end == 0 || end >= _t.Count)
        {
            return null;
        }

        string number = Key(end);
        if (!IsNumberLike(number) && !(number.Length > 1 && number[0] == '-' && IsNumberLike(number[1..])))
        {
            return null;
        }

        string designator = _ca && (Key(0) is "CP" or "C" or "CASE") ? "CP" : "PO BOX";
        string rule = _ca ? "canada-post-4.4-postal-box" : "pub28-281";
        Add(StreetPartRole.BoxDesignator, 0, end - 1, designator, _ca ? rule : (Key(0) is "PO" or "POST" or "POBOX" or "P" ? "pub28-281" : "pub28-283"));
        string standard = number;
        if (!_ca && number[0] == '-')
        {
            standard = "0" + number[1..];
        }

        Add(StreetPartRole.BoxNumber, end, standard, number[0] == '-' ? "pub28-282" : rule);
        int i = end + 1;
        if (_ca)
        {
            i = AddStation(i);
        }
        else if (i + 1 < _t.Count && Key(i) == "PMB" && IsNumberLike(Key(i + 1)))
        {
            Add(StreetPartRole.AdditionalDesignator, i, "PMB", "pub28-285");
            Add(StreetPartRole.AdditionalNumber, i + 1, Key(i + 1), "pub28-285");
            i += 2;
        }

        if (i < _t.Count)
        {
            Add(StreetPartRole.Other, i, _t.Count - 1, string.Join(' ', _t.Skip(i).Select(x => x.Key)), "unplaced");
        }

        return One(StreetLineKind.PostOfficeBox, _parts);
    }

    private int AddStation(int i)
    {
        if (i < _t.Count && UsCaRules.CaDeliveryWords.TryGetValue(Key(i), out (string Standard, string Role) w) && w.Role is "station" or "outlet")
        {
            Add(StreetPartRole.StationDesignator, i, w.Standard, "canada-post-4.4-station");
            i++;
            if (i < _t.Count)
            {
                Add(StreetPartRole.StationName, i, _t.Count - 1, string.Join(' ', _t.Skip(i).Select(x => x.Key)), "canada-post-4.4-station");
                i = _t.Count;
            }
        }

        return i;
    }

    // Publication 28 section 241 to 246 and 251 to 255: RR ## BOX ## and HC ## BOX ##; RFD, RD and RURAL ROUTE become RR, STAR ROUTE becomes HC,
    // leading zeros drop, extra words move to a line above. Canada Post section 4.4: RR followed by the route number and the station.
    private List<StreetLine>? TryRoute()
    {
        string k0 = Key(0);
        string k1 = _t.Count > 1 ? Key(1) : string.Empty;
        bool rural = false;
        bool highway = false;
        int i = 0;
        string number = string.Empty;
        int numberIndex = -1;
        int designatorEnd;
        if (IsAttachedRoute(k0))
        {
            rural = k0.StartsWith("RR", StringComparison.Ordinal);
            highway = !rural;
            number = k0[2..];
            designatorEnd = 0;
            i = 1;
        }
        else
        {
            if (k0 == "RR" || (k0 == "RURAL" && k1 == "ROUTE") || (!_ca && (k0 == "RFD" || (k0 == "RD" && _t.Count > 2 && IsNumberLike(k1) && k1.Any(char.IsAsciiDigit)))))
            {
                rural = true;
                i = k0 == "RURAL" ? 2 : 1;
            }
            else if (!_ca && (k0 == "HC" || (k0 == "STAR" && k1 == "ROUTE") || (k0 == "HIGHWAY" && k1 == "CONTRACT")))
            {
                highway = true;
                i = k0 == "HC" ? 1 : 2;
                if (k0 == "HIGHWAY" && i < _t.Count && Key(i) == "ROUTE")
                {
                    i++;
                }
            }
            else
            {
                return null;
            }

            designatorEnd = i - 1;
            if (i < _t.Count && Key(i) is "ROUTE" or "NUMBER" or "NO" or "#")
            {
                i++;
            }

            if (i >= _t.Count)
            {
                return null;
            }

            number = Key(i);
            numberIndex = i;
            i++;
        }

        if (!IsNumberLike(number))
        {
            return null;
        }

        string route = number.TrimStart('0');
        if (route.Length == 0)
        {
            route = "0";
        }

        string rule = _ca ? "canada-post-4.4-rural-route" : (rural ? "pub28-241" : "pub28-251");
        Add(StreetPartRole.RouteDesignator, 0, designatorEnd, rural ? "RR" : "HC", rural && k0 != "RR" ? "pub28-244" : (highway && k0 != "HC" && !IsAttachedRoute(k0) ? "pub28-253" : rule));
        if (numberIndex >= 0)
        {
            Add(StreetPartRole.RouteNumber, numberIndex, route, number.StartsWith('0') ? (rural ? "pub28-242" : "pub28-252") : rule);
        }
        else
        {
            // The number is glued to the designator (RR03): its span is the tail of that word.
            AddressToken t0 = _t[0];
            _parts.Add(new StreetPart(StreetPartRole.RouteNumber, number, route, new SourceSpan(t0.Start + 2, t0.Length - 2), number.StartsWith('0') ? (rural ? "pub28-242" : "pub28-252") : rule));
        }

        if (_ca)
        {
            i = AddStation(i);
            if (i < _t.Count)
            {
                return null;
            }

            return One(StreetLineKind.RuralRoute, _parts);
        }

        if (i < _t.Count && Key(i) is "BOX" or "BZN" or "BX" or "#")
        {
            int boxWord = i;
            i++;
            if (i < _t.Count && Key(i) is "#" or "NO" or "NUMBER")
            {
                i++;
            }

            if (i < _t.Count && IsNumberLike(Key(i)))
            {
                Add(StreetPartRole.BoxDesignator, boxWord, "BOX", Key(boxWord) == "BOX" ? rule : "pub28-244");
                Add(StreetPartRole.BoxNumber, i, Key(i), rule);
                i++;
            }
            else
            {
                return null;
            }

            if (i + 1 < _t.Count && Key(i) == "PMB" && IsNumberLike(Key(i + 1)))
            {
                Add(StreetPartRole.AdditionalDesignator, i, "PMB", "pub28-285");
                Add(StreetPartRole.AdditionalNumber, i + 1, Key(i + 1), "pub28-285");
                i += 2;
            }
        }
        else
        {
            return null;
        }

        List<StreetLine> lines = [];
        if (i < _t.Count)
        {
            // Sections 245 and 254: additional designations go above the delivery address line.
            lines.Add(new StreetLine(StreetLineKind.AdditionalAddress, [new StreetPart(StreetPartRole.Other, string.Join(' ', _t.Skip(i).Select(x => x.Text)), string.Join(' ', _t.Skip(i).Select(x => x.Key)), new SourceSpan(_t[i].Start, _t[^1].Start + _t[^1].Length - _t[i].Start), rural ? "pub28-245" : "pub28-254")]));
        }

        lines.Add(new StreetLine(rural ? StreetLineKind.RuralRoute : StreetLineKind.HighwayContractRoute, _parts));
        return lines;
    }

    // Publication 28 section 261: the words GENERAL DELIVERY, spelled out. Canada Post section 4.4: GD (French: PR) and the station.
    private List<StreetLine>? TryGeneralDelivery()
    {
        string k0 = Key(0);
        string k1 = _t.Count > 1 ? Key(1) : string.Empty;
        if (!_ca)
        {
            if (k0 == "GENERAL" && k1 == "DELIVERY" && _t.Count == 2)
            {
                Add(StreetPartRole.GeneralDelivery, 0, 1, "GENERAL DELIVERY", "pub28-261");
                return One(StreetLineKind.GeneralDelivery, _parts);
            }

            return null;
        }

        int i;
        if (k0 == "GD" || k0 == "PR")
        {
            i = 1;
        }
        else if ((k0 == "GENERAL" && k1 == "DELIVERY") || (k0 == "POSTE" && k1 == "RESTANTE"))
        {
            i = 2;
        }
        else
        {
            return null;
        }

        string standard = k0 is "PR" or "POSTE" ? "PR" : "GD";
        Add(StreetPartRole.GeneralDelivery, 0, i - 1, standard, "canada-post-4.4-general-delivery");
        i = AddStation(i);
        return i < _t.Count ? null : One(StreetLineKind.GeneralDelivery, _parts);
    }

    // Canada Post section 4.2: SITE and COMP (EMPL and COMP in French) stand above the rural route identifier.
    private List<StreetLine>? TryAdditionalAddress()
    {
        if (!_ca || !UsCaRules.CaDeliveryWords.TryGetValue(Key(0), out (string Standard, string Role) w) || w.Role != "site")
        {
            return null;
        }

        int i = 0;
        while (i < _t.Count)
        {
            if (UsCaRules.CaDeliveryWords.TryGetValue(Key(i), out (string Standard, string Role) d) && d.Role is "site" or "comp" && i + 1 < _t.Count && IsNumberLike(Key(i + 1)))
            {
                Add(StreetPartRole.AdditionalDesignator, i, d.Standard, "canada-post-4.2");
                Add(StreetPartRole.AdditionalNumber, i + 1, Key(i + 1), "canada-post-4.2");
                i += 2;
            }
            else
            {
                return null;
            }
        }

        return One(StreetLineKind.AdditionalAddress, _parts);
    }

    // Publication 28 section 213.3 and Canada Post section 4.4 (unit number, third place): the unit alone on the line above the street line.
    private List<StreetLine>? TrySecondaryOnly()
    {
        int i = ParseUnits(0, rest: false);
        return i == _t.Count && _parts.Count > 0 ? One(StreetLineKind.SecondaryOnly, _parts) : null;
    }

    /// <summary>Reads designator-and-number pairs from index <paramref name="i"/> to the end; returns the index reached.</summary>
    private int ParseUnits(int i, bool rest)
    {
        _ = rest;
        while (i < _t.Count)
        {
            string key = Key(i);
            if (key == "#" && i + 1 < _t.Count && IsNumberLike(Key(i + 1)))
            {
                // Publication 28 section 213.2: a pound sign is followed by a space and the number.
                Add(StreetPartRole.UnitDesignator, i, "#", "pub28-213.2");
                Add(StreetPartRole.UnitNumber, i + 1, Key(i + 1), "pub28-213.2");
                i += 2;
            }
            else if (IsUnitDesignator(key, _ca, out bool needs))
            {
                string standard = _ca ? UsCaRules.CaUnits[key] : UsCaRules.UsUnits[key].Abbreviation;
                string rule = _ca ? "canada-post-4.4-unit-designator" : "pub28-213.1";
                if (needs && i + 1 < _t.Count && IsNumberLike(Key(i + 1)))
                {
                    Add(StreetPartRole.UnitDesignator, i, standard, rule);
                    Add(StreetPartRole.UnitNumber, i + 1, Key(i + 1), rule);
                    i += 2;
                }
                else if (!needs)
                {
                    Add(StreetPartRole.UnitDesignator, i, standard, rule);
                    i++;
                }
                else
                {
                    break;
                }
            }
            else
            {
                break;
            }
        }

        return i;
    }

    private List<StreetLine>? TryStreet()
    {
        int i = 0;
        // Puerto Rico: the number may come last or after a bloque (Publication 28 section 295, appendix I5).
        if (_pr && PrNumberLast() is { } prLine)
        {
            return prLine;
        }

        bool hasNumber = false;
        if (IsHouseNumber(_t[0].Text, _ca))
        {
            hasNumber = true;
            string text = _t[0].Text;
            int hyphen = text.IndexOf('-', StringComparison.Ordinal);
            if (_ca && hyphen > 0 && hyphen < text.Length - 1 && text[..hyphen].All(char.IsAsciiLetterOrDigit) && IsHouseNumber(text[(hyphen + 1)..], true))
            {
                // Canada Post section 4.4: the unit number before the civic number, joined by a hyphen.
                _parts.Add(new StreetPart(StreetPartRole.UnitPrefix, text[..hyphen], text[..hyphen].ToUpperInvariant() + "-", new SourceSpan(_t[0].Start, hyphen), "canada-post-4.4-unit-number"));
                string civic = text[(hyphen + 1)..];
                _parts.Add(new StreetPart(StreetPartRole.HouseNumber, civic, civic.ToUpperInvariant(), new SourceSpan(_t[0].Start + hyphen + 1, civic.Length), "canada-post-4.4-civic-number"));
            }
            else
            {
                // Publication 28 appendix D: a hyphen in the address range is significant and stays; a hyphen between a letter and its number (PR) goes (appendix I).
                string standard = text.ToUpperInvariant();
                string rule = _ca ? "canada-post-4.4-civic-number" : "pub28-appendix-d";
                if (_pr && standard.Length > 2 && char.IsAsciiLetter(standard[0]) && standard[1] == '-' && char.IsAsciiDigit(standard[2]))
                {
                    standard = standard.Remove(1, 1);
                    rule = "pub28-appendix-i";
                }

                _parts.Add(new StreetPart(StreetPartRole.HouseNumber, text, standard, _t[0].Span, rule));
            }

            i = 1;
            if (i < _t.Count - 1 && IsFraction(_t[i].Text))
            {
                // Canada Post section 4.4: a fraction suffix is separated by one space; Publication 28 appendix D4.
                Add(StreetPartRole.Fraction, i, _t[i].Text, _ca ? "canada-post-4.4-civic-number-suffix" : "pub28-appendix-d4");
                i++;
            }
        }

        // Secondary units: the first designator that has a number after it (or stands alone at the end) after at least one street word.
        int unitStart = -1;
        for (int p = i + 1; p < _t.Count; p++)
        {
            string key = Key(p);
            if (key == "#" && p + 1 < _t.Count && IsNumberLike(Key(p + 1)))
            {
                unitStart = p;
                break;
            }

            if (IsUnitDesignator(key, _ca, out bool needs))
            {
                if ((needs && p + 1 < _t.Count && IsNumberLike(Key(p + 1))) || (!needs && p == _t.Count - 1))
                {
                    unitStart = p;
                    break;
                }
            }
        }

        int nameEnd = unitStart < 0 ? _t.Count : unitStart;
        if (nameEnd <= i)
        {
            return null;
        }

        List<AddressToken> name = _t.GetRange(i, nameEnd - i);
        // Without a house number only the line just above the city counts as a street line; a line higher up ("CASEY BROOKS") is a name.
        if (!hasNumber && !(_noNumberOk && name.Count >= 2 && name.Skip(1).Any(x => IsSuffix(x.Key, _ca, out _) || HighwayPrefixes.Contains(x.Key))))
        {
            return null;
        }

        List<StreetPart> street = _ca ? ParseCaStreetName(name) : ParseUsStreetName(name);
        _parts.AddRange(street);
        if (unitStart >= 0)
        {
            int end = ParseUnits(unitStart, rest: true);
            if (end < _t.Count)
            {
                _parts.Add(new StreetPart(StreetPartRole.Other, string.Join(' ', _t.Skip(end).Select(x => x.Text)), string.Join(' ', _t.Skip(end).Select(x => x.Key)), new SourceSpan(_t[end].Start, _t[^1].Start + _t[^1].Length - _t[end].Start), "unplaced"));
            }
        }

        return One(StreetLineKind.Street, _parts);
    }

    // Puerto Rico: Publication 28 section 295 and appendix I5. The number may come last ("CALLE 1 A17", "CALLE JULIO ENRIQUE VIZCARRONDO 150") or
    // as a bloque and number ("CALLE 19 BLQ 199 123"); the standard prints the number first ("A17 CALLE 1", "199-123 CALLE 19").
    private List<StreetLine>? PrNumberLast()
    {
        if (_t.Count < 3 || IsHouseNumber(_t[0].Text, false) || Key(0) is not ("CALLE" or "AVE" or "AVENIDA" or "CARR" or "CARRETERA" or "CAMINO" or "PASEO"))
        {
            return null;
        }

        int last = _t.Count - 1;
        int blq = -1;
        for (int p = 1; p < _t.Count - 2; p++)
        {
            if (Key(p) is "BLQ" or "BLOQUE" or "BL" && IsNumberLike(Key(p + 1)))
            {
                blq = p;
                break;
            }
        }

        string number;
        string numberText;
        SourceSpan numberSpan;
        List<AddressToken> rest;
        string rule = "pub28-appendix-i5-number-last";
        if (blq > 0)
        {
            int q = blq + 2;
            if (q < _t.Count && Key(q) is "NUM" or "NUMERO" or "NO")
            {
                q++;
            }

            if (q != last || !IsNumberLike(Key(q)))
            {
                return null;
            }

            number = Key(blq + 1) + "-" + Key(q);
            numberText = _t[blq + 1].Text + " " + _t[q].Text;
            numberSpan = new SourceSpan(_t[blq].Start, _t[q].Start + _t[q].Length - _t[blq].Start);
            rest = _t.GetRange(0, blq);
            rule = "pub28-appendix-i5-bloque";
        }
        else
        {
            string lk = Key(last);
            bool letterNumber = lk.Length > 1 && char.IsAsciiLetter(lk[0]) && lk.Skip(1).All(c => char.IsAsciiDigit(c) || c == '-' || char.IsAsciiLetter(c)) && lk.Any(char.IsAsciiDigit);
            bool plainAfterWord = lk.All(char.IsAsciiDigit) && Key(last - 1).All(char.IsAsciiLetter) && Key(last - 1).Length > 2;
            if (!letterNumber && !plainAfterWord)
            {
                return null;
            }

            number = letterNumber ? lk.Replace("-", string.Empty, StringComparison.Ordinal) : lk;
            numberText = _t[last].Text;
            numberSpan = _t[last].Span;
            rest = _t.GetRange(0, last);
        }

        _parts.Add(new StreetPart(StreetPartRole.HouseNumber, numberText, number, numberSpan, rule));
        _parts.AddRange(ParseUsStreetName(rest));
        return One(StreetLineKind.Street, _parts);
    }

    private static string Clean(string key, bool us) => us ? key.Replace('-', ' ') : key;

    private static readonly HashSet<string> HighwayPrefixes = new(StringComparer.Ordinal) { "COUNTY", "STATE", "US", "TOWNSHIP", "INTERSTATE", "FARM", "RANCH" };

    // Publication 28 sections 232 to 237: predirectional, street name, suffix and postdirectional, parsed from the right.
    private List<StreetPart> ParseUsStreetName(List<AddressToken> w)
    {
        int lo = 0;
        int hi = w.Count; // words [lo, hi) remain

        // Trailing directionals (233.22, 233.23).
        int trailing = 0;
        while (trailing < 2 && hi - lo - trailing >= 2 && IsUsDirectional(w[hi - 1 - trailing].Key, out _))
        {
            trailing++;
        }

        // Suffix (234): the last word before the directionals, with a street name before it. COUNTY ROAD, STATE HIGHWAY and the like are names (237).
        int s = hi - 1 - trailing;
        bool hasSuffix = s - lo >= 1 && UsCaRules.UsSuffixes.ContainsKey(w[s].Key) && !IsNumberOnly(w[s].Key)
            && !(s - lo >= 1 && HighwayPrefixes.Contains(w[s - 1].Key));
        if (!hasSuffix && trailing > 0)
        {
            trailing = 1;
        }

        StreetPart? post = null;
        if (trailing > 0)
        {
            IsUsDirectional(w[hi - 1].Key, out string last);
            string abbreviation = last;
            int words = 1;
            if (trailing == 2)
            {
                IsUsDirectional(w[hi - 2].Key, out string first);
                if (ShouldJoinDirectionals(first, last))
                {
                    abbreviation = first + last;
                    words = 2;
                }
            }

            post = Dir(StreetPartRole.PostDirectional, w, hi - words, hi - 1, abbreviation, words == 2 ? "pub28-233.23" : "pub28-233.22");
            hi -= words;
        }

        List<StreetPart> parts = [];
        StreetPart? suffix = null;
        if (hasSuffix && hi - lo >= 2)
        {
            UsCaRules.UsSuffixes.TryGetValue(w[hi - 1].Key, out (string Standard, string Primary) sx);
            suffix = new StreetPart(StreetPartRole.StreetType, w[hi - 1].Text, sx.Standard, w[hi - 1].Span, "pub28-234.1");
            hi--;
        }

        // A directional between the street name and the suffix is part of the name and spelled out (233.3).
        int nameHi = hi;
        string? spelledLast = null;
        if (suffix is not null && hi - lo >= 2 && IsUsDirectional(w[hi - 1].Key, out _))
        {
            spelledLast = UsCaRules.UsDirectionals.TryGetValue(w[hi - 1].Key, out (string Abbreviation, string Full) fd) ? fd.Full : w[hi - 1].Key;
            nameHi = hi - 1;
        }

        // Predirectional (233.21, 233.23): the first word, when a name remains after it; NORTH-SOUTH and EAST-WEST pairs keep the second word in the name.
        StreetPart? pre = null;
        int spellSecond = -1;
        if (nameHi - lo >= 2 && IsUsDirectional(w[lo].Key, out string p0))
        {
            int words = 1;
            if (nameHi - lo >= 3 && IsUsDirectional(w[lo + 1].Key, out string p1))
            {
                if (ShouldJoinDirectionals(p0, p1))
                {
                    p0 += p1;
                    words = 2;
                }
                else if (Opposing(p0, p1) || p0 == p1)
                {
                    spellSecond = lo + 1;
                }
            }

            pre = Dir(StreetPartRole.PreDirectional, w, lo, lo + words - 1, p0, words == 2 ? "pub28-233.23" : "pub28-233.21");
            lo += words;
        }

        if (pre is not null)
        {
            parts.Add(pre.Value);
        }

        // The name. Hyphens are not significant (232) and become spaces; the word before the suffix, when it is itself a suffix word, is spelled out (234.2).
        for (int p = lo; p < nameHi; p++)
        {
            string standard = Clean(w[p].Key, true);
            string rule = "pub28-232";
            if (suffix is not null && p == nameHi - 1 && nameHi - lo >= 2 && UsCaRules.UsSuffixes.TryGetValue(w[p].Key, out (string Standard, string Primary) sp) && !IsNumberOnly(w[p].Key))
            {
                standard = sp.Primary;
                rule = "pub28-234.2";
            }
            else if (p == spellSecond && UsCaRules.UsDirectionals.TryGetValue(w[p].Key, out (string Abbreviation, string Full) sd))
            {
                standard = sd.Full;
                rule = "pub28-233.23";
            }
            else if (suffix is null && nameHi - lo == 1 && post is not null && UsCaRules.UsSuffixes.TryGetValue(w[p].Key, out (string Standard, string Primary) one))
            {
                // "AVE E": a suffix word that is the whole street name is spelled out, the letter after it is an alphabet indicator.
                standard = one.Primary;
                rule = "pub28-233.3";
            }

            parts.Add(new StreetPart(StreetPartRole.StreetName, w[p].Text, standard, w[p].Span, rule));
        }

        if (spelledLast is not null)
        {
            parts.Add(new StreetPart(StreetPartRole.StreetName, w[nameHi].Text, spelledLast, w[nameHi].Span, "pub28-233.3"));
        }

        if (suffix is not null)
        {
            parts.Add(suffix.Value);
        }

        if (post is not null)
        {
            parts.Add(post.Value);
        }

        return parts;
    }

    private static bool IsNumberOnly(string key) => key.All(char.IsAsciiDigit);

    private bool IsUsDirectional(string key, out string abbreviation)
    {
        if (UsCaRules.UsDirectionals.TryGetValue(key, out (string Abbreviation, string Full) d))
        {
            abbreviation = d.Abbreviation;
            return true;
        }

        if (_pr && key is "NO" or "SO")
        {
            abbreviation = key;
            return true;
        }

        abbreviation = string.Empty;
        return false;
    }

    private static bool Opposing(string a, string b) => (a, b) is ("N", "S") or ("S", "N") or ("E", "W") or ("W", "E");

    // Two directional words in a row make one directional (N E is NE) unless they are opposites or the same direction.
    private static bool ShouldJoinDirectionals(string a, string b) => a.Length == 1 && b.Length == 1 && !Opposing(a, b) && a != b;

    private static StreetPart Dir(StreetPartRole role, List<AddressToken> w, int first, int last, string abbreviation, string rule)
    {
        AddressToken a = w[first];
        AddressToken b = w[last];
        string text = first == last ? a.Text : string.Join(' ', w.Skip(first).Take(last - first + 1).Select(x => x.Text));
        return new StreetPart(role, text, abbreviation, new SourceSpan(a.Start, b.Start + b.Length - a.Start), rule);
    }

    // Canada Post section 4.4: the English street type after the street name, the French type before it (after it when the name is an ordinal),
    // the street direction last, in abbreviated form.
    private static List<StreetPart> ParseCaStreetName(List<AddressToken> w)
    {
        List<StreetPart> parts = [];
        int lo = 0;
        int hi = w.Count;
        StreetPart? dir = null;
        if (hi - lo >= 2 && UsCaRules.CaDirections.TryGetValue(w[hi - 1].Key, out string? d) && (UsCaRules.CaStreetTypesEnglish.ContainsKey(w[hi - 2].Key) || UsCaRules.CaStreetTypesFrench.ContainsKey(w[hi - 2].Key) || hi - lo >= 3))
        {
            dir = new StreetPart(StreetPartRole.PostDirectional, w[hi - 1].Text, d, w[hi - 1].Span, "canada-post-4.4-street-direction");
            hi--;
        }

        StreetPart? type = null;
        bool typeBefore = false;
        if (hi - lo >= 2 && UsCaRules.CaStreetTypesEnglish.TryGetValue(w[hi - 1].Key, out UsCaRules.CaStreetType en))
        {
            type = new StreetPart(StreetPartRole.StreetType, w[hi - 1].Text, en.Standard, w[hi - 1].Span, "canada-post-4.4-english-street-type");
            hi--;
        }
        else if (hi - lo >= 2 && UsCaRules.CaStreetTypesFrench.TryGetValue(w[hi - 1].Key, out UsCaRules.CaStreetType frAfter) && IsOrdinal(w[hi - 2].Key))
        {
            type = new StreetPart(StreetPartRole.StreetType, w[hi - 1].Text, frAfter.Standard, w[hi - 1].Span, "canada-post-4.4-french-street-type");
            hi--;
        }
        else if (hi - lo >= 2 && UsCaRules.CaStreetTypesFrench.TryGetValue(w[lo].Key, out UsCaRules.CaStreetType fr))
        {
            type = new StreetPart(StreetPartRole.StreetType, w[lo].Text, fr.Standard, w[lo].Span, "canada-post-4.4-french-street-type");
            typeBefore = true;
            lo++;
        }

        if (typeBefore && type is not null)
        {
            parts.Add(type.Value);
        }

        for (int p = lo; p < hi; p++)
        {
            parts.Add(new StreetPart(StreetPartRole.StreetName, w[p].Text, w[p].Key, w[p].Span, "canada-post-4.4-street-name"));
        }

        if (!typeBefore && type is not null)
        {
            parts.Add(type.Value);
        }

        if (dir is not null)
        {
            parts.Add(dir.Value);
        }

        return parts;
    }
}
