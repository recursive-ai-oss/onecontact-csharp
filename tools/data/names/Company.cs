using System.Text;

namespace OneContact.Data.Names;

/// <summary>
/// Renders company names from register records and from our own templates. A register record gives the name (and for GLEIF the ELF code); the
/// legal form span comes from the ISO 20275 list, so labels do not depend on our rule parser.
/// </summary>
internal sealed class CompanyGen
{
    private static readonly (string Name, double Weight)[] WithForm =
    [
        ("register", 40), ("recased", 16), ("form-alt", 10), ("no-form", 9), ("article-added", 3), ("dba", 8), ("care-of", 5), ("punct-noise", 4),
    ];

    private static readonly (string Name, double Weight)[] WithoutForm =
    [
        ("register", 52), ("recased", 22), ("article-added", 6), ("dba", 12), ("care-of", 8),
    ];

    private static readonly double[] WithFormWeights = WithForm.Select(p => p.Weight).ToArray();
    private static readonly double[] WithoutFormWeights = WithoutForm.Select(p => p.Weight).ToArray();

    private static readonly string[] SmallWords = ["of", "and", "the", "de", "la", "du", "des", "et", "for", "in", "on", "at", "a", "an", "to", "&"];

    private static readonly (string Name, double Weight)[] SynthPatterns =
    [
        ("person-trade", 22), ("surname-and-sons", 8), ("surname-partners", 9), ("law-offices", 6), ("possessive-shop", 9), ("family-practice", 6),
        ("the-surname-group", 4), ("fr-prefix-surname", 12), ("fr-surname-et-fils", 5), ("numbered-quebec", 8), ("numbered-canada", 5), ("numbered-province", 6),
    ];

    private static readonly double[] SynthWeights = SynthPatterns.Select(p => p.Weight).ToArray();

    private readonly ElfList _elf;
    private readonly Templates _templates;
    private readonly PersonGen _persons;
    private readonly List<string> _tradeNames = [];

    internal CompanyGen(ElfList elf, Templates templates, PersonGen persons)
    {
        _elf = elf;
        _templates = templates;
        _persons = persons;
    }

    /// <summary>Names without a legal form, drawn from the records read so far, for trading-as and care-of parts.</summary>
    internal void RememberTradeName(string baseName)
    {
        if (_tradeNames.Count < 200_000 && baseName.Length <= 40 && !baseName.Any(char.IsDigit) && !HasTrustWord(baseName) && !LooksLikePerson(baseName))
        {
            _tradeNames.Add(baseName);
        }
    }

    private static readonly HashSet<string> TrustWords = new(StringComparer.Ordinal)
    {
        "trust", "trustees", "trustee", "tr", "ttee", "settlement", "will", "scheme", "estate", "revocable", "irrevocable", "rev", "dtd", "fbo", "ua", "uad", "uw",
    };

    /// <summary>True for names of personal trusts and settlements ("The A B Smith Discretionary Trust"), which carry private persons' names and are left out.</summary>
    private static bool HasTrustWord(string name)
    {
        foreach (string t in name.Split(' '))
        {
            if (TrustWords.Contains(Txt.Fold(t)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the text is one to three words that are all given or family names in the pools: indistinguishable from a person's name.</summary>
    private bool LooksLikePerson(string name)
    {
        string[] tokens = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is 0 or > 3)
        {
            return false;
        }

        foreach (string t in tokens)
        {
            if (!_persons.IsPersonToken(Txt.Fold(t)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Splits a register name into leading article, base name, joiner and legal form; null when the register's code cannot be placed in the name.</summary>
    internal bool TryDecompose(CompanyRecord rec, out string? article, out string baseName, out string joiner, out string? form, out string? code, out string reason)
    {
        article = null;
        baseName = rec.Name;
        joiner = "";
        form = null;
        code = null;
        reason = "";
        string country2 = rec.Jurisdiction.Length >= 2 ? rec.Jurisdiction[..2] : rec.Jurisdiction;
        bool realCode = rec.Code is not null && rec.Code is not ("8888" or "9999") && _elf.HasCode(rec.Code);
        FormMatch? m;
        if (realCode)
        {
            m = ElfList.FindTail(rec.Name, _elf.FormsOf(rec.Code!), skipGeneric: false) ?? ElfList.FindTail(rec.Name, _elf.FormsOfCountry(country2), skipGeneric: true);
            if (m == null)
            {
                reason = "form-not-located";
                return false;
            }

            code = m.Value.Codes.Contains(rec.Code!) ? rec.Code : m.Value.Codes.Count == 1 ? m.Value.Codes[0] : null;
        }
        else
        {
            m = ElfList.FindTail(rec.Name, _elf.FormsOfCountry(country2), skipGeneric: true);
            code = m is { } mm && mm.Codes.Count == 1 ? mm.Codes[0] : null;
        }

        if (m is { } match)
        {
            string head = rec.Name[..match.Start];
            string trimmed = head.TrimEnd(' ', ',', ';');
            if (trimmed.Length == 0 || !trimmed.Any(char.IsLetterOrDigit))
            {
                reason = "empty-base";
                return false;
            }

            baseName = trimmed;
            joiner = head[trimmed.Length..];
            form = match.Text;
        }

        if (baseName.Length > 4 && baseName.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
        {
            string rest = baseName[4..].TrimStart();
            if (rest.Length > 0)
            {
                article = baseName[..3];
                baseName = rest;
            }
        }

        if (!realCode && HasTrustWord(rec.Name))
        {
            reason = "trust-like";
            return false;
        }

        if (!realCode && form == null && LooksLikePerson(baseName))
        {
            reason = "person-like-without-form";
            return false;
        }

        return true;
    }

    internal Example? FromRecord(CompanyRecord rec, Prng r, string id, out string skipReason)
    {
        skipReason = "";
        if (!TryDecompose(rec, out string? article, out string baseName, out string joiner, out string? form, out string? code, out skipReason))
        {
            return null;
        }

        string country2 = rec.Jurisdiction.Length >= 2 ? rec.Jurisdiction[..2] : rec.Jurisdiction;
        bool upperSource = Txt.IsAllUpper(rec.Name);
        bool hasForm = form != null;
        string pattern = hasForm ? WithForm[r.Weighted(WithFormWeights)].Name : WithoutForm[r.Weighted(WithoutFormWeights)].Name;
        if ((pattern == "article-added" && article != null) || (pattern == "no-form" && LooksLikePerson(baseName)))
        {
            pattern = "register";
        }

        // Case mode: 0 as in the register, 1 upper, 2 lower, 3 title.
        int caseMode = 0;
        if (pattern == "recased")
        {
            double x = r.Unit();
            caseMode = upperSource ? (x < 0.75 ? 3 : 2) : (x < 0.45 ? 3 : x < 0.75 ? 1 : 2);
            if (!upperSource && caseMode == 3)
            {
                caseMode = 1;
            }
        }

        string caseName = caseMode switch { 1 => "upper", 2 => "lower", 3 => "title", _ => "" };
        Builder b = new();
        if (article != null)
        {
            b.Add("LeadingArticle", Case(article, caseMode, "article")).Raw(" ");
        }
        else if (pattern == "article-added")
        {
            b.Add("LeadingArticle", Case(upperSource ? "THE" : "The", caseMode, "article")).Raw(" ");
        }

        b.Add("Base", Case(baseName, caseMode, "base"));
        if (hasForm && pattern != "no-form")
        {
            string text = form!;
            string j = joiner;
            if (pattern == "form-alt")
            {
                Form? alt = AlternativeForm(rec, code, form!, r);
                if (alt != null)
                {
                    text = upperSource ? alt.Text.ToUpperInvariant() : alt.Text;
                    if (r.Chance(0.25))
                    {
                        j = j.Contains(',', StringComparison.Ordinal) ? " " : ", ";
                    }
                }
                else
                {
                    pattern = "register";
                }
            }
            else if (pattern == "punct-noise")
            {
                j = r.Pick(new[] { ",", " , ", "  ", ", ", " " });
            }

            b.Raw(j).Add("LegalForm", Case(text, caseMode, "form"));
        }

        if (pattern == "dba" && _tradeNames.Count > 0)
        {
            string marker = DbaMarker(r, country2, caseMode);
            b.Raw(" " + marker + " ").Add("TradingAs", Case(r.Pick(_tradeNames), caseMode == 0 && upperSource ? 1 : caseMode, "base"));
        }
        else if (pattern == "dba")
        {
            pattern = "register";
        }

        if (pattern == "care-of")
        {
            string marker = r.Pick(new[] { "c/o", "C/O", "c/o", "care of" });
            if (caseMode == 1)
            {
                marker = marker.ToUpperInvariant();
            }
            else if (caseMode == 2)
            {
                marker = marker.ToLowerInvariant();
            }

            string target;
            if (r.Chance(0.65))
            {
                (string lang, _) = PersonGen.PickLocale(r);
                target = _persons.Given(r, lang).Name + " " + _persons.Family(r, lang);
            }
            else
            {
                target = _tradeNames.Count > 0 ? r.Pick(_tradeNames) : "Acme";
            }

            b.Raw(r.Chance(0.25) ? ", " : " ").Raw(marker).Raw(" ").Add("CareOf", Case(target, caseMode == 0 && upperSource ? 1 : caseMode, "base"));
        }

        string full = b.Text;
        int bucket = Txt.Bucket(Txt.Fold(baseName));
        return new Example
        {
            Id = id,
            Text = full,
            Class = "company",
            Pattern = pattern,
            Noise = caseName,
            Lang = rec.Lang,
            Region = country2,
            Source = rec.Source,
            Elf = hasForm && pattern != "no-form" ? code : null,
            Split = bucket < 90 ? "train" : bucket < 95 ? "dev" : "reserved",
            Parts = b.Parts,
        };
    }

    private Form? AlternativeForm(CompanyRecord rec, string? code, string current, Prng r)
    {
        if (code == null)
        {
            return null;
        }

        List<Form> options = [];
        foreach (Form f in _elf.FormsOf(code))
        {
            if (!string.Equals(f.Text, current, StringComparison.OrdinalIgnoreCase))
            {
                options.Add(f);
            }
        }

        _ = rec;
        return options.Count == 0 ? null : options[r.Int(options.Count)];
    }

    private static string DbaMarker(Prng r, string country2, int caseMode)
    {
        string[] options = country2 is "GB" or "IE" ? ["t/a", "trading as", "T/A"] : ["dba", "d/b/a", "DBA", "doing business as"];
        string m = r.Pick(options);
        return caseMode == 1 ? m.ToUpperInvariant() : caseMode == 2 ? m.ToLowerInvariant() : m;
    }

    /// <summary>Re-cases a piece: 0 as given, 1 upper, 2 lower, 3 title (small words and short consonant-only tokens such as "LLC" kept as they are).</summary>
    private static string Case(string s, int mode, string role)
    {
        switch (mode)
        {
            case 1:
                return Txt.MapChars(s, upper: true);
            case 2:
                return Txt.MapChars(s, upper: false);
            case 3:
                return role == "article" ? "The" : TitleCase(s, role == "form");
            default:
                return s;
        }
    }

    private static string TitleCase(string s, bool isForm)
    {
        string[] tokens = s.Split(' ');
        StringBuilder sb = new(s.Length);
        for (int i = 0; i < tokens.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            string t = tokens[i];
            if (t.Length == 0)
            {
                continue;
            }

            if (t.Any(char.IsLower) || t.Any(char.IsDigit) || (t.Contains('&', StringComparison.Ordinal) && t.Length > 1))
            {
                sb.Append(t);
            }
            else if (isForm && t is "LTD" or "LTD.")
            {
                sb.Append(t.Length == 4 ? "Ltd." : "Ltd");
            }
            else if (!isForm && i > 0 && Array.IndexOf(SmallWords, Txt.MapChars(t, upper: false)) >= 0)
            {
                sb.Append(Txt.MapChars(t, upper: false));
            }
            else if (t.Length <= 3 && !t.Any(c => "AEIOUYaeiouy".Contains(c, StringComparison.Ordinal)))
            {
                sb.Append(t);
            }
            else
            {
                sb.Append(Txt.NameCase(t));
            }
        }

        return sb.ToString();
    }

    // ---- synthesized company names ------------------------------------------------------------------------------------

    internal Example Synthesize(Prng r, string id)
    {
        string pattern = SynthPatterns[r.Weighted(SynthWeights)].Name;
        bool french = pattern.StartsWith("fr-", StringComparison.Ordinal) || pattern.StartsWith("numbered-quebec", StringComparison.Ordinal);
        string lang = french ? "fr" : "en";
        string region;
        if (french || pattern.StartsWith("numbered-", StringComparison.Ordinal))
        {
            region = "CA";
        }
        else
        {
            double x = r.Unit();
            region = x < 0.7 ? "US" : x < 0.9 ? "CA" : "GB";
        }

        string formCountry = french ? "FR" : region;
        Builder b = new();
        bool formal = false;
        string family = _persons.Family(r, lang == "fr" ? "fr" : "en");
        switch (pattern)
        {
            case "person-trade":
                {
                    string given = _persons.Given(r, lang).Name;
                    b.Add("Base", given + " " + family + " " + r.Pick(_templates.List("trade", lang)));
                    formal = r.Chance(0.45);
                    break;
                }

            case "surname-and-sons":
                b.Add("Base", family + " & Sons");
                formal = r.Chance(0.5);
                break;
            case "surname-partners":
                {
                    string kind = r.Pick(new[] { "family-and-family", "triple", "associates" });
                    if (kind == "family-and-family")
                    {
                        b.Add("Base", family + " & " + _persons.Family(r, "en") + " " + r.Pick(new[] { "Associates", "Partners", "Group", "Consulting" }));
                    }
                    else if (kind == "triple")
                    {
                        b.Add("Base", family + ", " + _persons.Family(r, "en") + " & " + _persons.Family(r, "en"));
                    }
                    else
                    {
                        b.Add("Base", family + " & Associates");
                    }

                    formal = r.Chance(0.75);
                    break;
                }

            case "law-offices":
                b.Add("Base", "Law Offices of " + _persons.Given(r, "en").Name + " " + family);
                formal = r.Chance(0.35);
                break;
            case "possessive-shop":
                b.Add("Base", family + "'s " + r.Pick(_templates.List("shop", "en")));
                formal = r.Chance(0.3);
                break;
            case "family-practice":
                b.Add("Base", family + " Family " + r.Pick(_templates.List("practice", "en")));
                formal = r.Chance(0.5);
                break;
            case "the-surname-group":
                b.Add("LeadingArticle", "The").Raw(" ").Add("Base", family + " " + r.Pick(new[] { "Group", "Partnership", "Collection", "Agency" }));
                formal = r.Chance(0.2);
                break;
            case "fr-prefix-surname":
                b.Add("Base", r.Pick(_templates.List("frprefix", "fr")) + " " + family);
                formal = r.Chance(0.9);
                break;
            case "fr-surname-et-fils":
                b.Add("Base", family + " " + (r.Chance(0.6) ? "et Fils" : "& Fils"));
                formal = r.Chance(0.85);
                break;
            case "numbered-quebec":
                b.Add("Base", Digits(r, 4) + "-" + Digits(r, 4) + " Qu\u00E9bec");
                formal = true;
                break;
            case "numbered-canada":
                b.Add("Base", Digits(r, 7) + " " + "Canada");
                formal = true;
                break;
            case "numbered-province":
                b.Add("Base", Digits(r, 7) + " " + r.Pick(new[] { "Ontario", "Alberta", "B.C.", "Manitoba", "Saskatchewan", "Nova Scotia" }));
                formal = true;
                break;
            default:
                throw new InvalidOperationException(pattern);
        }

        string? code = null;
        if (formal)
        {
            string country = pattern.StartsWith("numbered-quebec", StringComparison.Ordinal) ? "FR" : formCountry;
            string formText = _templates.Forms[country].Pick(r);
            b.Raw(r.Chance(0.12) ? "," + " " : " ").Add("LegalForm", formText);
            FormMatch? m = ElfList.FindTail("x " + formText, _elf.FormsOfCountry(region == "GB" ? "GB" : region == "US" ? "US" : "CA"), skipGeneric: true);
            code = m is { } mm && mm.Codes.Count == 1 ? mm.Codes[0] : null;
        }

        string baseText = b.Parts.First(p => p.Label == "Base") is { } bp ? b.Text.Substring(bp.Start, bp.End - bp.Start) : "";
        int bucket = Txt.Bucket(Txt.Fold(baseText));
        return new Example
        {
            Id = id,
            Text = b.Text,
            Class = "company",
            Pattern = pattern,
            Lang = lang,
            Region = region,
            Source = "templates",
            Elf = code,
            Split = bucket < 90 ? "train" : bucket < 95 ? "dev" : "reserved",
            Parts = b.Parts,
        };
    }

    private static string Digits(Prng r, int n)
    {
        StringBuilder sb = new(n);
        sb.Append((char)('1' + r.Int(9)));
        for (int i = 1; i < n; i++)
        {
            sb.Append((char)('0' + r.Int(10)));
        }

        return sb.ToString();
    }
}
