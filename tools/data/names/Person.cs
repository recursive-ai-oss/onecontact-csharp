using System.Text;

namespace OneContact.Data.Names;

/// <summary>
/// Renders person names, several-people strings and non-names. Every pattern assembles parts from independently drawn given and family
/// names, so each part's span is known by construction (no parser decides a label).
/// </summary>
internal sealed class PersonGen
{
    private static readonly (string Name, double En, double Fr, double Es)[] SinglePatterns =
    [
        ("gf", 30, 30, 14), ("gmf", 11, 11, 7), ("gif", 10, 8, 3), ("if", 3, 3, 1), ("iif", 2, 1, 0.5),
        ("fg", 8, 8, 3), ("fgm", 3, 3, 1), ("fgi", 2, 1, 0.5),
        ("tgf", 7, 8, 5), ("tf", 2, 2, 1), ("tgmf", 1.5, 1, 1),
        ("gfs", 3, 0.5, 0), ("gfp", 2.5, 2.5, 0.5), ("tgfp", 0.8, 0.8, 0), ("fgs", 0.5, 0, 0),
        ("gnf", 2.5, 2, 0.5), ("gnf-paren", 1.5, 1.5, 0.3),
        ("gpf", 3.5, 3.5, 1), ("fgp", 0.7, 0.7, 0), ("gf-hyphen", 2, 2, 0.5), ("full", 0.5, 0.4, 0),
        ("gfsf", 0, 0, 40), ("gmfsf", 0, 0, 14), ("tgfsf", 0, 0, 8), ("fsfg", 0, 0, 5),
    ];

    private static readonly (string Name, double Weight)[] MultiplePatterns =
    [
        ("mr-mrs-gf", 24), ("mr-mrs-f", 10), ("g-and-g-f", 22), ("gf-and-gf", 16), ("gf-semicolon-gf", 8), ("list-g-g-g-f", 6), ("dr-and-dr", 4),
    ];

    private static readonly (string Name, double Weight)[] UnknownPatterns =
    [
        ("job-title", 30), ("department", 7), ("generic", 4), ("bare-given", 14), ("bare-family", 12), ("title-only", 2), ("digits", 2), ("email", 8), ("long-text", 12),
    ];

    private static readonly double[] SingleEn = SinglePatterns.Select(p => p.En).ToArray();
    private static readonly double[] SingleFr = SinglePatterns.Select(p => p.Fr).ToArray();
    private static readonly double[] SingleEs = SinglePatterns.Select(p => p.Es).ToArray();
    private static readonly double[] MultipleWeights = MultiplePatterns.Select(p => p.Weight).ToArray();
    private static readonly double[] UnknownWeights = UnknownPatterns.Select(p => p.Weight).ToArray();

    // Quotes around a nickname. The rule parser reads straight and curly double quotes, guillemets and parentheses.
    private static readonly (string Open, string Close, double Weight)[] NickQuotes =
        [("\"", "\"", 55), ("(", ")", 25), ("\u201C", "\u201D", 12), ("\u00AB ", " \u00BB", 0)];

    private static readonly Dictionary<string, string> DottedProfessional = new(StringComparer.Ordinal)
    {
        ["PhD"] = "Ph.D.", ["MD"] = "M.D.", ["DDS"] = "D.D.S.", ["DMD"] = "D.M.D.", ["DVM"] = "D.V.M.", ["JD"] = "J.D.", ["MBA"] = "M.B.A.",
        ["CPA"] = "C.P.A.", ["RN"] = "R.N.", ["PE"] = "P.E.", ["LLM"] = "LL.M.", ["DC"] = "D.C.", ["CFA"] = "C.F.A.", ["PMP"] = "P.M.P.", ["Esq"] = "Esq.",
        ["MSc"] = "M.Sc.", ["MPH"] = "M.P.H.",
    };

    private readonly NamePools _pools;
    private readonly Templates _templates;

    internal PersonGen(NamePools pools, Templates templates)
    {
        _pools = pools;
        _templates = templates;
    }

    // ---- name draws -------------------------------------------------------------------------------------------------

    internal static (string Lang, string Region) PickLocale(Prng r)
    {
        double x = r.Unit() * 100;
        if (x < 64)
        {
            return ("en", "US");
        }

        if (x < 78)
        {
            return ("en", "CA");
        }

        return x < 95 ? ("fr", "CA") : ("es", "US");
    }

    internal GivenName Given(Prng r, string lang)
    {
        switch (lang)
        {
            case "es":
                return _pools.EsGiven.Pick(r);
            case "fr":
                GivenName g = r.Chance(0.55) ? _pools.FrGivenQuebec.Pick(r) : _pools.FrGivenInsee.Pick(r);
                if (r.Chance(0.12))
                {
                    GivenName second = r.Chance(0.55) ? _pools.FrGivenQuebec.Pick(r) : _pools.FrGivenInsee.Pick(r);
                    if (!second.Name.Contains('-', StringComparison.Ordinal) && !g.Name.Contains('-', StringComparison.Ordinal))
                    {
                        return new GivenName(g.Name + "-" + second.Name, g.Sex);
                    }
                }

                return g;
            default:
                return _pools.EnGiven.Pick(r);
        }
    }

    internal string Family(Prng r, string lang)
    {
        switch (lang)
        {
            case "es":
                return _pools.EsFamily.Pick(r);
            case "fr":
                double x = r.Unit();
                return x < 0.45 ? _pools.FrFamilyInsee.Pick(r) : x < 0.80 ? _pools.FrFamilyQuebec.Pick(r) : _pools.EnFamily.Pick(r);
            default:
                return _pools.EnFamily.Pick(r);
        }
    }

    private string PlainFamily(Prng r, string lang)
    {
        string f = Family(r, lang);
        for (int i = 0; i < 8 && Builder.ParticleLength(f) > 0; i++)
        {
            f = Family(r, lang);
        }

        return f;
    }

    internal bool IsPersonToken(string foldedToken) => _pools.PersonTokens.Contains(foldedToken);

    private string Middle(Prng r, string lang, char sex)
    {
        // A middle name is another given name; for es the second part of a compound given name.
        for (int i = 0; i < 4; i++)
        {
            GivenName m = Given(r, lang);
            if (m.Name.Contains('-', StringComparison.Ordinal))
            {
                continue;
            }

            if (lang == "es" || m.Sex == sex || r.Chance(0.25))
            {
                return m.Name;
            }
        }

        return Given(r, lang).Name.Split('-')[0];
    }

    // ---- style choices shared by the renderers ----------------------------------------------------------------------

    private sealed class Style
    {
        internal string Space = " ";
        internal string Comma = ", ";
        internal bool Dots;
        internal bool TitleDots;
        internal int CaseMode; // 0 as written, 1 upper, 2 lower
    }

    private static Style PickStyle(Prng r, string lang, string region)
    {
        Style s = new()
        {
            Dots = r.Chance(0.8),
            TitleDots = lang == "en" && r.Chance(region == "US" ? 0.65 : 0.4),
        };
        double x = r.Unit();
        s.Space = x < 0.985 ? " " : "  ";
        double y = r.Unit();
        s.Comma = y < 0.965 ? ", " : y < 0.985 ? "," : " , ";
        double z = r.Unit();
        s.CaseMode = z < 0.86 ? 0 : z < 0.93 ? 1 : 2;
        return s;
    }

    private string TitleText(Prng r, string lang, char sex, Style st)
    {
        WeightedList<Title> list = _templates.Titles[lang];
        Title t = list.Pick(r);
        for (int i = 0; i < 6 && ((t.Sex == 'M' && sex == 'F') || (t.Sex == 'F' && sex == 'M')); i++)
        {
            t = list.Pick(r);
        }

        return t.DotOptional && st.TitleDots ? t.Text + "." : t.Text;
    }

    private static string Initial(string name, Style st)
    {
        char c = char.ToUpperInvariant(name[0]);
        return st.Dots ? c + "." : c.ToString();
    }

    private string Particle(Prng r, string lang, bool capital)
    {
        string p = _templates.Particles[lang].Pick(r);
        return capital && lang == "en" ? Txt.NameCase(p) : p;
    }

    private string GenerationalText(Prng r, string lang, Style st)
    {
        string s = _templates.Generational[lang].Pick(r).Text;
        return st.Dots && s is "Jr" or "Sr" ? s + "." : s;
    }

    private string ProfessionalText(Prng r, string lang, Style st)
    {
        string s = _templates.Professional[lang].Pick(r).Text;
        if (st.Dots && DottedProfessional.TryGetValue(s, out string? dotted))
        {
            return dotted;
        }

        return s;
    }

    // ---- single persons ---------------------------------------------------------------------------------------------

    /// <summary>Renders one person. The result's <see cref="Example.Split"/> is "reserved" when its text falls in the evaluation reserve.</summary>
    internal Example Single(Prng r, string id)
    {
        (string lang, string region) = PickLocale(r);
        Style st = PickStyle(r, lang, region);
        double[] weights = lang == "en" ? SingleEn : lang == "fr" ? SingleFr : SingleEs;
        string pattern = SinglePatterns[r.Weighted(weights)].Name;

        GivenName g = Given(r, lang);
        string family = Family(r, lang);
        Builder b = new();
        string sp = st.Space;

        switch (pattern)
        {
            case "gf":
                b.Add("Given", g.Name).Raw(sp).Family(family);
                break;
            case "gmf":
                b.Add("Given", g.Name).Raw(sp).Add("Middle", Middle(r, lang, g.Sex)).Raw(sp).Family(family);
                break;
            case "gif":
                b.Add("Given", g.Name).Raw(sp).Add("Middle", Initial(Middle(r, lang, g.Sex), st)).Raw(sp).Family(family);
                break;
            case "if":
                b.Add("Given", Initial(g.Name, st)).Raw(sp).Family(family);
                break;
            case "iif":
                b.Add("Given", Initial(g.Name, st)).Raw(sp).Add("Middle", Initial(Middle(r, lang, g.Sex), st)).Raw(sp).Family(family);
                break;
            case "fg":
                b.Family(family).Raw(st.Comma).Add("Given", g.Name);
                break;
            case "fgm":
                b.Family(family).Raw(st.Comma).Add("Given", g.Name).Raw(sp).Add("Middle", Middle(r, lang, g.Sex));
                break;
            case "fgi":
                b.Family(family).Raw(st.Comma).Add("Given", g.Name).Raw(sp).Add("Middle", Initial(Middle(r, lang, g.Sex), st));
                break;
            case "tgf":
                b.Add("Prefix", TitleText(r, lang, g.Sex, st)).Raw(sp).Add("Given", g.Name).Raw(sp).Family(family);
                break;
            case "tf":
                b.Add("Prefix", TitleText(r, lang, '-', st)).Raw(sp).Family(family);
                break;
            case "tgmf":
                b.Add("Prefix", TitleText(r, lang, g.Sex, st)).Raw(sp).Add("Given", g.Name).Raw(sp).Add("Middle", Middle(r, lang, g.Sex)).Raw(sp).Family(family);
                break;
            case "gfs":
                b.Add("Given", g.Name).Raw(sp).Family(family).Raw(r.Chance(0.3) ? st.Comma : sp).Add("Suffix", GenerationalText(r, lang, st));
                break;
            case "gfp":
                b.Add("Given", g.Name).Raw(sp).Family(family).Raw(r.Chance(0.88) ? st.Comma : sp).Add("ProfessionalSuffix", ProfessionalText(r, lang, st));
                break;
            case "tgfp":
                b.Add("Prefix", TitleText(r, lang, g.Sex, st)).Raw(sp).Add("Given", g.Name).Raw(sp).Family(family).Raw(st.Comma).Add("ProfessionalSuffix", ProfessionalText(r, lang, st));
                break;
            case "fgs":
                b.Family(family).Raw(st.Comma).Add("Given", g.Name).Raw(r.Chance(0.5) ? st.Comma : sp).Add("Suffix", GenerationalText(r, lang, st));
                break;
            case "gnf":
            case "gnf-paren":
                {
                    string? nick = Nickname(r, g.Name);
                    if (nick == null)
                    {
                        pattern = "gf";
                        b.Add("Given", g.Name).Raw(sp).Family(family);
                        break;
                    }

                    (string open, string close, _) = pattern == "gnf-paren" ? NickQuotes[1] : NickQuotes[r.Chance(0.2) ? 2 : 0];
                    b.Add("Given", g.Name).Raw(sp).Raw(open).Add("Nickname", nick).Raw(close).Raw(sp).Family(family);
                    break;
                }

            case "gpf":
                family = PlainFamily(r, lang);
                b.Add("Given", g.Name).Raw(sp).Add("Particle", Particle(r, lang, r.Chance(0.65))).Raw(sp).Family(family);
                break;
            case "fgp":
                family = PlainFamily(r, lang);
                b.Family(family).Raw(st.Comma).Add("Given", g.Name).Raw(sp).Add("Particle", Particle(r, lang, false));
                break;
            case "gf-hyphen":
                {
                    string second = Family(r, lang);
                    for (int i = 0; i < 6 && string.Equals(second, family, StringComparison.Ordinal); i++)
                    {
                        second = Family(r, lang);
                    }

                    b.Add("Given", g.Name).Raw(sp).Family(family + "-" + second);
                    break;
                }
            case "full":
                b.Add("Prefix", TitleText(r, lang, g.Sex, st)).Raw(sp).Add("Given", g.Name).Raw(sp).Add("Middle", Middle(r, lang, g.Sex)).Raw(sp)
                    .Family(family).Raw(sp).Add("Suffix", GenerationalText(r, lang, st)).Raw(st.Comma).Add("ProfessionalSuffix", ProfessionalText(r, lang, st));
                break;
            case "gfsf":
                b.Add("Given", g.Name).Raw(sp).Family(family).Raw(sp).Add("SecondFamily", Family(r, lang));
                break;
            case "gmfsf":
                b.Add("Given", g.Name).Raw(sp).Add("Middle", Middle(r, lang, g.Sex)).Raw(sp).Family(family).Raw(sp).Add("SecondFamily", Family(r, lang));
                break;
            case "tgfsf":
                b.Add("Prefix", TitleText(r, lang, g.Sex, st)).Raw(sp).Add("Given", g.Name).Raw(sp).Family(family).Raw(sp).Add("SecondFamily", Family(r, lang));
                break;
            case "fsfg":
                b.Family(family).Raw(sp).Add("SecondFamily", Family(r, lang)).Raw(st.Comma).Add("Given", g.Name);
                break;
            default:
                throw new InvalidOperationException(pattern);
        }

        return Finish(id, b, "person", pattern, lang, region, st.CaseMode, "name-lists", noAccents: lang == "fr" && r.Chance(0.02));
    }

    private string? Nickname(Prng r, string given)
    {
        string key = Txt.MapChars(given, upper: false);
        return _pools.Nicknames.TryGetValue(key, out string[]? nicks) ? Txt.NameCase(nicks[r.Int(nicks.Length)]) : null;
    }

    // ---- several people ---------------------------------------------------------------------------------------------

    internal Example Multiple(Prng r, string id)
    {
        double x = r.Unit() * 100;
        (string lang, string region) = x < 74 ? ("en", x < 52 ? "US" : "CA") : x < 94 ? ("fr", "CA") : ("es", "US");
        Style st = PickStyle(r, lang, region);
        string pattern = MultiplePatterns[r.Weighted(MultipleWeights)].Name;
        string sp = st.Space;
        string conn = lang switch
        {
            "fr" => r.Chance(0.15) ? "&" : "et",
            "es" => "y",
            _ => r.Chance(0.4) ? "&" : "and",
        };

        GivenName g1 = Given(r, lang);
        GivenName g2 = Given(r, lang);
        string f1 = Family(r, lang);
        Builder b = new();
        switch (pattern)
        {
            case "mr-mrs-gf":
                {
                    string mr = lang == "fr" ? "M." : lang == "es" ? "Sr." : st.TitleDots ? "Mr." : "Mr";
                    string mrs = lang == "fr" ? "Mme" : lang == "es" ? "Sra." : st.TitleDots ? "Mrs." : "Mrs";
                    b.Add("Prefix", mr, 1).Raw(sp + conn + sp).Add("Prefix", mrs, 2).Raw(sp).Add("Given", g1.Name, 1, 2).Raw(sp).Family(f1, 1, 2);
                    break;
                }

            case "mr-mrs-f":
                {
                    string mr = lang == "fr" ? "M." : lang == "es" ? "Sr." : st.TitleDots ? "Mr." : "Mr";
                    string mrs = lang == "fr" ? "Mme" : lang == "es" ? "Sra." : st.TitleDots ? "Mrs." : "Mrs";
                    b.Add("Prefix", mr, 1).Raw(sp + conn + sp).Add("Prefix", mrs, 2).Raw(sp).Family(f1, 1, 2);
                    break;
                }

            case "g-and-g-f":
                b.Add("Given", g1.Name, 1).Raw(sp + conn + sp).Add("Given", g2.Name, 2).Raw(sp).Family(f1, 1, 2);
                break;
            case "gf-and-gf":
                b.Add("Given", g1.Name, 1).Raw(sp).Family(f1, 1).Raw(sp + conn + sp).Add("Given", g2.Name, 2).Raw(sp).Family(Family(r, lang), 2);
                break;
            case "gf-semicolon-gf":
                b.Add("Given", g1.Name, 1).Raw(sp).Family(f1, 1).Raw(r.Chance(0.2) ? "\n" : "; ").Add("Given", g2.Name, 2).Raw(sp).Family(Family(r, lang), 2);
                break;
            case "list-g-g-g-f":
                {
                    GivenName g3 = Given(r, lang);
                    b.Add("Given", g1.Name, 1).Raw(st.Comma).Add("Given", g2.Name, 2).Raw(sp + conn + sp).Add("Given", g3.Name, 3).Raw(sp).Family(f1, 1, 2, 3);
                    break;
                }

            case "dr-and-dr":
                {
                    string dr = lang == "fr" ? "Dr" : lang == "es" ? "Dr." : st.TitleDots ? "Dr." : "Dr";
                    b.Add("Prefix", dr, 1).Raw(sp).Add("Given", g1.Name, 1).Raw(sp + conn + sp).Add("Prefix", dr, 2).Raw(sp).Add("Given", g2.Name, 2).Raw(sp).Family(f1, 1, 2);
                    break;
                }

            default:
                throw new InvalidOperationException(pattern);
        }

        return Finish(id, b, "multiple", pattern, lang, region, st.CaseMode, "name-lists", noAccents: false);
    }

    // ---- strings that are not names ---------------------------------------------------------------------------------

    internal Example Unknown(Prng r, string id)
    {
        (string lang, string region) = r.Chance(0.8) ? ("en", r.Chance(0.82) ? "US" : "CA") : ("fr", "CA");
        Style st = PickStyle(r, lang, region);
        string pattern = UnknownPatterns[r.Weighted(UnknownWeights)].Name;
        Builder b = new();
        switch (pattern)
        {
            case "job-title":
                {
                    List<string> levels = _templates.List("level", lang);
                    List<string> areas = _templates.List("area", lang);
                    List<string> roles = _templates.List("role", lang);
                    StringBuilder sb = new();
                    if (r.Chance(0.5))
                    {
                        sb.Append(r.Pick(levels)).Append(' ');
                    }

                    if (lang == "fr")
                    {
                        sb.Append(r.Pick(roles));
                        sb.Append(' ').Append(r.Pick(areas));
                    }
                    else
                    {
                        if (r.Chance(0.6))
                        {
                            sb.Append(r.Pick(areas)).Append(' ');
                        }

                        sb.Append(r.Pick(roles));
                    }

                    b.Raw(sb.ToString());
                    break;
                }

            case "department":
                b.Raw(r.Pick(_templates.List("unit", lang)));
                break;
            case "generic":
                b.Raw(r.Pick(_templates.List("generic", lang)));
                break;
            case "bare-given":
                b.Raw(Given(r, lang).Name);
                break;
            case "bare-family":
                b.Raw(Family(r, lang));
                break;
            case "title-only":
                b.Raw(r.Chance(0.5) ? TitleText(r, lang, '-', st) : lang == "fr" ? "M. et Mme" : "Mr & Mrs");
                break;
            case "digits":
                b.Raw(r.Pick(_templates.List("digits", "en")));
                break;
            case "email":
                {
                    string local = Txt.MapChars(Given(r, lang).Name, false) + (r.Chance(0.5) ? "." : "_") + Txt.MapChars(Family(r, lang), false);
                    b.Raw(Txt.StripAccents(local.Replace(" ", "", StringComparison.Ordinal)) + "@" + (r.Chance(0.5) ? "example.com" : "example.org"));
                    break;
                }

            case "long-text":
                {
                    List<string> filler = _templates.List("filler", lang);
                    int words = 9 + r.Int(6);
                    for (int i = 0; i < words; i++)
                    {
                        b.Raw(i == 0 ? "" : " ").Raw(r.Pick(filler));
                    }

                    break;
                }

            default:
                throw new InvalidOperationException(pattern);
        }

        return Finish(id, b, "unknown", pattern, lang, region, pattern is "email" ? 0 : st.CaseMode, "templates", noAccents: false);
    }

    // ---- finishing ----------------------------------------------------------------------------------------------------

    internal static Example Finish(string id, Builder b, string cls, string pattern, string lang, string region, int caseMode, string source, bool noAccents)
    {
        string plain = b.Text;
        string text = plain;
        string noise = "";
        if (noAccents)
        {
            text = Txt.StripAccents(text);
            noise = "no-accents";
        }

        if (caseMode == 1)
        {
            text = Txt.MapChars(text, upper: true);
            noise = "upper";
        }
        else if (caseMode == 2)
        {
            text = Txt.MapChars(text, upper: false);
            noise = "lower";
        }

        int bucket = Txt.Bucket(Txt.Fold(plain));
        return new Example
        {
            Id = id,
            Text = text,
            Class = cls,
            Pattern = pattern,
            Noise = noise,
            Lang = lang,
            Region = region,
            Source = source,
            Split = bucket < 90 ? "train" : bucket < 95 ? "dev" : "reserved",
            Parts = b.Parts,
        };
    }
}
