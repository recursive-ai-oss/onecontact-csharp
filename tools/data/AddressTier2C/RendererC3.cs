using System.Globalization;
using System.Text;
using OneContact.Data.AddressUsCa;

namespace OneContact.Data.AddressTier2C;

/// <summary>
/// Renders one address of Croatia, Hungary, Romania or Australia as a free-text example through the postal layouts of its country.
/// Street before number in Croatia, Hungary and Romania (Hungary and Romania may also put the postcode first), number before street in
/// Australia. Only labelled pieces carry letters and digits, so every one of them lies inside a span. Rates are the shared
/// <see cref="Rates"/> and are written into the manifest by the caller.
/// </summary>
internal sealed class RendererC3
{
    /// <summary>Changes whenever a template, rate or draw order of these four countries changes.</summary>
    internal const string Version = "tier2c3-2026-11-06.1";

    internal static bool Handles(string country) => country is "HR" or "HU" or "RO" or "AU";

    private static readonly Dictionary<string, string[]> CountryNames = new(StringComparer.Ordinal)
    {
        ["HR"] = ["Hrvatska", "Croatia", "HRVATSKA"],
        ["HU"] = ["Magyarország", "Hungary", "MAGYARORSZÁG"],
        ["RO"] = ["România", "Romania", "ROMÂNIA"],
        ["AU"] = ["Australia", "AUSTRALIA"],
    };

    // The state names Australia Post accepts in place of the abbreviation.
    private static readonly Dictionary<string, string> StateNames = new(StringComparer.Ordinal)
    {
        ["NSW"] = "New South Wales", ["VIC"] = "Victoria", ["QLD"] = "Queensland", ["SA"] = "South Australia", ["WA"] = "Western Australia",
        ["TAS"] = "Tasmania", ["NT"] = "Northern Territory", ["ACT"] = "Australian Capital Territory",
    };

    // Street types and the abbreviations Australia Post's guide lists for them.
    private static readonly Dictionary<string, string> AuTypeAbbreviations = new(StringComparer.Ordinal)
    {
        ["Street"] = "St", ["Road"] = "Rd", ["Avenue"] = "Ave", ["Drive"] = "Dr", ["Court"] = "Ct", ["Place"] = "Pl", ["Crescent"] = "Cres",
        ["Lane"] = "Ln", ["Terrace"] = "Tce", ["Boulevard"] = "Bvd", ["Parade"] = "Pde", ["Highway"] = "Hwy", ["Close"] = "Cl",
        ["Circuit"] = "Cct", ["Grove"] = "Gr", ["Esplanade"] = "Esp", ["Square"] = "Sq", ["Promenade"] = "Prom", ["Way"] = "Way",
    };

    // Romanian street types in the form the register writes them, and the short forms people use.
    private static readonly Dictionary<string, string> RoTypeAbbreviations = new(StringComparer.Ordinal)
    {
        ["Strada"] = "Str.", ["Bulevardul"] = "Bd.", ["Aleea"] = "Al.", ["Șoseaua"] = "Șos.", ["Calea"] = "Cal.", ["Piața"] = "P-ța",
        ["Splaiul"] = "Spl.", ["Drumul"] = "Dr.", ["Intrarea"] = "Int.", ["Fundătura"] = "Fd.",
    };

    private readonly Rates _rates;
    private readonly ulong _seed;
    private readonly Dictionary<string, List<string>> _pools = new(StringComparer.Ordinal);

    internal RendererC3(string templatesDir, Rates rates, ulong seed)
    {
        _rates = rates;
        _seed = seed;
        string path = Path.Combine(templatesDir, "tier2c3-names.tsv");
        Sha256 = Renderer.Sha256Of(path);
        foreach (string line in File.ReadLines(path))
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

    internal Example Render(Addr a) => new Ctx(this, a, Prng.For(_seed, a.Id)).Run();

    private sealed class Ctx
    {
        private readonly RendererC3 _o;
        private readonly Addr _a;
        private readonly Prng _r;
        private readonly Rates _rt;
        private readonly List<string> _events = [];
        private int _mode;

        internal Ctx(RendererC3 o, Addr a, Prng r)
        {
            _o = o;
            _a = a;
            _r = r;
            _rt = o._rates;
        }

        private string Cc => _a.Country;

        internal Example Run()
        {
            _mode = _r.Weighted(_rt.CaseWeights);
            _events.Add("case:" + new[] { "asis", "upper", "lower", "postal" }[_mode]);
            bool label = _r.Chance(_rt.LabelLayout);
            bool hasOrg = _r.Chance(label ? _rt.OrganizationLabel : _rt.OrganizationLine);
            bool hasRecipient = _r.Chance(label ? _rt.RecipientLabel : _rt.RecipientLine);
            bool box = _r.Chance(_rt.PostBox);
            // Hungarian mail is addressed city, street, postcode; everyday writing puts the postcode first.
            bool cityFirst = Cc == "HU" && _r.Chance(label ? 0.5 : 0.3);

            List<Piece> delivery = Delivery();
            List<Piece>? boxLine = null;
            if (box)
            {
                boxLine = PostBoxPieces();
                _events.Add("post-box");
                if (!_r.Chance(_rt.PostBoxWithStreet))
                {
                    delivery = [];
                }
            }

            List<Piece> last = LastLine(cityFirst, out List<Piece> dependent, out List<Piece>? postalLine);
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

            if (cityFirst)
            {
                lines.Add(last);
                _events.Add("city-first");
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

            if (cityFirst)
            {
                if (postalLine is not null)
                {
                    lines.Add(postalLine);
                }
            }
            else
            {
                lines.Add(last);
            }

            if (_r.Chance(label ? _rt.CountryLabel : _rt.CountryLine))
            {
                string[] names = CountryNames[Cc];
                lines.Add([new Piece(names[_r.Weighted(new[] { 0.7, 0.2, 0.1 }[..names.Length])], "country")]);
                _events.Add("country");
            }

            // Only Hungary has two orders worth telling apart in the layout name.
            string style = Cc != "HU" ? string.Empty : cityFirst ? "city-first." : "postcode-first.";
            if (label)
            {
                List<Piece> all = [];
                for (int i = 0; i < lines.Count; i++)
                {
                    if (i > 0)
                    {
                        all.Add(new Piece("\n", null));
                    }

                    all.AddRange(lines[i]);
                }

                return Finish(all, $"{Cc.ToLowerInvariant()}.label.{style}{_a.Language}");
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

            return Finish(flat, $"{Cc.ToLowerInvariant()}.line.{new[] { "comma", "space", "dash", "semicolon", "slash" }[s]}.{style}{_a.Language}");
        }

        private Example Finish(List<Piece> pieces, string layout)
        {
            if (_r.Chance(_rt.Typo))
            {
                Typo(pieces);
            }

            if (Cc == "RO" && _r.Chance(0.2))
            {
                // Many Romanian texts still write the cedilla forms (ş ţ) the old code pages had.
                foreach (Piece p in pieces)
                {
                    p.Text = p.Text.Replace('ș', 'ş').Replace('ț', 'ţ').Replace('Ș', 'Ş').Replace('Ț', 'Ţ');
                }

                _events.Add("cedilla");
            }

            if (Cc != "AU" && _r.Chance(0.07))
            {
                // Typed without diacritics.
                foreach (Piece p in pieces)
                {
                    p.Text = StripDiacritics(p.Text);
                }

                _events.Add("ascii");
            }

            if (_r.Chance(_rt.DoubleSpace))
            {
                List<Piece> spaces = pieces.Where(p => p.Label is null && p.Text == " ").ToList();
                if (spaces.Count > 0)
                {
                    _r.Pick(spaces).Text = "  ";
                    _events.Add("double-space");
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

        private static string StripDiacritics(string t)
        {
            StringBuilder sb = new(t.Length);
            foreach (char c in t.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c switch { 'đ' => 'd', 'Đ' => 'D', _ => c });
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private string Case(string t) => _mode switch
        {
            1 => t.ToUpperInvariant(),
            2 => t.ToLowerInvariant(),
            // Australia Post asks for capitals without punctuation; the other three countries have no separate postal casing.
            3 when Cc == "AU" => t.ToUpperInvariant(),
            _ => t,
        };

        private string Digits(string t) => _mode == 2 ? t.ToLowerInvariant() : t;

        private List<Piece> Delivery()
        {
            return Cc switch
            {
                "HR" => CroatiaDelivery(),
                "HU" => HungaryDelivery(),
                "RO" => RomaniaDelivery(),
                _ => AustraliaDelivery(),
            };
        }

        // "Brežna ulica 33", "Ulica Stjepana Radića 122A", flats after a slash ("12/3").
        private List<Piece> CroatiaDelivery()
        {
            List<Piece> p = [new Piece(Case(_a.Street), "street.name"), new Piece(" ", null)];
            string number = _a.HouseNumber + (_a.Suffix.Length > 0 ? (_r.Chance(_rt.SpacedSuffix / 2) ? " " : string.Empty) + _a.Suffix : string.Empty);
            p.Add(new Piece(Digits(number), "street.house-number"));
            if (_r.Chance(_rt.SyntheticUnit / 2))
            {
                p.Add(new Piece("/", null));
                p.Add(new Piece((1 + _r.Int(40)).ToString(CultureInfo.InvariantCulture), "street.unit-number"));
                _events.Add("unit:synthetic");
            }

            return p;
        }

        // "Kossuth Lajos utca 12.", "Fő utca 3/A.": the number follows the street and may end with a full stop.
        private List<Piece> HungaryDelivery()
        {
            List<Piece> p = [new Piece(Case(_a.Street), "street.name"), new Piece(" ", null)];
            string number = _a.HouseNumber + (_a.Suffix.Length > 0 ? "/" + _a.Suffix : string.Empty);
            if (_r.Chance(0.55))
            {
                number += ".";
            }

            p.Add(new Piece(Digits(number), "street.house-number"));
            return p;
        }

        // "Strada Mihai Eminescu nr. 12", "Str. Mihai Eminescu, nr. 12, bl. 4, ap. 15". The "nr." belongs to the number span.
        private List<Piece> RomaniaDelivery()
        {
            string street = _a.Street;
            int space = street.IndexOf(' ', StringComparison.Ordinal);
            if (space > 0 && RoTypeAbbreviations.TryGetValue(street[..space], out string? abbreviation) && _r.Chance(0.35))
            {
                street = abbreviation + street[space..];
                _events.Add("type-abbreviated");
            }

            List<Piece> p = [new Piece(Case(street), "street.name")];
            if (_a.HouseNumber.Length == 0)
            {
                return p;
            }

            p.Add(new Piece(_r.Chance(0.55) ? ", " : " ", null));
            string number = _a.HouseNumber + _a.Suffix;
            int style = _r.Weighted([0.5, 0.2, 0.3]);
            string text = style switch { 0 => "nr. " + number, 1 => "Nr. " + number, _ => number };
            p.Add(new Piece(Digits(text), "street.house-number"));
            if (_r.Chance(_rt.SyntheticUnit * 1.6))
            {
                // Flats in Romanian cities are written by block, staircase and flat.
                bool block = _r.Chance(0.4);
                if (block)
                {
                    p.Add(new Piece(", ", null));
                    p.Add(new Piece(Case(_r.Pick(new[] { "bl.", "Bl.", "bloc" })), "street.unit-designator"));
                    p.Add(new Piece(" ", null));
                    p.Add(new Piece(_r.Pick(new[] { "A1", "B2", "C", "12", "5", "D3", "7A", "20" }), "street.unit-number"));
                }

                p.Add(new Piece(", ", null));
                p.Add(new Piece(Case(_r.Pick(new[] { "ap.", "Ap.", "apartament" })), "street.unit-designator"));
                p.Add(new Piece(" ", null));
                p.Add(new Piece((1 + _r.Int(80)).ToString(CultureInfo.InvariantCulture), "street.unit-number"));
                _events.Add(block ? "unit:block-flat" : "unit:flat");
            }

            return p;
        }

        // "45 Smith Street", "Unit 3, 45 Smith Street", "3/45 Smith Street", "45-47 Smith St"; the postal casing is "UNIT 3 45 SMITH ST".
        private List<Piece> AustraliaDelivery()
        {
            string street = _a.Street;
            int space = street.LastIndexOf(' ');
            if (space > 0 && AuTypeAbbreviations.TryGetValue(street[(space + 1)..], out string? abbreviation) && (_mode == 3 || _r.Chance(0.4)))
            {
                street = street[..(space + 1)] + abbreviation;
                _events.Add("type-abbreviated");
            }

            string number = _a.HouseNumber + _a.Suffix;
            if (_a.Suffix.Length == 0 && _r.Chance(0.04))
            {
                number += "-" + (int.Parse(_a.HouseNumber, CultureInfo.InvariantCulture) + 2);
            }

            List<Piece> p = [];
            bool unit = _r.Chance(0.2);
            if (unit)
            {
                string flat = (1 + _r.Int(60)).ToString(CultureInfo.InvariantCulture);
                if (_r.Chance(0.4))
                {
                    p.Add(new Piece(flat, "street.unit-number"));
                    p.Add(new Piece("/", null));
                    _events.Add("unit:slash");
                }
                else
                {
                    string designator = _r.Pick(new[] { "Unit", "Unit", "Unit", "Apartment", "Flat", "Suite", "Shop", "Apt" });
                    p.Add(new Piece(Case(designator), "street.unit-designator"));
                    p.Add(new Piece(" ", null));
                    p.Add(new Piece(flat, "street.unit-number"));
                    p.Add(new Piece(_mode == 3 || _r.Chance(0.3) ? " " : ", ", null));
                    _events.Add("unit:designator");
                }
            }

            p.Add(new Piece(Digits(number), "street.house-number"));
            p.Add(new Piece(" ", null));
            p.Add(new Piece(Case(street), "street.name"));
            return p;
        }

        private List<Piece> PostBoxPieces()
        {
            string[] designators = Cc switch
            {
                "HR" => ["Poštanski pretinac", "p.p.", "PP", "P.P."],
                "HU" => ["Pf.", "Postafiók", "Pf"],
                "RO" => ["CP", "C.P.", "Căsuța poștală", "Căsuța Poștală"],
                _ => ["PO Box", "PO Box", "P.O. Box", "GPO Box", "Locked Bag"],
            };
            string number = (1 + _r.Int(_r.Chance(0.5) ? 999 : 9999)).ToString(CultureInfo.InvariantCulture);
            return [new Piece(Case(_r.Pick(designators)), "street.box-designator"), new Piece(" ", null), new Piece(number, "street.box-number")];
        }

        // The last line, a line of its own for a settlement or district, and for Hungary's city-first order the postcode line.
        private List<Piece> LastLine(bool cityFirst, out List<Piece> dependentLine, out List<Piece>? postalLine)
        {
            dependentLine = [];
            postalLine = null;
            bool noPostal = _r.Chance(_rt.MissingPostalCode);
            bool noPlace = !noPostal && _r.Chance(_rt.MissingLocality);
            if (Cc == "RO" && (_a.Street == _a.Locality || _a.Street == "Sat " + _a.Locality) && _r.Chance(0.5))
            {
                // A village address already names the village in its first line; the last line often leaves it out.
                noPlace = true;
                _events.Add("village-once");
            }

            string place = Case(_a.Locality);
            string postal = _a.PostalCode;
            List<Piece> p = [];

            Piece? CodePiece()
            {
                if (noPostal)
                {
                    return null;
                }

                return new Piece(postal, "postal-code");
            }

            List<Piece> Prefixed(Piece? code)
            {
                List<Piece> r = [];
                if (code is null)
                {
                    return r;
                }

                if (_r.Chance(_rt.CountryCodePrefix))
                {
                    r.Add(new Piece(Cc == "HU" && _r.Chance(0.5) ? "H" : Cc, "country"));
                    r.Add(new Piece("-", null));
                    _events.Add("code-prefix");
                }

                r.Add(code);
                return r;
            }

            switch (Cc)
            {
                case "HR":
                    // "10000 Zagreb"; a settlement that differs from the post office goes on its own line.
                    p.AddRange(Prefixed(CodePiece()));
                    if (!noPlace)
                    {
                        if (p.Count > 0)
                        {
                            p.Add(new Piece(" ", null));
                        }

                        p.Add(new Piece(place, "locality"));
                    }

                    if (_a.Dependent.Length > 0 && _r.Chance(_rt.Dependent))
                    {
                        dependentLine = [new Piece(Case(_a.Dependent), "dependent-locality")];
                        _events.Add("dependent-locality");
                    }

                    break;
                case "HU":
                    if (cityFirst)
                    {
                        // Mail form: the city on its own line, the postcode alone on the last line.
                        if (!noPlace)
                        {
                            p.Add(new Piece(place, "locality"));
                        }

                        List<Piece> code = Prefixed(CodePiece());
                        postalLine = code.Count > 0 ? code : null;
                        if (p.Count == 0)
                        {
                            p.Add(new Piece(Case(_a.Locality), "locality"));
                        }
                    }
                    else
                    {
                        p.AddRange(Prefixed(CodePiece()));
                        if (!noPlace)
                        {
                            if (p.Count > 0)
                            {
                                p.Add(new Piece(" ", null));
                            }

                            p.Add(new Piece(place, "locality"));
                        }
                    }

                    break;
                case "RO":
                    RomaniaLastLine(p, noPlace, place, Prefixed(CodePiece()), out dependentLine);
                    break;
                default:
                    // "Sydney NSW 2000", "Sydney, NSW 2000", "SYDNEY NSW 2000".
                    if (!noPlace)
                    {
                        p.Add(new Piece(place, "locality"));
                    }

                    if (_a.Admin.Length > 0)
                    {
                        if (p.Count > 0)
                        {
                            p.Add(new Piece(_r.Chance(0.2) ? ", " : " ", null));
                        }

                        bool full = _mode != 3 && _r.Chance(0.2);
                        p.Add(new Piece(Case(full ? StateNames[_a.Admin] : _a.Admin), "admin-area"));
                        if (full)
                        {
                            _events.Add("state-name");
                        }
                    }

                    if (!noPostal)
                    {
                        if (p.Count > 0)
                        {
                            p.Add(new Piece(" ", null));
                        }

                        p.Add(new Piece(postal, "postal-code"));
                    }

                    break;
            }

            if (p.Count == 0)
            {
                // Never an empty last line: a missing place and a missing code fall back to the place.
                p.Add(new Piece(Case(_a.Locality), "locality"));
            }

            return p;
        }

        // "400114 Cluj-Napoca, jud. Cluj", "Cluj-Napoca, jud. Cluj, 400114", "011357 București, Sector 1".
        private void RomaniaLastLine(List<Piece> p, bool noPlace, string place, List<Piece> code, out List<Piece> dependentLine)
        {
            dependentLine = [];
            bool postcodeFirst = _r.Chance(0.62);
            List<Piece> tail = [];
            if (_a.Dependent.Length > 0)
            {
                // A Bucharest sector follows the city.
                tail.Add(new Piece(", ", null));
                tail.Add(new Piece(Case(_a.Dependent), "dependent-locality"));
                _events.Add("dependent-locality");
            }
            else if (_a.Admin.Length > 0 && _r.Chance(0.5))
            {
                string county = _a.Admin;
                string text = _r.Weighted([0.5, 0.3, 0.2]) switch { 0 => "jud. " + county, 1 => "județul " + county, _ => "Județ " + county };
                tail.Add(new Piece(", ", null));
                tail.Add(new Piece(Case(text), "admin-area"));
                _events.Add("county");
            }

            if (postcodeFirst)
            {
                p.AddRange(code);
                if (!noPlace)
                {
                    if (p.Count > 0)
                    {
                        p.Add(new Piece(" ", null));
                    }

                    p.Add(new Piece(place, "locality"));
                    p.AddRange(tail);
                }
            }
            else
            {
                if (!noPlace)
                {
                    p.Add(new Piece(place, "locality"));
                    p.AddRange(tail);
                }

                if (code.Count > 0)
                {
                    if (p.Count > 0)
                    {
                        p.Add(new Piece(", ", null));
                    }

                    p.AddRange(code);
                }
            }

        }

        private string Pick(string kind) => _r.Pick(_o._pools[kind + "/" + _a.Language]);

        private List<Piece> NameLine(bool organization)
        {
            string family = Pick("family");
            string text;
            if (organization)
            {
                text = Pick("org").Replace("{fam}", family, StringComparison.Ordinal).Replace("{word}", Pick("word"), StringComparison.Ordinal)
                    .Replace("{city}", _a.Locality, StringComparison.Ordinal);
            }
            else
            {
                string given = Pick("given");
                // Hungarian writes the family name first.
                bool familyFirst = _a.Language == "hu";
                text = _r.Weighted([0.65, 0.2, 0.15]) switch
                {
                    0 => familyFirst ? family + " " + given : given + " " + family,
                    1 => familyFirst ? family + " " + given[0] + "." : given[0] + ". " + family,
                    _ => familyFirst ? given + " " + family : family + " " + given,
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
