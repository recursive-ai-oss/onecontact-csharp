using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace OneContact.Data.Names;

/// <summary>The name pools of one language and country: who is drawn, in what proportion, and how the name is ordered.</summary>
internal sealed class Tier2Unit
{
    internal required string Lang { get; init; }

    internal required string Region { get; init; }

    /// <summary>Share of the persons rendered for this unit.</summary>
    internal required double Weight { get; init; }

    internal required WeightedList<GivenName> Given { get; init; }

    /// <summary>The family names every person draws from; for sex-paired sources (Slovenian, Polish feminine forms) see <see cref="FamilyF"/>.</summary>
    internal required WeightedList<string> Family { get; init; }

    /// <summary>Feminine and masculine family-name lists, when the source publishes them apart (Poland): a given name's sex picks the list.</summary>
    internal WeightedList<string>? FamilyF { get; init; }

    internal WeightedList<string>? FamilyM { get; init; }

    /// <summary>Iberian naming: a first and a second family name.</summary>
    internal bool TwoFamilyNames { get; init; }

    /// <summary>Family name before the given name without a comma is the usual order (Hungarian).</summary>
    internal bool FamilyFirst { get; init; }

    /// <summary>"name-lists" when an open national list feeds the unit, "templates" when only our own short list does.</summary>
    internal required string Source { get; init; }

    internal required string Inputs { get; init; }
}

/// <summary>Titles, particles and connectors per language, from <c>templates/forms-tier2.tsv</c>, with the English, French and Spanish ones of the US/CA templates.</summary>
internal sealed class Tier2Forms
{
    internal Dictionary<string, WeightedList<Title>> Titles { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, WeightedList<string>> Particles { get; } = new(StringComparer.Ordinal);

    internal Dictionary<string, List<string>> Words { get; } = new(StringComparer.Ordinal);

    internal static Tier2Forms Load(string dir, Templates basic)
    {
        Tier2Forms f = new();
        Dictionary<string, List<(Title, double)>> titles = [];
        Dictionary<string, List<(string, double)>> particles = [];
        foreach (string line in File.ReadAllLines(Path.Combine(dir, "forms-tier2.tsv")))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] c = line.Split('\t');
            double weight = double.Parse(c[^1], CultureInfo.InvariantCulture);
            switch (c[0])
            {
                case "title":
                    if (!titles.TryGetValue(c[1], out List<(Title, double)>? tl))
                    {
                        titles[c[1]] = tl = [];
                    }

                    tl.Add((new Title(c[2], c[3][0], c[4] == "1"), weight));
                    break;
                case "particle":
                    if (!particles.TryGetValue(c[1], out List<(string, double)>? pl))
                    {
                        particles[c[1]] = pl = [];
                    }

                    pl.Add((c[2], weight));
                    break;
                default:
                    {
                        string key = c[0] + "|" + c[1];
                        if (!f.Words.TryGetValue(key, out List<string>? wl))
                        {
                            f.Words[key] = wl = [];
                        }

                        wl.Add(c[2]);
                        break;
                    }
            }
        }

        foreach ((string lang, List<(Title, double)> l) in titles)
        {
            f.Titles[lang] = new WeightedList<Title>(l);
        }

        foreach ((string lang, List<(string, double)> l) in particles)
        {
            f.Particles[lang] = new WeightedList<string>(l);
        }

        foreach (string lang in new[] { "en", "fr", "es" })
        {
            f.Titles[lang] = basic.Titles[lang];
            if (basic.Particles.TryGetValue(lang, out WeightedList<string>? p))
            {
                f.Particles[lang] = p;
            }
        }

        return f;
    }

    internal string Word(Prng r, string kind, string lang) => r.Pick(Words[kind + "|" + lang]);
}

/// <summary>
/// Renders the Tier 2 person-name training set (<c>names-tier2-2</c>): single persons, several-people strings and non-names in each language of
/// the Tier 2 countries, from the open national lists where there is one and from our own short lists where there is none. Every label comes
/// from the template that placed the part. Given names and family names are drawn independently, so no string is a real person's name.
/// </summary>
internal sealed class Tier2Gen
{
    internal const string SetVersion = "names-tier2-2";

    private static readonly (string Name, double W, double Iberian, double FamilyFirst)[] SinglePatterns =
    [
        ("gf", 32, 14, 8), ("gmf", 10, 7, 3), ("gif", 8, 3, 2), ("if", 3, 1, 1), ("iif", 1, 0.5, 0.5),
        ("fg", 7, 3, 4), ("fgm", 2, 1, 1), ("fgi", 1, 0.5, 0.5),
        ("tgf", 6, 5, 3), ("tf", 2, 1, 1), ("tgmf", 1.5, 1, 1), ("gfp", 2, 0.5, 1), ("tgfp", 1, 0, 0.5),
        ("gpf", 3, 1, 0), ("gf-hyphen", 2, 0.5, 0.5), ("gfs", 1, 0, 0), ("tgfs", 0.3, 0, 0),
        ("gfsf", 0, 40, 0), ("gmfsf", 0, 14, 0), ("tgfsf", 0, 8, 0), ("fsfg", 0, 5, 0),
        ("fgn", 0, 0, 40), ("fgmn", 0, 0, 8), ("tfgn", 0, 0, 10), ("fgin", 0, 0, 2),
    ];

    private static readonly (string Name, double Weight)[] MultiplePatterns =
        [("mr-mrs-gf", 24), ("mr-mrs-f", 10), ("g-and-g-f", 22), ("gf-and-gf", 16), ("gf-semicolon-gf", 8), ("list-g-g-g-f", 6), ("dr-and-dr", 4)];

    private static readonly (string Name, double Weight)[] UnknownPatterns =
        [("bare-given", 14), ("bare-family", 12), ("title-only", 3), ("digits", 2), ("email", 8)];

    private static readonly double[] MultipleWeights = MultiplePatterns.Select(p => p.Weight).ToArray();
    private static readonly double[] UnknownWeights = UnknownPatterns.Select(p => p.Weight).ToArray();

    private readonly List<Tier2Unit> _units;
    private readonly double[] _unitWeights;
    private readonly Tier2Forms _forms;
    private readonly Templates _templates;

    internal Tier2Gen(List<Tier2Unit> units, Tier2Forms forms, Templates templates)
    {
        _units = units;
        _unitWeights = units.Select(u => u.Weight).ToArray();
        _forms = forms;
        _templates = templates;
    }

    private sealed class Style
    {
        internal string Space = " ";
        internal string Comma = ", ";
        internal bool Dots;
        internal bool TitleDots;
        internal int CaseMode;
    }

    private static Style PickStyle(Prng r, string lang)
    {
        Style s = new() { Dots = r.Chance(0.8), TitleDots = lang == "en" && r.Chance(0.5) };
        double x = r.Unit();
        s.Space = x < 0.985 ? " " : "  ";
        double y = r.Unit();
        s.Comma = y < 0.965 ? ", " : y < 0.985 ? "," : " , ";
        double z = r.Unit();
        s.CaseMode = z < 0.86 ? 0 : z < 0.93 ? 1 : 2;
        return s;
    }

    private Tier2Unit PickUnit(Prng r) => _units[r.Weighted(_unitWeights)];

    private static GivenName Given(Prng r, Tier2Unit u) => u.Given.Pick(r);

    private static string Family(Prng r, Tier2Unit u, char sex)
    {
        if (u.FamilyF != null && u.FamilyM != null)
        {
            return sex == 'F' ? u.FamilyF.Pick(r) : u.FamilyM.Pick(r);
        }

        return u.Family.Pick(r);
    }

    private static string PlainFamily(Prng r, Tier2Unit u, char sex)
    {
        string f = Family(r, u, sex);
        for (int i = 0; i < 8 && Builder.ParticleLength(f) > 0; i++)
        {
            f = Family(r, u, sex);
        }

        return f;
    }

    private static string Middle(Prng r, Tier2Unit u, char sex)
    {
        for (int i = 0; i < 4; i++)
        {
            GivenName m = Given(r, u);
            if (!m.Name.Contains('-', StringComparison.Ordinal) && (m.Sex == sex || m.Sex == '-' || sex == '-' || r.Chance(0.2)))
            {
                return m.Name;
            }
        }

        return Given(r, u).Name.Split('-')[0];
    }

    private static string Initial(string name, Style st)
    {
        char c = char.ToUpperInvariant(name[0]);
        return st.Dots ? c + "." : c.ToString();
    }

    private string TitleText(Prng r, string lang, char sex, Style st)
    {
        WeightedList<Title> list = _forms.Titles[lang];
        Title t = list.Pick(r);
        for (int i = 0; i < 6 && ((t.Sex == 'M' && sex == 'F') || (t.Sex == 'F' && sex == 'M')); i++)
        {
            t = list.Pick(r);
        }

        return t.DotOptional && st.TitleDots ? t.Text + "." : t.Text;
    }

    private string Professional(Prng r, Style st)
    {
        string s = _templates.Professional["en"].Pick(r).Text;
        return st.Dots && s switch { "PhD" => true, "MBA" => true, "MSc" => true, _ => false } ? s switch { "PhD" => "Ph.D.", "MSc" => "M.Sc.", _ => "M.B.A." } : s;
    }

    private string Generational(Prng r, Style st)
    {
        string s = _templates.Generational["en"].Pick(r).Text;
        return st.Dots && s is "Jr" or "Sr" ? s + "." : s;
    }

    // ---- single persons ---------------------------------------------------------------------------------------------

    internal Example Single(Prng r, string id)
    {
        Tier2Unit u = PickUnit(r);
        Style st = PickStyle(r, u.Lang);
        bool hasParticles = _forms.Particles.ContainsKey(u.Lang);
        double[] weights = SinglePatterns.Select(p => (u.FamilyFirst ? p.FamilyFirst : u.TwoFamilyNames ? p.Iberian : p.W)).ToArray();
        if (!hasParticles)
        {
            weights[Array.FindIndex(SinglePatterns, p => p.Name == "gpf")] = 0;
        }

        if (u.Lang != "en")
        {
            weights[Array.FindIndex(SinglePatterns, p => p.Name == "gfs")] = 0;
            weights[Array.FindIndex(SinglePatterns, p => p.Name == "tgfs")] = 0;
        }

        string pattern = SinglePatterns[r.Weighted(weights)].Name;
        GivenName g = Given(r, u);
        string family = Family(r, u, g.Sex);
        Builder b = new();
        string sp = st.Space;
        string lang = u.Lang;

        switch (pattern)
        {
            case "gf":
                b.Add("Given", g.Name).Raw(sp).Family(family);
                break;
            case "gmf":
                b.Add("Given", g.Name).Raw(sp).Add("Middle", Middle(r, u, g.Sex)).Raw(sp).Family(family);
                break;
            case "gif":
                b.Add("Given", g.Name).Raw(sp).Add("Middle", Initial(Middle(r, u, g.Sex), st)).Raw(sp).Family(family);
                break;
            case "if":
                b.Add("Given", Initial(g.Name, st)).Raw(sp).Family(family);
                break;
            case "iif":
                b.Add("Given", Initial(g.Name, st)).Raw(sp).Add("Middle", Initial(Middle(r, u, g.Sex), st)).Raw(sp).Family(family);
                break;
            case "fg":
                b.Family(family).Raw(st.Comma).Add("Given", g.Name);
                break;
            case "fgm":
                b.Family(family).Raw(st.Comma).Add("Given", g.Name).Raw(sp).Add("Middle", Middle(r, u, g.Sex));
                break;
            case "fgi":
                b.Family(family).Raw(st.Comma).Add("Given", g.Name).Raw(sp).Add("Middle", Initial(Middle(r, u, g.Sex), st));
                break;
            case "tgf":
                b.Add("Prefix", TitleText(r, lang, g.Sex, st)).Raw(sp).Add("Given", g.Name).Raw(sp).Family(family);
                break;
            case "tf":
                b.Add("Prefix", TitleText(r, lang, g.Sex == '-' ? '-' : g.Sex, st)).Raw(sp).Family(family);
                break;
            case "tgmf":
                b.Add("Prefix", TitleText(r, lang, g.Sex, st)).Raw(sp).Add("Given", g.Name).Raw(sp).Add("Middle", Middle(r, u, g.Sex)).Raw(sp).Family(family);
                break;
            case "gfp":
                b.Add("Given", g.Name).Raw(sp).Family(family).Raw(r.Chance(0.88) ? st.Comma : sp).Add("ProfessionalSuffix", Professional(r, st));
                break;
            case "tgfp":
                b.Add("Prefix", TitleText(r, lang, g.Sex, st)).Raw(sp).Add("Given", g.Name).Raw(sp).Family(family).Raw(st.Comma).Add("ProfessionalSuffix", Professional(r, st));
                break;
            case "gfs":
                b.Add("Given", g.Name).Raw(sp).Family(family).Raw(r.Chance(0.3) ? st.Comma : sp).Add("Suffix", Generational(r, st));
                break;
            case "tgfs":
                b.Add("Prefix", TitleText(r, lang, g.Sex, st)).Raw(sp).Add("Given", g.Name).Raw(sp).Family(family).Raw(sp).Add("Suffix", Generational(r, st));
                break;
            case "gpf":
                family = PlainFamily(r, u, g.Sex);
                b.Add("Given", g.Name).Raw(sp).Add("Particle", _forms.Particles[lang].Pick(r)).Raw(sp).Family(family);
                break;
            case "gf-hyphen":
                {
                    string second = Family(r, u, g.Sex);
                    for (int i = 0; i < 6 && string.Equals(second, family, StringComparison.Ordinal); i++)
                    {
                        second = Family(r, u, g.Sex);
                    }

                    b.Add("Given", g.Name).Raw(sp).Family(family + "-" + second);
                    break;
                }

            case "gfsf":
                b.Add("Given", g.Name).Raw(sp).Family(family).Raw(sp).Add("SecondFamily", Family(r, u, g.Sex));
                break;
            case "gmfsf":
                b.Add("Given", g.Name).Raw(sp).Add("Middle", Middle(r, u, g.Sex)).Raw(sp).Family(family).Raw(sp).Add("SecondFamily", Family(r, u, g.Sex));
                break;
            case "tgfsf":
                b.Add("Prefix", TitleText(r, lang, g.Sex, st)).Raw(sp).Add("Given", g.Name).Raw(sp).Family(family).Raw(sp).Add("SecondFamily", Family(r, u, g.Sex));
                break;
            case "fsfg":
                b.Family(family).Raw(sp).Add("SecondFamily", Family(r, u, g.Sex)).Raw(st.Comma).Add("Given", g.Name);
                break;
            case "fgn":
                b.Family(family).Raw(sp).Add("Given", g.Name);
                break;
            case "fgmn":
                b.Family(family).Raw(sp).Add("Given", g.Name).Raw(sp).Add("Middle", Middle(r, u, g.Sex));
                break;
            case "tfgn":
                b.Add("Prefix", TitleText(r, lang, g.Sex, st)).Raw(sp).Family(family).Raw(sp).Add("Given", g.Name);
                break;
            case "fgin":
                b.Family(family).Raw(sp).Add("Given", Initial(g.Name, st));
                break;
            default:
                throw new InvalidOperationException(pattern);
        }

        return PersonGen.Finish(id, b, "person", pattern, lang, u.Region, st.CaseMode, u.Source, noAccents: lang != "en" && r.Chance(0.02));
    }

    // ---- several people ---------------------------------------------------------------------------------------------

    internal Example Multiple(Prng r, string id)
    {
        Tier2Unit u = PickUnit(r);
        string lang = u.Lang;
        Style st = PickStyle(r, lang);
        string pattern = MultiplePatterns[r.Weighted(MultipleWeights)].Name;
        string sp = st.Space;
        string conn = _forms.Word(r, "and", lang);
        string mr = _forms.Word(r, "mr", lang);
        string mrs = _forms.Word(r, "mrs", lang);
        string dr = _forms.Word(r, "dr", lang);
        GivenName g1 = Given(r, u);
        GivenName g2 = Given(r, u);
        string f1 = Family(r, u, 'M');
        Builder b = new();
        switch (pattern)
        {
            case "mr-mrs-gf":
                b.Add("Prefix", mr, 1).Raw(sp + conn + sp).Add("Prefix", mrs, 2).Raw(sp).Add("Given", g1.Name, 1, 2).Raw(sp).Family(f1, 1, 2);
                break;
            case "mr-mrs-f":
                b.Add("Prefix", mr, 1).Raw(sp + conn + sp).Add("Prefix", mrs, 2).Raw(sp).Family(f1, 1, 2);
                break;
            case "g-and-g-f":
                b.Add("Given", g1.Name, 1).Raw(sp + conn + sp).Add("Given", g2.Name, 2).Raw(sp).Family(f1, 1, 2);
                break;
            case "gf-and-gf":
                b.Add("Given", g1.Name, 1).Raw(sp).Family(f1, 1).Raw(sp + conn + sp).Add("Given", g2.Name, 2).Raw(sp).Family(Family(r, u, g2.Sex), 2);
                break;
            case "gf-semicolon-gf":
                b.Add("Given", g1.Name, 1).Raw(sp).Family(f1, 1).Raw(r.Chance(0.2) ? "\n" : "; ").Add("Given", g2.Name, 2).Raw(sp).Family(Family(r, u, g2.Sex), 2);
                break;
            case "list-g-g-g-f":
                {
                    GivenName g3 = Given(r, u);
                    b.Add("Given", g1.Name, 1).Raw(st.Comma).Add("Given", g2.Name, 2).Raw(sp + conn + sp).Add("Given", g3.Name, 3).Raw(sp).Family(f1, 1, 2, 3);
                    break;
                }

            case "dr-and-dr":
                b.Add("Prefix", dr, 1).Raw(sp).Add("Given", g1.Name, 1).Raw(sp + conn + sp).Add("Prefix", dr, 2).Raw(sp).Add("Given", g2.Name, 2).Raw(sp).Family(f1, 1, 2);
                break;
            default:
                throw new InvalidOperationException(pattern);
        }

        return PersonGen.Finish(id, b, "multiple", pattern, lang, u.Region, st.CaseMode, u.Source, noAccents: false);
    }

    // ---- strings that are not names ---------------------------------------------------------------------------------

    internal Example Unknown(Prng r, string id)
    {
        Tier2Unit u = PickUnit(r);
        string lang = u.Lang;
        Style st = PickStyle(r, lang);
        string pattern = UnknownPatterns[r.Weighted(UnknownWeights)].Name;
        Builder b = new();
        switch (pattern)
        {
            case "bare-given":
                b.Raw(Given(r, u).Name);
                break;
            case "bare-family":
                b.Raw(Family(r, u, r.Chance(0.5) ? 'F' : 'M'));
                break;
            case "title-only":
                b.Raw(r.Chance(0.5) ? TitleText(r, lang, '-', st) : _forms.Word(r, "mr", lang) + " " + _forms.Word(r, "and", lang) + " " + _forms.Word(r, "mrs", lang));
                break;
            case "digits":
                b.Raw(r.Pick(_templates.List("digits", "en")));
                break;
            case "email":
                {
                    string local = Txt.MapChars(Given(r, u).Name, false) + (r.Chance(0.5) ? "." : "_") + Txt.MapChars(Family(r, u, 'M'), false);
                    b.Raw(Txt.StripAccents(local.Replace(" ", "", StringComparison.Ordinal)) + "@" + (r.Chance(0.5) ? "example.com" : "example.org"));
                    break;
                }

            default:
                throw new InvalidOperationException(pattern);
        }

        return PersonGen.Finish(id, b, "unknown", pattern, lang, u.Region, st.CaseMode, u.Source, noAccents: false);
    }

    // ---- the render -------------------------------------------------------------------------------------------------

    internal static void Render(string dataDir, string outDir, ulong seed, int persons, int multiples, int unknowns)
    {
        string baseDir = AppContext.BaseDirectory;
        string templateDir = Path.Combine(baseDir, "templates");
        Directory.CreateDirectory(outDir);
        Console.WriteLine("loading the name lists");
        List<NameSet> sets = [];
        List<Tier2Unit> units = Tier2Units.Build(dataDir, templateDir, sets);
        Templates basic = Templates.Load(templateDir);
        Tier2Forms forms = Tier2Forms.Load(templateDir, basic);
        Tier2Gen gen = new(units, forms, basic);

        long reserved = 0;
        long duplicates = 0;
        HashSet<ulong> texts = [];
        SortedDictionary<string, long> perUnit = new(StringComparer.Ordinal);
        using ExampleWriter writer = new(outDir);

        void Run(string prefix, int attempts, Func<Prng, string, Example> make, bool dedupe)
        {
            for (int i = 0; i < attempts; i++)
            {
                string id = prefix + i.ToString("D8", CultureInfo.InvariantCulture);
                Example? e = null;
                for (int k = 0; k < 4 && e == null; k++)
                {
                    Example c = make(Prng.For(seed, k == 0 ? id : id + "#" + k.ToString(CultureInfo.InvariantCulture)), id);
                    if (c.Split == "reserved" || !dedupe || texts.Add(Txt.Hash64(Txt.Fold(c.Text))))
                    {
                        e = c;
                    }
                    else
                    {
                        duplicates++;
                    }
                }

                if (e == null)
                {
                    continue;
                }

                if (e.Split == "reserved")
                {
                    reserved++;
                    continue;
                }

                writer.Write(e);
                string key = $"{e.Split}|{e.Lang}-{e.Region}|{e.Class}";
                perUnit[key] = perUnit.GetValueOrDefault(key) + 1;
            }
        }

        Console.WriteLine("rendering persons");
        Run("t", persons, gen.Single, true);
        Console.WriteLine("rendering several-people strings");
        Run("m", multiples, gen.Multiple, true);
        Console.WriteLine("rendering non-names");
        Run("u", unknowns, gen.Unknown, false);
        writer.Dispose();

        writer.TextHashes.Sort();
        using (FileStream fs = new(Path.Combine(outDir, "text-hashes.u64"), FileMode.Create, FileAccess.Write))
        {
            byte[] buf = new byte[8];
            foreach (ulong h in writer.TextHashes)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(buf, h);
                fs.Write(buf);
            }
        }

        WriteManifest(outDir, dataDir, templateDir, seed, persons, multiples, unknowns, sets, units, writer, perUnit, reserved, duplicates);
    }

    private static string Hex(byte[] hash) => Convert.ToHexString(hash).ToLowerInvariant();

    private static void WriteManifest(string outDir, string dataDir, string templateDir, ulong seed, int persons, int multiples, int unknowns, List<NameSet> sets,
        List<Tier2Unit> units, ExampleWriter writer, SortedDictionary<string, long> perUnit, long reserved, long duplicates)
    {
        using FileStream manifest = new(Path.Combine(outDir, "manifest.json"), FileMode.Create, FileAccess.Write);
        using Utf8JsonWriter w = new(manifest, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        w.WriteStartObject();
        w.WriteString("set", SetVersion);
        w.WriteString("tool", "names-2");
        w.WriteNumber("seed", seed);
        w.WriteNumber("personAttempts", persons);
        w.WriteNumber("multipleAttempts", multiples);
        w.WriteNumber("unknownAttempts", unknowns);

        w.WriteStartArray("inputs");
        foreach (string pinName in new[] { "names-tier2.pin", "names.pin" })
        {
            foreach (string line in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "pins", pinName)))
            {
                string[] c = line.Split(' ', 6);
                // From the US/CA pin only the INSEE files are read by this set (French given and family names).
                if (c[0] != "file" || (pinName == "names.pin" && !c[1].StartsWith("insee/", StringComparison.Ordinal)))
                {
                    continue;
                }

                string path = Path.Combine(dataDir, c[1]);
                w.WriteStartObject();
                w.WriteString("file", c[1]);
                w.WriteNumber("bytes", new FileInfo(path).Length);
                w.WriteString("sha256", Hex(SHA256.HashData(File.ReadAllBytes(path))));
                w.WriteString("role", "source (train and dev through the split of the rendered text)");
                w.WriteEndObject();
            }
        }

        w.WriteEndArray();

        w.WriteStartObject("templates");
        foreach (string f in new[] { "forms-tier2.tsv", "titles.tsv", "particles.tsv", "suffixes.tsv", "negatives.tsv" })
        {
            w.WriteString(f, Hex(SHA256.HashData(File.ReadAllBytes(Path.Combine(templateDir, f)))));
        }

        foreach (string f in Directory.GetFiles(Path.Combine(templateDir, "own"), "*.tsv").OrderBy(x => x, StringComparer.Ordinal))
        {
            w.WriteString("own/" + Path.GetFileName(f), Hex(SHA256.HashData(File.ReadAllBytes(f))));
        }

        w.WriteEndObject();

        w.WriteStartObject("nameSets");
        foreach (NameSet s in sets)
        {
            w.WriteStartObject(s.Id);
            w.WriteNumber("rowsRead", s.RowsRead);
            w.WriteNumber("names", s.RowsKept);
            w.WriteEndObject();
        }

        w.WriteEndObject();

        w.WriteStartArray("units");
        foreach (Tier2Unit u in units)
        {
            w.WriteStartObject();
            w.WriteString("lang", u.Lang);
            w.WriteString("region", u.Region);
            w.WriteNumber("weight", u.Weight);
            w.WriteString("src", u.Source);
            w.WriteString("inputs", u.Inputs);
            w.WriteNumber("givenNames", u.Given.Count);
            w.WriteNumber("familyNames", u.Family.Count);
            w.WriteEndObject();
        }

        w.WriteEndArray();

        w.WriteNumber("reservedNotRendered", reserved);
        w.WriteNumber("duplicateTextsRejected", duplicates);
        w.WriteStartObject("examplesPerUnit");
        long totalTrain = 0;
        long totalDev = 0;
        foreach ((string key, long v) in perUnit)
        {
            w.WriteNumber(key, v);
            if (key.StartsWith("train|", StringComparison.Ordinal))
            {
                totalTrain += v;
            }
            else
            {
                totalDev += v;
            }
        }

        w.WriteEndObject();
        w.WriteStartObject("counts");
        foreach ((string key, long v) in writer.Counts)
        {
            w.WriteNumber(key, v);
        }

        w.WriteEndObject();
        w.WriteNumber("totalTrain", totalTrain);
        w.WriteNumber("totalDev", totalDev);

        w.WriteStartArray("files");
        foreach (string file in Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            string rel = Path.GetRelativePath(outDir, file).Replace('\\', '/');
            if (rel == "manifest.json")
            {
                continue;
            }

            w.WriteStartObject();
            w.WriteString("path", rel);
            w.WriteNumber("bytes", new FileInfo(file).Length);
            w.WriteString("sha256", Hex(SHA256.HashData(File.ReadAllBytes(file))));
            w.WriteString("role", rel.StartsWith("train/", StringComparison.Ordinal) ? "train" : rel.StartsWith("dev/", StringComparison.Ordinal) ? "dev" : "index");
            w.WriteEndObject();
        }

        w.WriteEndArray();
        w.WriteEndObject();
    }
}
