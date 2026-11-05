using System.Globalization;
using System.Text;
using OneContact.Data.AddressUsCa;

namespace OneContact.Data.AddressTier2B;

/// <summary>The rates the generator draws with. They are written into the manifest, so a run states what it did.</summary>
internal sealed class RatesB
{
    /// <summary>Casing of the example: as the source writes it, UPPER, lower, and the postal service's form (upper-case locality).</summary>
    internal double[] CaseWeights { get; init; } = [0.62, 0.13, 0.07, 0.18];

    internal double LabelLayout { get; init; } = 0.52;

    internal double RecipientLabel { get; init; } = 0.35;

    internal double RecipientLine { get; init; } = 0.10;

    internal double OrganizationLabel { get; init; } = 0.20;

    internal double OrganizationLine { get; init; } = 0.06;

    internal double CountryLabel { get; init; } = 0.07;

    internal double CountryLine { get; init; } = 0.10;

    internal double CountryAbroad { get; init; } = 0.20;

    internal double MissingPostalCode { get; init; } = 0.04;

    internal double MissingLocality { get; init; } = 0.012;

    internal double SyntheticUnit { get; init; } = 0.09;

    internal double PostBox { get; init; } = 0.02;

    internal double PostBoxWithStreet { get; init; } = 0.25;

    internal double AlternateLanguage { get; init; } = 0.45;

    internal double Abbreviation { get; init; } = 0.15;

    internal double Typo { get; init; } = 0.05;

    internal double DoubleSpace { get; init; } = 0.01;

    internal double Admin { get; init; } = 0.30;

    internal double Dependent { get; init; } = 0.50;

    internal double CountryCodePrefix { get; init; } = 0.06;
}

/// <summary>
/// Renders one address as a free-text example through the postal layouts of its country: the delivery line (number before the street in
/// the English-speaking countries, after it elsewhere), the postcode and locality line, and the optional lines around them. Only labelled
/// pieces carry letters and digits, so every one of them lies inside a span.
/// </summary>
internal sealed class RendererB
{
    /// <summary>Changes whenever a template, rate or draw order changes, so a manifest says which renderer made its files.</summary>
    internal const string Version = "tier2b-2026-11-05.1";

    private readonly RatesB _rates;
    private readonly ulong _seed;
    private readonly Dictionary<string, List<string>> _pools = new(StringComparer.Ordinal);

    internal RendererB(string templatesDir, RatesB rates, ulong seed)
    {
        _rates = rates;
        _seed = seed;
        string path = Path.Combine(templatesDir, "tier2b-names.tsv");
        NamesSha256 = Sha256Of(path);
        StreetsSha256 = Sha256Of(Path.Combine(templatesDir, "tier2b-streets.tsv"));
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

    internal string NamesSha256 { get; }

    internal string StreetsSha256 { get; }

    internal RatesB Rates => _rates;

    internal static string Sha256Of(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    internal Example Render(RecB a) => new Ctx(this, a, Prng.For(_seed, a.Id)).Run();

    private static string NameGroup(string lang) => lang switch
    {
        "es" or "gl" => "es",
        "ca" => "ca",
        "it" or "de" => "it",
        "pt" => "pt",
        "mt" => "mt",
        _ => "en",
    };

    private string PickName(string kind, string group, Prng r) => r.Pick(_pools[kind + "/" + group]);

    private sealed class Ctx
    {
        private readonly RendererB _o;
        private readonly RecB _a;
        private readonly Prng _r;
        private readonly RatesB _rt;
        private readonly List<string> _events = [];
        private int _mode;
        private string _lang = string.Empty;

        internal Ctx(RendererB o, RecB a, Prng r)
        {
            _o = o;
            _a = a;
            _r = r;
            _rt = o._rates;
        }

        private bool EnglishOrder => _a.Country is "GB" or "JE" or "GG" or "IM" or "IE" || (_a.Country == "MT" && _lang == "en") || (_a.Country == "AD" && _lang == "fr");

        internal Example Run()
        {
            string cc = _a.Country;
            _mode = _r.Weighted(_rt.CaseWeights);
            _events.Add("case:" + new[] { "asis", "upper", "lower", "postal" }[_mode]);
            bool label = _r.Chance(_rt.LabelLayout);
            _lang = _a.Language;
            string type = _a.Type;
            string name = _a.Name;
            string locality = _a.Locality;
            bool typeBefore = _a.TypeBefore;
            if (_a.AltName.Length > 0 && _r.Chance(_rt.AlternateLanguage))
            {
                _lang = _a.AltLanguage;
                type = _a.AltType;
                name = _a.AltName;
                typeBefore = _lang is not ("cy" or "gd" or "de");
                locality = _a.LocalityAlt.Length > 0 && _lang == "de" ? _a.LocalityAlt : locality;
                _events.Add("alternate-language");
            }
            else if (cc == "PT" && _a.LocalityAlt.Length > 0 && _r.Chance(0.4))
            {
                locality = _a.LocalityAlt;
                _events.Add("post-office-name");
            }
            else if (cc == "MT" && _a.LocalityAlt.Length > 0 && _r.Chance(0.3))
            {
                locality = _a.LocalityAlt;
                _events.Add("alternate-locality");
            }

            bool hasOrg = _r.Chance(label ? _rt.OrganizationLabel : _rt.OrganizationLine);
            bool hasRecipient = _r.Chance(label ? _rt.RecipientLabel : _rt.RecipientLine);
            bool box = _r.Chance(_rt.PostBox);

            // The delivery line.
            List<Piece> street = StreetPieces(type, name, typeBefore);
            Piece number = new(NumberText(), "street.house-number");
            List<Piece> unit = _r.Chance(_rt.SyntheticUnit) ? UnitPieces() : [];
            bool unitAbove = false;
            List<Piece> delivery = [];
            if (EnglishOrder || (cc == "MT" && _r.Chance(0.5)))
            {
                if (unit.Count > 0)
                {
                    unitAbove = label && _r.Chance(0.5);
                    if (!unitAbove)
                    {
                        delivery.AddRange(unit);
                        delivery.Add(new Piece(_r.Chance(0.75) ? ", " : " ", null));
                        unit = [];
                    }
                }

                delivery.Add(number);
                delivery.Add(new Piece(cc == "MT" && _r.Chance(0.55) ? ", " : " ", null));
                delivery.AddRange(street);
            }
            else
            {
                delivery.AddRange(street);
                delivery.Add(new Piece(NumberSeparator(cc), null));
                delivery.Add(number);
                if (unit.Count > 0)
                {
                    unitAbove = label && _r.Chance(0.4);
                    if (!unitAbove)
                    {
                        delivery.Add(new Piece(_r.Chance(0.7) ? ", " : " ", null));
                        delivery.AddRange(unit);
                        unit = [];
                    }
                }
            }

            if (unit.Count > 0)
            {
                _events.Add("unit:synthetic");
            }

            List<Piece>? boxLine = null;
            if (box)
            {
                boxLine = PostBoxPieces(cc);
                _events.Add("post-box");
                if (!_r.Chance(_rt.PostBoxWithStreet))
                {
                    delivery = [];
                    unit = [];
                    unitAbove = false;
                }
            }

            // Lines of the example in postal order.
            List<List<Piece>> lines = [];
            string group = NameGroup(_lang);
            if (hasRecipient)
            {
                lines.Add(NameLine(false, group, locality));
                _events.Add("recipient");
            }

            if (hasOrg)
            {
                lines.Add(NameLine(true, group, locality));
                _events.Add("organization");
            }

            if (unitAbove && unit.Count > 0)
            {
                lines.Add(unit);
                _events.Add("unit:line");
            }

            if (delivery.Count > 0)
            {
                lines.Add(delivery);
            }

            if (_a.Dependent.Length > 0 && _r.Chance(_rt.Dependent))
            {
                lines.Add([new Piece(Case(_a.Dependent, false), "dependent-locality")]);
                _events.Add("dependent-locality");
            }

            if (boxLine is not null)
            {
                lines.Add(boxLine);
            }

            lines.AddRange(Tail(cc, locality, label));
            bool abroad = cc is "SM" or "VA" or "AD" or "MT" or "IE" or "JE" or "GG" or "IM";
            if (_r.Chance(abroad ? _rt.CountryAbroad : label ? _rt.CountryLabel : _rt.CountryLine))
            {
                string[] names = LexB.CountryNames(cc, _lang);
                lines.Add([new Piece(names[_r.Weighted(new[] { 0.6, 0.2, 0.1, 0.06, 0.04 }[..Math.Min(5, names.Length)])], "country")]);
                _events.Add("country");
            }

            // Join the lines.
            string layout;
            List<Piece> all = [];
            if (label)
            {
                layout = $"{cc.ToLowerInvariant()}.label.{_lang}";
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
            layout = $"{cc.ToLowerInvariant()}.line.{new[] { "comma", "space", "dash", "semicolon", "slash" }[s]}.{_lang}";
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                {
                    all.Add(new Piece(seps[s], null));
                }

                all.AddRange(lines[i]);
            }

            return Finish(all, layout);
        }

        private string NumberSeparator(string cc) => cc switch
        {
            "ES" or "PT" or "AD" => _r.Chance(0.85) ? ", " : " ",
            "IT" or "SM" or "VA" => _r.Chance(0.55) ? ", " : " ",
            _ => " ",
        };

        // The house number as the country writes it: Italian exponents ("12/A", "12A", "12 bis"), a letter after a British number ("12A").
        private string NumberText()
        {
            string n = _a.Number;
            if (_a.Suffix.Length == 0)
            {
                return n;
            }

            string suffix = _a.Suffix;
            if (_a.Country is "IT" or "SM" or "VA")
            {
                string sep = _r.Weighted([0.5, 0.35, 0.15]) switch { 0 => "/", 1 => string.Empty, _ => " " };
                return n + sep + (_mode == 2 ? suffix.ToLowerInvariant() : suffix);
            }

            return n + (_mode == 2 ? suffix.ToLowerInvariant() : suffix);
        }

        private List<Piece> StreetPieces(string type, string name, bool typeBefore)
        {
            if (type.Length == 0)
            {
                return [new Piece(Case(name, false), "street.name")];
            }

            string written = type;
            string period = string.Empty;
            string[] abbr = LexB.Abbreviations(type);
            if (abbr.Length > 0 && _r.Chance(_rt.Abbreviation))
            {
                written = _r.Pick(abbr);
                // "C/" and "R/" carry their slash inside the word; others end in a period already or take none.
                if (written.EndsWith('.'))
                {
                    written = written[..^1];
                    period = ".";
                }

                _events.Add("type-abbreviated");
            }

            List<Piece> typePieces = [new Piece(Case(written, false), "street.type")];
            if (period.Length > 0)
            {
                typePieces.Add(new Piece(".", null));
            }

            List<Piece> p = [];
            if (typeBefore)
            {
                p.AddRange(typePieces);
                p.Add(new Piece(" ", null));
                p.Add(new Piece(Case(name, false), "street.name"));
            }
            else
            {
                p.Add(new Piece(Case(name, false), "street.name"));
                p.Add(new Piece(" ", null));
                p.AddRange(typePieces);
            }

            // Spanish "C/ Mayor" is written without a space.
            if (typeBefore && written.EndsWith('/') && _r.Chance(0.5))
            {
                p[period.Length > 0 ? 2 : 1].Text = string.Empty;
            }

            return p;
        }

        // A secondary-address piece the register does not hold: a flat, floor or staircase, written the way the country writes it.
        private List<Piece> UnitPieces()
        {
            string cc = _a.Country;
            int n = 1 + _r.Int(_r.Chance(0.6) ? 12 : 60);
            string ns = n.ToString(CultureInfo.InvariantCulture);
            string letter = ((char)('A' + _r.Int(5))).ToString();
            switch (cc)
            {
                case "ES":
                {
                    string[] forms = ["º", "º", "ª"];
                    string text = _r.Weighted([0.5, 0.2, 0.15, 0.15]) switch
                    {
                        0 => ns + "º " + letter,
                        1 => ns + "º" + (_r.Chance(0.5) ? " izq." : " dcha."),
                        2 => _r.Chance(0.5) ? "Bajo" : "Entresuelo",
                        _ => ns + forms[_r.Int(2)] + letter.ToLowerInvariant(),
                    };
                    return [new Piece(_mode == 1 ? text.ToUpperInvariant() : text, "street.unit-number")];
                }

                case "PT":
                {
                    string text = _r.Weighted([0.4, 0.3, 0.3]) switch
                    {
                        0 => ns + ".º " + (_r.Chance(0.5) ? "Esq." : "Dto."),
                        1 => "R/C " + (_r.Chance(0.5) ? "Esq." : "Dto."),
                        _ => ns + "º " + letter,
                    };
                    return [new Piece(_mode == 1 ? text.ToUpperInvariant() : text, "street.unit-number")];
                }

                case "IT" or "SM" or "VA":
                {
                    string[] d = ["Scala", "Int.", "Interno", "Piano", "Sc."];
                    string des = d[_r.Int(d.Length)];
                    string val = des is "Scala" or "Sc." ? letter : ns;
                    return [new Piece(Case(des, false), "street.unit-designator"), new Piece(" ", null), new Piece(val, "street.unit-number")];
                }

                case "AD":
                {
                    string[] d = _lang == "ca" ? ["Pis", "Edifici", "Esc.", "Planta"] : ["Piso", "Edificio", "Esc."];
                    string des = d[_r.Int(d.Length)];
                    return [new Piece(Case(des, false), "street.unit-designator"), new Piece(" ", null), new Piece(des.StartsWith("Ed", StringComparison.Ordinal) ? letter : ns, "street.unit-number")];
                }

                default:
                {
                    string[] d = cc == "MT" ? ["Flat", "Apt", "Block", "Unit"] : ["Flat", "Flat", "Apartment", "Apt", "Unit", "Suite"];
                    string des = d[_r.Int(d.Length)];
                    string val = des == "Block" ? letter : ns;
                    return [new Piece(Case(des, false), "street.unit-designator"), new Piece(" ", null), new Piece(val, "street.unit-number")];
                }
            }
        }

        private List<Piece> PostBoxPieces(string cc)
        {
            string[] designators = cc switch
            {
                "ES" => ["Apartado de Correos", "Apdo.", "Apartado"],
                "PT" => ["Apartado", "Apartado"],
                "IT" or "SM" or "VA" => ["Casella Postale", "C.P.", "Casella postale"],
                "AD" => ["Apartat de Correus", "Apartat", "Apartado"],
                "MT" => ["P.O. Box", "PO Box"],
                _ => ["PO Box", "PO Box", "P.O. Box"],
            };
            string d = _r.Pick(designators);
            string number = (1 + _r.Int(_r.Chance(0.5) ? 999 : 9999)).ToString(CultureInfo.InvariantCulture);
            return [new Piece(Case(d, false), "street.box-designator"), new Piece(" ", null), new Piece(number, "street.box-number")];
        }

        // Lines after the delivery lines, by country: locality, county or province, postcode.
        private List<List<Piece>> Tail(string cc, string locality, bool label)
        {
            bool noPostal = _r.Chance(_rt.MissingPostalCode);
            bool noPlace = !noPostal && _r.Chance(_rt.MissingLocality);
            string postal = PostalText(cc);
            Piece? postalPiece = noPostal ? null : new Piece(postal, "postal-code");
            Piece? place = noPlace ? null : new Piece(Case(locality, true), "locality");
            Piece? admin = null;
            if (_r.Chance(_rt.Admin))
            {
                if (cc is "GB" && _a.Admin.Length > 0)
                {
                    admin = new Piece(Case(_a.Admin, false), "admin-area");
                }
                else if (cc == "ES" && _a.Admin.Length > 0 && !string.Equals(_a.Admin, locality, StringComparison.OrdinalIgnoreCase))
                {
                    admin = new Piece(Case(_a.Admin, true), "admin-area");
                }
            }

            if (cc == "IT" && _a.AdminAbbr.Length > 0 && _r.Chance(0.75))
            {
                admin = new Piece(_mode == 2 ? _a.AdminAbbr.ToLowerInvariant() : _a.AdminAbbr, "admin-area");
            }

            List<List<Piece>> lines = [];
            switch (cc)
            {
                case "GB" or "JE" or "GG" or "IM":
                    if (place is not null)
                    {
                        lines.Add([place]);
                    }

                    if (admin is not null)
                    {
                        lines.Add([admin]);
                    }

                    if (postalPiece is not null)
                    {
                        // The postcode closes the address, on its own line or after the post town.
                        if (lines.Count > 0 && admin is null && _r.Chance(0.35))
                        {
                            lines[^1].Add(new Piece(" ", null));
                            lines[^1].Add(postalPiece);
                        }
                        else
                        {
                            lines.Add([postalPiece]);
                        }
                    }

                    break;
                case "IE":
                    if (place is not null)
                    {
                        lines.Add([place]);
                    }

                    if (postalPiece is not null)
                    {
                        lines.Add([postalPiece]);
                    }

                    break;
                case "MT":
                    lines.Add(Join(" ", place, postalPiece));
                    break;
                case "ES":
                    {
                        List<Piece> last = Join(" ", postalPiece, place);
                        if (admin is not null)
                        {
                            if (label && _r.Chance(0.5))
                            {
                                lines.Add(last);
                                lines.Add([admin]);
                                break;
                            }

                            bool paren = _r.Chance(0.5);
                            last.Add(new Piece(paren ? " (" : ", ", null));
                            last.Add(admin);
                            if (paren)
                            {
                                last.Add(new Piece(")", null));
                            }
                        }

                        lines.Add(last);
                        break;
                    }

                case "IT":
                    {
                        List<Piece> last = Join(" ", postalPiece, place);
                        if (admin is not null)
                        {
                            bool paren = _r.Chance(0.5);
                            last.Add(new Piece(paren ? " (" : " ", null));
                            last.Add(admin);
                            if (paren)
                            {
                                last.Add(new Piece(")", null));
                            }
                        }

                        lines.Add(last);
                        break;
                    }

                default:
                    lines.Add(Join(" ", postalPiece, place));
                    break;
            }

            _ = label;
            return lines.Where(l => l.Count > 0).ToList();
        }

        private static List<Piece> Join(string sep, Piece? first, Piece? second)
        {
            List<Piece> p = [];
            if (first is not null)
            {
                p.Add(first);
            }

            if (first is not null && second is not null)
            {
                p.Add(new Piece(sep, null));
            }

            if (second is not null)
            {
                p.Add(second);
            }

            return p;
        }

        private string PostalText(string cc)
        {
            string p = _a.PostalCode;
            if (cc is "GB" or "JE" or "GG" or "IM")
            {
                if (p.Contains(' ', StringComparison.Ordinal) && _r.Chance(0.03))
                {
                    p = p.Replace(" ", string.Empty, StringComparison.Ordinal);
                }
            }
            else if (cc == "IE" && p.Contains(' ', StringComparison.Ordinal) && _r.Chance(0.12))
            {
                p = p.Replace(" ", string.Empty, StringComparison.Ordinal);
            }
            else if (cc == "MT" && p.Contains(' ', StringComparison.Ordinal) && _r.Chance(0.1))
            {
                p = p.Replace(" ", string.Empty, StringComparison.Ordinal);
            }
            else if (cc == "AD" && _r.Chance(0.1))
            {
                p = "AD-" + p[2..];
            }

            return _mode == 2 ? p.ToLowerInvariant() : p;
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
                if (p.Label is not null && p.Text.Length > 0)
                {
                    entities.Add((start, sb.Length, p.Label));
                }
            }

            string text = sb.ToString();
            int firstStreet = entities.FindIndex(e => e.Label.StartsWith("street.", StringComparison.Ordinal));
            List<(int Start, int End, string Label)> fields = [];
            if (firstStreet >= 0)
            {
                int lastStreet = entities.FindLastIndex(e => e.Label.StartsWith("street.", StringComparison.Ordinal));
                fields.Add((entities[firstStreet].Start, entities[lastStreet].End, "street-address"));
            }

            return new Example
            {
                Id = _a.Id,
                Country = _a.Country,
                Language = _lang,
                Layout = layout,
                Text = text,
                Tokens = Tokenizer.Tokenize(text, entities),
                Entities = entities,
                Fields = fields,
                Events = _events,
            };
        }

        // Casing of a piece for the example's mode; "postal" writes only the locality in capitals, as the postal services ask.
        private string Case(string t, bool isPlace) => _mode switch
        {
            1 => t.ToUpperInvariant(),
            2 => t.ToLowerInvariant(),
            3 => isPlace && _a.Country is "GB" or "JE" or "GG" or "IM" or "ES" or "PT" or "IT" ? t.ToUpperInvariant() : t,
            _ => t,
        };

        private List<Piece> NameLine(bool organization, string group, string city)
        {
            string family = _o.PickName("family", group, _r);
            string text;
            if (organization)
            {
                string pattern = _o.PickName("org", group, _r);
                text = pattern.Replace("{fam}", family, StringComparison.Ordinal).Replace("{word}", _o.PickName("word", group, _r), StringComparison.Ordinal)
                    .Replace("{city}", city, StringComparison.Ordinal);
            }
            else
            {
                string given = _o.PickName("given", group, _r);
                bool woman = _r.Chance(0.5);
                text = group switch
                {
                    "es" or "ca" => _r.Weighted([0.55, 0.2, 0.15, 0.1]) switch
                    {
                        0 => given + " " + family + " " + _o.PickName("family", group, _r),
                        1 => (woman ? "Sra. " : "Sr. ") + given + " " + family,
                        2 => (woman ? "Dña. " : "D. ") + given + " " + family,
                        _ => given + " " + family,
                    },
                    "it" => _r.Weighted([0.55, 0.2, 0.15, 0.1]) switch
                    {
                        0 => given + " " + family,
                        1 => (woman ? "Sig.ra " : "Sig. ") + family,
                        2 => (woman ? "Gent.ma Sig.ra " : "Egr. Sig. ") + given + " " + family,
                        _ => family + " " + given,
                    },
                    "pt" => _r.Weighted([0.55, 0.25, 0.2]) switch
                    {
                        0 => given + " " + family,
                        1 => (woman ? "Exma. Sra. " : "Exmo. Sr. ") + given + " " + family,
                        _ => (woman ? "Sra. D. " : "Sr. ") + given + " " + family,
                    },
                    _ => _r.Weighted([0.5, 0.25, 0.15, 0.1]) switch
                    {
                        0 => given + " " + family,
                        1 => (woman ? "Mrs " : "Mr ") + given[0] + " " + family,
                        2 => (woman ? "Ms " : "Mr ") + given + " " + family,
                        _ => given[0] + ". " + family,
                    },
                };
            }

            return [new Piece(Case(text, false), organization ? "organization" : "recipient")];
        }

        // A one-letter slip inside a word of a name piece: swapped neighbours, a dropped or a doubled letter.
        private void Typo(List<Piece> pieces)
        {
            List<Piece> candidates = pieces.Where(p => p.Label is "street.name" or "locality" or "street.type" && p.Text.Length >= 5).ToList();
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
