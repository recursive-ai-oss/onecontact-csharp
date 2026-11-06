using System.Globalization;
using System.Text;
using OneContact.Data.AddressUsCa;

namespace OneContact.Data.AddressTier2C;

/// <summary>The rates the generator draws with. They are written into the manifest, so a run states what it did.</summary>
internal sealed class Rates
{
    /// <summary>Casing of the whole example: as the register writes it, UPPER, lower, and the postal service's own form (Norway's upper-case post office).</summary>
    internal double[] CaseWeights { get; init; } = [0.62, 0.13, 0.07, 0.18];

    internal double LabelLayout { get; init; } = 0.52;

    internal double RecipientLabel { get; init; } = 0.35;

    internal double RecipientLine { get; init; } = 0.10;

    internal double OrganizationLabel { get; init; } = 0.20;

    internal double OrganizationLine { get; init; } = 0.06;

    internal double CountryLabel { get; init; } = 0.07;

    internal double CountryLine { get; init; } = 0.10;

    internal double MissingPostalCode { get; init; } = 0.04;

    internal double MissingLocality { get; init; } = 0.012;

    internal double SyntheticUnit { get; init; } = 0.06;

    internal double PostBox { get; init; } = 0.025;

    internal double PostBoxWithStreet { get; init; } = 0.25;

    internal double CountryCodePrefix { get; init; } = 0.07;

    internal double Dependent { get; init; } = 0.6;

    internal double Typo { get; init; } = 0.05;

    internal double DoubleSpace { get; init; } = 0.01;

    internal double SpacedSuffix { get; init; } = 0.25;
}

/// <summary>
/// Renders one register address as a free-text example through the postal layouts of its country: street before number in all four
/// countries, the postcode first on the last line in Norway, the Czech Republic and Estonia and last in Latvia. Only labelled pieces
/// carry letters and digits, so every one of them lies inside a span.
/// </summary>
internal sealed partial class Renderer
{
    /// <summary>Changes whenever a template, rate or draw order changes, so a manifest says which renderer made its files.</summary>
    internal const string Version = "tier2c-2026-11-05.1";

    private static readonly Dictionary<string, string[]> CountryNames = new(StringComparer.Ordinal)
    {
        ["NO"] = ["Norge", "Norway", "NORGE"],
        ["CZ"] = ["Česká republika", "Czech Republic", "Česko", "Czechia"],
        ["LV"] = ["Latvija", "Latvia"],
        ["EE"] = ["Eesti", "Estonia"],
        ["DK"] = ["Danmark", "Denmark", "DANMARK"],
        ["SE"] = ["Sverige", "Sweden", "SVERIGE"],
        ["FI"] = ["Suomi", "Finland", "SUOMI"],
        ["FI-AX"] = ["Åland", "Finland", "ÅLAND"],
        ["IS"] = ["Ísland", "Iceland", "ÍSLAND"],
    };

    private readonly Rates _rates;
    private readonly ulong _seed;
    private readonly Dictionary<string, List<string>> _pools = new(StringComparer.Ordinal);

    internal Renderer(string templatesDir, Rates rates, ulong seed)
    {
        _rates = rates;
        _seed = seed;
        string[] paths = Directory.GetFiles(templatesDir, "tier2c-*names*.tsv").OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Sha256 = paths.Length == 1 ? Sha256Of(paths[0]) : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(paths.Select(Sha256Of))))).ToLowerInvariant();
        foreach (string line in paths.SelectMany(File.ReadLines))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] c = line.Split('\t');
            string key = c[0] + "/" + c[1];
            if (!_pools.TryGetValue(key, out List<string>? list))
            {
                _pools[key] = list = [];
            }

            list.Add(c[2]);
        }
    }

    internal string Sha256 { get; }

    internal Rates Rates => _rates;

    internal static string Sha256Of(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    internal Example Render(Addr a) => new Ctx(this, a, Prng.For(_seed, a.Id)).Run();

    private sealed partial class Ctx
    {
        private readonly Renderer _o;
        private readonly Addr _a;
        private readonly Prng _r;
        private readonly Rates _rt;
        private readonly List<string> _events = [];
        private int _mode;

        internal Ctx(Renderer o, Addr a, Prng r)
        {
            _o = o;
            _a = a;
            _r = r;
            _rt = o._rates;
        }

        internal Example Run()
        {
            string cc = _a.Country;
            _mode = _r.Weighted(_rt.CaseWeights);
            _events.Add("case:" + new[] { "asis", "upper", "lower", "postal" }[_mode]);
            bool label = _r.Chance(_rt.LabelLayout);
            bool hasOrg = _r.Chance(label ? _rt.OrganizationLabel : _rt.OrganizationLine);
            bool hasRecipient = _r.Chance(label ? _rt.RecipientLabel : _rt.RecipientLine);
            bool box = _r.Chance(_rt.PostBox);

            List<Piece> delivery = Delivery();
            List<Piece>? boxLine = null;
            if (box)
            {
                boxLine = PostBoxPieces(cc);
                _events.Add("post-box");
                if (!_r.Chance(_rt.PostBoxWithStreet))
                {
                    delivery = [];
                }
            }

            List<Piece> dependent = [];
            List<Piece> last = LastLine(cc, out dependent);

            List<List<Piece>> lines = [];
            if (hasRecipient)
            {
                lines.Add(NameLine(false));
                _events.Add("recipient");
            }

            if (hasOrg)
            {
                lines.Add(NameLine(true));
                _events.Add("organization");
            }

            if (delivery.Count > 0)
            {
                lines.Add(delivery);
            }

            if (dependent.Count > 0)
            {
                lines.Add(dependent);
            }

            if (boxLine is not null)
            {
                lines.Add(boxLine);
            }

            lines.Add(last);
            if (_r.Chance(label ? _rt.CountryLabel : _rt.CountryLine))
            {
                string[] names = CountryNames.TryGetValue(cc == "FI" && _a.Region == "19" ? "FI-AX" : cc, out string[]? known) ? known : CountryNamesC1[cc];
                lines.Add([new Piece(names[_r.Weighted(new[] { 0.7, 0.2, 0.06, 0.04 }[..names.Length])], "country")]);
                _events.Add("country");
            }

            if (label)
            {
                string layout = $"{cc.ToLowerInvariant()}.label.{_a.Language}";
                List<Piece> all = [];
                for (int i = 0; i < lines.Count; i++)
                {
                    if (i > 0)
                    {
                        all.Add(new Piece("\n", null));
                    }

                    all.AddRange(lines[i]);
                }

                return Finish(all, layout);
            }

            string[] seps = [", ", " ", " - ", "; ", " / "];
            int s = _r.Weighted([0.72, 0.17, 0.04, 0.03, 0.04]);
            List<Piece> flat = [];
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                {
                    flat.Add(new Piece(seps[s], null));
                }

                flat.AddRange(lines[i]);
            }

            return Finish(flat, $"{cc.ToLowerInvariant()}.line.{new[] { "comma", "space", "dash", "semicolon", "slash" }[s]}.{_a.Language}");
        }

        private Example Finish(List<Piece> pieces, string layout)
        {
            if (_r.Chance(_rt.Typo))
            {
                Typo(pieces);
            }

            if (_r.Chance(_rt.DoubleSpace))
            {
                foreach (Piece p in pieces)
                {
                    if (p.Label is null && p.Text == " ")
                    {
                        p.Text = "  ";
                        _events.Add("double-space");
                        break;
                    }
                }
            }

            StringBuilder sb = new();
            List<(int Start, int End, string Label)> entities = [];
            foreach (Piece p in pieces)
            {
                int start = sb.Length;
                sb.Append(p.Text);
                if (p.Label is not null)
                {
                    entities.Add((start, sb.Length, p.Label));
                }
            }

            string text = sb.ToString();
            int first = entities.FindIndex(e => e.Label.StartsWith("street.", StringComparison.Ordinal));
            List<(int Start, int End, string Label)> fields = [];
            if (first >= 0)
            {
                int lastStreet = entities.FindLastIndex(e => e.Label.StartsWith("street.", StringComparison.Ordinal));
                fields.Add((entities[first].Start, entities[lastStreet].End, "street-address"));
            }

            return new Example
            {
                Id = _a.Id,
                Country = _a.Country,
                Language = _a.Language,
                Layout = layout,
                Text = text,
                Tokens = Tokenizer.Tokenize(text, entities),
                Entities = entities,
                Fields = fields,
                Events = _events,
            };
        }

        private string Case(string t) => _mode switch { 1 => t.ToUpperInvariant(), 2 => t.ToLowerInvariant(), _ => t };

        // Street name, then number: "Storgata 12B", "Hlavní 123/4", "Brīvības iela 10 k-2", "Kase põik 9-12".
        private List<Piece> Delivery()
        {
            if (C1)
            {
                return DeliveryC1();
            }

            List<Piece> p = [new Piece(Case(_a.Street), "street.name")];
            if (_a.HouseNumber.Length == 0)
            {
                return p;
            }

            p.Add(new Piece(_r.Chance(0.03) ? ", " : " ", null));
            string number = _a.HouseNumber;
            if (_a.Suffix.Length > 0)
            {
                string sep = _a.Country == "LV" ? " " : _r.Chance(_rt.SpacedSuffix) ? " " : string.Empty;
                string suffix = _a.Country == "NO" && _r.Chance(0.15) ? _a.Suffix.ToLowerInvariant() : _a.Suffix;
                if (_a.Country == "FI" && _r.Chance(0.4))
                {
                    // Finnish postal custom writes the staircase letter in capitals after a space ("Kauppakatu 5 B").
                    sep = " ";
                    suffix = _a.Suffix.ToUpperInvariant();
                }

                number += sep + suffix;
            }

            p.Add(new Piece(_mode == 2 ? number.ToLowerInvariant() : number, "street.house-number"));
            if (_a.Unit.Length > 0)
            {
                // Estonian flat numbers follow the house number after a hyphen or a slash, or a word ("9-12", "9/12", "9, korter 12").
                int style = _r.Weighted([0.75, 0.1, 0.15]);
                if (style == 2)
                {
                    p.Add(new Piece(", ", null));
                    p.Add(new Piece(Case("korter"), "street.unit-designator"));
                    p.Add(new Piece(" ", null));
                }
                else
                {
                    p.Add(new Piece(style == 0 ? "-" : "/", null));
                }

                p.Add(new Piece(_a.Unit, "street.unit-number"));
                _events.Add("unit:register");
            }
            else if (_a.Country == "LV" && _r.Chance(_rt.SyntheticUnit))
            {
                // Latvian flats: "Brīvības iela 10 - 5", "Brīvības iela 10-5", "Brīvības iela 10, dz. 5".
                int style = _r.Weighted([0.5, 0.2, 0.3]);
                string flat = (1 + _r.Int(80)).ToString(CultureInfo.InvariantCulture);
                if (style == 2)
                {
                    p.Add(new Piece(", ", null));
                    p.Add(new Piece(Case("dz."), "street.unit-designator"));
                    p.Add(new Piece(" ", null));
                }
                else
                {
                    p.Add(new Piece(style == 0 ? " - " : "-", null));
                }

                p.Add(new Piece(flat, "street.unit-number"));
                _events.Add("unit:synthetic");
            }
            else if (_a.Country is "NO" or "CZ" && _r.Chance(_rt.SyntheticUnit / 2))
            {
                // Norwegian flat numbers are written "H0201" (floor and flat), Czech ones "byt 5".
                bool no = _a.Country == "NO";
                string designator = no ? "H" : _r.Pick(new[] { "byt", "č. bytu" });
                string flat = no ? (1 + _r.Int(4)).ToString("D2", CultureInfo.InvariantCulture) + _r.Int(20).ToString("D2", CultureInfo.InvariantCulture) : (1 + _r.Int(60)).ToString(CultureInfo.InvariantCulture);
                if (no)
                {
                    p.Add(new Piece(" ", null));
                    p.Add(new Piece(designator + flat, "street.unit-number"));
                }
                else
                {
                    p.Add(new Piece(", ", null));
                    p.Add(new Piece(Case(designator), "street.unit-designator"));
                    p.Add(new Piece(" ", null));
                    p.Add(new Piece(flat, "street.unit-number"));
                }

                _events.Add("unit:synthetic");
            }

            else if (_a.Country is "DK" or "SE" or "FI" && _r.Chance(_rt.SyntheticUnit / 2))
            {
                AddNordicUnit(p);
            }

            return p;
        }

        // Flat designations drawn for the countries whose registers carry none: Danish floor and side ("2. th."), Swedish apartment number
        // ("lgh 1101"), Finnish flat number after the staircase letter ("5 B 12").
        private void AddNordicUnit(List<Piece> p)
        {
            switch (_a.Country)
            {
                case "DK":
                    string floor = _r.Pick(new[] { "st.", "1.", "2.", "3.", "4." });
                    string side = _r.Pick(new[] { "th.", "tv.", "mf.", string.Empty });
                    p.Add(new Piece(", ", null));
                    p.Add(new Piece(Case(side.Length == 0 ? floor : floor + " " + side), "street.unit-number"));
                    break;
                case "SE":
                    p.Add(new Piece(" ", null));
                    p.Add(new Piece(Case(_r.Pick(new[] { "lgh", "lgh", "Lgh" })), "street.unit-designator"));
                    p.Add(new Piece(" ", null));
                    p.Add(new Piece((1000 + (1 + _r.Int(9)) * 100 + 1 + _r.Int(5)).ToString(CultureInfo.InvariantCulture), "street.unit-number"));
                    break;
                default:
                    if (_a.Suffix.Length == 0)
                    {
                        return;
                    }

                    p.Add(new Piece(" ", null));
                    p.Add(new Piece((1 + _r.Int(120)).ToString(CultureInfo.InvariantCulture), "street.unit-number"));
                    break;
            }

            _events.Add("unit:synthetic");
        }

        private List<Piece> PostBoxPieces(string cc)
        {
            if (C1)
            {
                return PostBoxC1(cc);
            }

            string[] designators = cc switch
            {
                "NO" => ["Postboks", "Postboks", "Pb."],
                "CZ" => ["Poštovní přihrádka", "P.O.Box", "PO Box"],
                "LV" => ["Pasta kastīte", "p/k", "PK"],
                "DK" => ["Postboks", "Postboks", "Pb."],
                "SE" => ["Box", "Box", "Postbox"],
                "FI" => _a.Language == "sv" ? ["PB", "PB", "Postbox"] : ["PL", "PL", "Postilokero"],
                "IS" => ["Pósthólf", "Pósthólf", "Pósth."],
                _ => ["Postkast", "PK", "Postkast"],
            };
            string number = (1 + _r.Int(_r.Chance(0.5) ? 999 : 9999)).ToString(CultureInfo.InvariantCulture);
            return [new Piece(Case(_r.Pick(designators)), "street.box-designator"), new Piece(" ", null), new Piece(number, "street.box-number")];
        }

        // The last line, and any line of its own for a district or parish.
        private List<Piece> LastLine(string cc, out List<Piece> dependentLine)
        {
            if (C1)
            {
                return LastLineC1(cc, out dependentLine);
            }

            dependentLine = [];
            bool noPostal = _r.Chance(_rt.MissingPostalCode);
            bool noPlace = !noPostal && _r.Chance(_rt.MissingLocality);
            string place = _a.Locality;
            if (_mode == 3 && cc is "NO" or "FI" or "SE")
            {
                place = place.ToUpperInvariant();
            }
            else if (cc == "EE" && _r.Chance(0.1))
            {
                place += " linn";
            }
            else
            {
                place = Case(place);
            }

            List<Piece> p = [];
            bool useDependent = _a.Dependent.Length > 0 && _r.Chance(_rt.Dependent);
            if (useDependent)
            {
                _events.Add("dependent-locality");
            }

            string postal = _a.PostalCode;
            switch (cc)
            {
                case "NO":
                case "EE":
                case "DK":
                case "SE":
                case "FI":
                case "IS":
                    // "0155 Oslo", "NO-0155 Oslo", "EE-10115 Tallinn".
                    if (!noPostal)
                    {
                        if (_r.Chance(_rt.CountryCodePrefix))
                        {
                            p.Add(new Piece(cc == "FI" && _a.Region == "19" ? "AX" : cc, "country"));
                            p.Add(new Piece("-", null));
                            _events.Add("code-prefix");
                        }

                        // Swedish postcodes are written "123 45", sometimes without the space.
                        p.Add(new Piece(cc == "SE" && _r.Chance(0.1) ? postal.Replace(" ", string.Empty, StringComparison.Ordinal) : postal, "postal-code"));
                    }

                    if (!noPlace)
                    {
                        if (p.Count > 0)
                        {
                            p.Add(new Piece(" ", null));
                        }

                        p.Add(new Piece(place, "locality"));
                        if (cc == "EE" && _a.Region.Length > 0 && _a.Admin.Length > 0 && _r.Chance(0.5))
                        {
                            // Rural Estonian addresses end with the county: "48216 Jõgeva maakond".
                            p.Add(new Piece(" ", null));
                            p.Add(new Piece(Case(_a.Region), "admin-area"));
                        }
                    }

                    if (cc == "EE" && _a.Admin.Length > 0 && _r.Chance(0.6))
                    {
                        dependentLine = [new Piece(Case(_a.Admin), "dependent-locality")];
                        _events.Add("municipality");
                    }

                    break;
                case "CZ":
                    // "110 00 Praha 1": the postcode is written in two groups.
                    if (!noPostal)
                    {
                        if (_r.Chance(_rt.CountryCodePrefix))
                        {
                            p.Add(new Piece("CZ", "country"));
                            p.Add(new Piece("-", null));
                            _events.Add("code-prefix");
                        }

                        p.Add(new Piece(_r.Chance(0.9) ? postal[..3] + " " + postal[3..] : postal, "postal-code"));
                    }

                    if (!noPlace)
                    {
                        if (p.Count > 0)
                        {
                            p.Add(new Piece(" ", null));
                        }

                        p.Add(new Piece(place, "locality"));
                    }

                    break;
                default:
                    // Latvia: "Rīga, LV-1010", "Vecates pag., Valmieras nov., LV-4211"; the LV- belongs to the postcode.
                    if (!noPlace)
                    {
                        p.Add(new Piece(place, "locality"));
                        if (_a.Admin.Length > 0 && _r.Chance(0.8))
                        {
                            p.Add(new Piece(", ", null));
                            p.Add(new Piece(Case(_a.Admin), "admin-area"));
                        }
                    }

                    if (!noPostal)
                    {
                        if (p.Count > 0)
                        {
                            p.Add(new Piece(_r.Chance(0.9) ? ", " : " ", null));
                        }

                        p.Add(new Piece(_r.Chance(0.9) ? "LV-" + postal : postal, "postal-code"));
                    }

                    break;
            }

            // A district or parish (Czech part of a municipality, Latvian pagasts, Estonian city district) is its own line before the last one;
            // an Estonian rural address may already have its municipality there.
            if (useDependent && dependentLine.Count == 0)
            {
                dependentLine = [new Piece(Case(_a.Dependent), "dependent-locality")];
            }

            if (p.Count == 0)
            {
                p.Add(new Piece(Case(_a.Locality), "locality"));
            }

            return p;
        }

        private string Pick(string kind, Prng r) => r.Pick(_o._pools[kind + "/" + _a.Language]);

        private List<Piece> NameLine(bool organization)
        {
            string family = Pick("family", _r);
            string text;
            if (organization)
            {
                text = Pick("org", _r).Replace("{fam}", family, StringComparison.Ordinal).Replace("{word}", Pick("word", _r), StringComparison.Ordinal)
                    .Replace("{city}", _a.Locality, StringComparison.Ordinal);
            }
            else
            {
                string given = Pick("given", _r);
                text = _r.Weighted([0.65, 0.2, 0.15]) switch
                {
                    0 => given + " " + family,
                    1 => given[0] + ". " + family,
                    _ => family + " " + given,
                };
            }

            return [new Piece(Case(text), organization ? "organization" : "recipient")];
        }

        // A one-letter slip inside a word of a name piece: swapped neighbours, a dropped or a doubled letter.
        private void Typo(List<Piece> pieces)
        {
            List<Piece> candidates = pieces.Where(p => p.Label is "street.name" or "locality" && p.Text.Length >= 5).ToList();
            if (candidates.Count == 0)
            {
                return;
            }

            Piece target = _r.Pick(candidates);
            string t = target.Text;
            for (int tries = 0; tries < 8; tries++)
            {
                int i = 1 + _r.Int(t.Length - 2);
                if (!char.IsLetter(t[i]) || !char.IsLetter(t[i - 1]))
                {
                    continue;
                }

                target.Text = _r.Int(3) switch
                {
                    0 => t[..(i - 1)] + t[i] + t[i - 1] + t[(i + 1)..],
                    1 => t[..i] + t[(i + 1)..],
                    _ => t[..i] + t[i] + t[i..],
                };
                _events.Add("typo");
                return;
            }
        }
    }
}
