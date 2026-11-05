using System.Globalization;
using System.Text;
using OneContact.Data.AddressUsCa;

namespace OneContact.Data.AddressTier2A;

/// <summary>The rates the generator draws with. They are written into the manifest, so a run states what it did.</summary>
internal sealed class Rates
{
    /// <summary>Casing of the whole example: as the register writes it, UPPER, lower, and the postal service's own form (France, the Netherlands).</summary>
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

    internal double SyntheticUnit { get; init; } = 0.07;

    internal double PostBox { get; init; } = 0.025;

    internal double PostBoxWithStreet { get; init; } = 0.25;

    internal double Cedex { get; init; } = 0.012;

    internal double CedexOfOrganization { get; init; } = 0.45;

    internal double CountryCodePrefix { get; init; } = 0.07;

    internal double Bilingual { get; init; } = 0.03;

    internal double AlternateLanguage { get; init; } = 0.45;

    internal double Abbreviation { get; init; } = 0.15;

    internal double Typo { get; init; } = 0.05;

    internal double Glue { get; init; } = 0.012;

    internal double DoubleSpace { get; init; } = 0.01;

    internal double Canton { get; init; } = 0.04;

    internal double Place { get; init; } = 0.5;
}

/// <summary>
/// Renders one register address as a free-text example through the postal layouts of its country: the delivery line (number before the
/// street in France and Monaco, after it elsewhere), the postcode and locality line, and the optional lines around them. Only labelled
/// pieces carry letters and digits, so every one of them lies inside a span.
/// </summary>
internal sealed class Renderer
{
    /// <summary>Changes whenever a template, rate or draw order changes, so a manifest says which renderer made its files.</summary>
    internal const string Version = "tier2a-2026-11-05.1";

    private readonly Rates _rates;
    private readonly ulong _seed;
    private readonly Dictionary<string, List<string>> _pools = new(StringComparer.Ordinal);

    internal Renderer(string templatesDir, Rates rates, ulong seed)
    {
        _rates = rates;
        _seed = seed;
        string path = Path.Combine(templatesDir, "tier2a-names.tsv");
        Sha256 = Sha256Of(path);
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

    internal Rates Rates => _rates;

    internal static string Sha256Of(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    internal Example Render(Addr a) => new Ctx(this, a, Prng.For(_seed, a.Id)).Run();

    private string PickName(string kind, string lang, Prng r)
    {
        string l = lang is "lb" or "it" ? (r.Chance(0.5) ? "fr" : "de") : lang;
        return r.Pick(_pools[kind + "/" + l]);
    }

    private sealed class Ctx
    {
        private readonly Renderer _o;
        private readonly Addr _a;
        private readonly Prng _r;
        private readonly Rates _rt;
        private readonly List<string> _events = [];
        private int _mode;
        private string _lang = string.Empty;

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
            string street = _a.Street;
            string place = _a.Locality;
            _lang = _a.Language;
            bool bilingual = false;
            if (_a.StreetAlt.Length > 0)
            {
                if (_a.Region == "BRU" && _r.Chance(_rt.Bilingual))
                {
                    bilingual = true;
                    _events.Add("bilingual-street");
                }
                else if (_r.Chance(_rt.AlternateLanguage))
                {
                    _lang = _a.AltLanguage;
                    street = _a.StreetAlt;
                    place = _a.LocalityAlt.Length > 0 ? _a.LocalityAlt : _a.Locality;
                    _events.Add("alternate-language");
                }
            }

            // Swiss writers use "ss" for "ß"; German and Austrian ones sometimes do too.
            if (cc is "CH" or "LI" || (cc is "DE" or "AT" && _r.Chance(0.06)))
            {
                street = street.Replace("ß", "ss", StringComparison.Ordinal);
            }

            bool frenchStreet = Lexicon.SplitTypeFirst(street, "fr") is not null && _lang is "fr" or "lb";
            bool numberFirst = cc is "FR" or "MC" || (cc == "LU" && frenchStreet);
            bool hasOrg = _r.Chance(label ? _rt.OrganizationLabel : _rt.OrganizationLine);
            bool hasRecipient = _r.Chance(label ? _rt.RecipientLabel : _rt.RecipientLine);
            bool box = _r.Chance(_rt.PostBox);

            // The delivery line.
            List<Piece> delivery = [];
            if (bilingual)
            {
                List<Piece> both = [new Piece(Case(street), "street.name"), new Piece(_r.Chance(0.5) ? " - " : " / ", null), new Piece(Case(_a.StreetAlt), "street.name")];
                delivery = NumberAndStreet(both, numberFirst, cc);
            }
            else
            {
                delivery = NumberAndStreet(StreetPieces(street, cc), numberFirst, cc);
            }

            string frenchUnitLang = _lang is "fr" or "lb" ? "fr" : _lang;
            List<Piece> unitLine = [];
            bool unitAbove = false;
            if (_a.Box.Length > 0)
            {
                List<Piece> boxPieces = BelgianBox(_a.Box);
                if (boxPieces.Count > 0)
                {
                    delivery.AddRange(boxPieces);
                }
            }
            else if (cc is "FR" or "DE" or "AT" or "LU" && _r.Chance(_rt.SyntheticUnit))
            {
                unitLine = SyntheticUnit(cc == "LU" ? frenchUnitLang : cc);
                unitAbove = label && _r.Chance(0.55);
                if (!unitAbove)
                {
                    delivery.Add(new Piece(_r.Chance(0.6) ? ", " : " ", null));
                    delivery.AddRange(unitLine);
                    unitLine = [];
                }

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
                    unitLine = [];
                }
            }

            // The postcode and locality line.
            bool hasCedex = false;
            List<Piece> last = LastLine(cc, place, hasOrg, out hasCedex);
            List<Piece> dependent = [];
            if (_a.Place.Length > 0 && _r.Chance(_rt.Place))
            {
                dependent = [new Piece(Case(_a.Place), "dependent-locality")];
                _events.Add("dependent-locality");
            }

            // Lines of the example in postal order.
            List<List<Piece>> lines = [];
            if (hasRecipient)
            {
                lines.Add(NameLine(false, _lang, place));
                _events.Add("recipient");
            }

            if (hasOrg)
            {
                lines.Add(NameLine(true, _lang, place));
                _events.Add("organization");
            }

            if (unitAbove && unitLine.Count > 0)
            {
                lines.Add(unitLine);
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
            bool abroad = cc is "CH" or "LI" or "LU" or "MC" or "BE" or "AT";
            if (_r.Chance(abroad ? _rt.CountryAbroad : label ? _rt.CountryLabel : _rt.CountryLine))
            {
                string[] names = Lexicon.CountryNames(cc, _lang);
                lines.Add([new Piece(names[_r.Weighted(new[] { 0.7, 0.2, 0.1, 0.05, 0.03 }[..names.Length])], "country")]);
                _events.Add("country");
            }

            // Join the lines.
            string layout;
            if (label)
            {
                layout = $"{cc.ToLowerInvariant()}.label.{_lang}";
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
            layout = $"{cc.ToLowerInvariant()}.line.{new[] { "comma", "space", "dash", "semicolon", "slash" }[s]}.{_lang}";
            List<Piece> flat = [];
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                {
                    // Without a comma a postcode line still needs a break from the street line.
                    flat.Add(new Piece(seps[s], null));
                }

                flat.AddRange(lines[i]);
            }

            return Finish(flat, layout);
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

        // Casing of a piece's text for the example's mode.
        private string Case(string t) => _mode switch
        {
            1 => t.ToUpperInvariant(),
            2 => t.ToLowerInvariant(),
            3 => _a.Country is "FR" or "MC" ? Lexicon.PostalAscii(t) : t,
            _ => t,
        };

        private List<Piece> StreetPieces(string street, string cc)
        {
            string lang = _lang;
            (string Type, string Name)? split = Lexicon.SplitTypeFirst(street, lang);
            if (split is null && lang == "lb")
            {
                split = Lexicon.SplitTypeFirst(street, "fr");
            }

            if (split is { } sp)
            {
                string type = sp.Type;
                string period = string.Empty;
                string[] abbr = Lexicon.Abbreviations(type);
                if (abbr.Length > 0 && _r.Chance(_rt.Abbreviation))
                {
                    type = _r.Pick(abbr);
                    period = _r.Chance(0.4) ? "." : string.Empty;
                    _events.Add("type-abbreviated");
                }

                List<Piece> p = [new Piece(Case(type), "street.type")];
                if (period.Length > 0)
                {
                    p.Add(new Piece(period, null));
                }

                p.Add(new Piece(" ", null));
                p.Add(new Piece(Case(sp.Name), "street.name"));
                return p;
            }

            string name = street;
            if (lang == "de" && _r.Chance(_rt.Abbreviation))
            {
                string shortened = ShortenGerman(name);
                if (shortened != name)
                {
                    List<Piece> p = [new Piece(Case(shortened), "street.name"), new Piece(".", null)];
                    _events.Add("type-abbreviated");
                    return p;
                }
            }

            return [new Piece(Case(name), "street.name")];
        }

        // "Hauptstraße" -> "Hauptstr", "Berliner Straße" -> "Berliner Str": the period is written outside the span.
        private static string ShortenGerman(string name)
        {
            foreach (string suffix in new[] { "straße", "strasse", "Straße", "Strasse" })
            {
                if (name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length)
                {
                    return name[..^suffix.Length] + (suffix[0] == 'S' ? "Str" : "str");
                }
            }

            return name;
        }

        private List<Piece> NumberAndStreet(List<Piece> street, bool numberFirst, string cc)
        {
            Piece number = new(HouseNumberText(), "street.house-number");
            List<Piece> p = [];
            if (numberFirst)
            {
                p.Add(number);
                p.Add(new Piece(_r.Chance(cc == "LU" ? 0.55 : 0.07) ? ", " : " ", null));
                p.AddRange(street);
                if (_r.Chance(_rt.Glue))
                {
                    // "12rue de la Paix": the space is lost; spans stay on token edges because tokens are cut at span edges.
                    p[1].Text = string.Empty;
                    _events.Add("glue");
                }
            }
            else
            {
                p.AddRange(street);
                p.Add(new Piece(_r.Chance(cc is "DE" or "AT" ? 0.02 : 0.03) ? ", " : " ", null));
                p.Add(number);
            }

            return p;
        }

        // "12\u0001bis" joins a number and its repetition index; other numbers come as the register writes them.
        private string HouseNumberText()
        {
            string n = _a.HouseNumber;
            int i = n.IndexOf('\u0001', StringComparison.Ordinal);
            if (i < 0)
            {
                return _mode == 2 ? n.ToLowerInvariant() : n;
            }

            string number = n[..i];
            string rep = n[(i + 1)..];
            bool word = rep.Length > 1 && char.IsLetter(rep[0]);
            bool numeric = char.IsDigit(rep[0]);
            string sep = word ? (_r.Chance(0.88) ? " " : string.Empty)
                : numeric ? (_r.Chance(0.8) ? "-" : " ")
                : _r.Weighted([0.72, 0.2, 0.08]) switch { 0 => string.Empty, 1 => " ", _ => "-" };
            string r = word ? rep.ToLowerInvariant() : (_r.Chance(0.2) ? rep.ToUpperInvariant() : rep.ToLowerInvariant());
            string text = number + sep + r;
            return _mode == 1 ? text.ToUpperInvariant() : text;
        }

        private List<Piece> BelgianBox(string box)
        {
            string[] designators = _lang switch
            {
                "nl" => ["bus", "bus", "bus", "b", "bte"],
                "de" => ["Box", "Bte", "Bus"],
                _ => ["bte", "boîte", "bte", "b", "bt"],
            };
            if (_r.Chance(0.12))
            {
                _events.Add("unit:slash");
                return [new Piece("/", null), new Piece(box, "street.unit-number")];
            }

            string d = _r.Pick(designators);
            _events.Add("unit:box");
            List<Piece> p = [new Piece(" ", null), new Piece(Case(d), "street.unit-designator")];
            if (_r.Chance(0.15))
            {
                p.Add(new Piece(".", null));
            }

            p.Add(new Piece(" ", null));
            p.Add(new Piece(box, "street.unit-number"));
            return p;
        }

        private List<Piece> SyntheticUnit(string kind)
        {
            // Designator words of the postal guides' secondary-address lines: French "Bâtiment/Appartement/Étage/Entrée/Porte/Escalier",
            // German "Wohnung/Zimmer/Haus", Austrian "Top/Stiege/Tür".
            string[] designators = kind switch
            {
                "fr" or "FR" => ["Bâtiment", "Bât", "Appartement", "Appt", "Apt", "Étage", "Entrée", "Porte", "Escalier", "Esc"],
                "AT" => ["Top", "Top", "Stiege", "Tür", "Haus"],
                _ => ["Wohnung", "Whg", "App", "Zimmer", "Haus", "Wohnung"],
            };
            string d = _r.Pick(designators);
            string number = _r.Chance(0.3) ? ((char)('A' + _r.Int(8))).ToString() : (1 + _r.Int(60)).ToString(CultureInfo.InvariantCulture);
            List<Piece> p = [new Piece(Case(d), "street.unit-designator")];
            if (d is "Bât" or "Appt" or "Esc" or "Whg" or "App" && _r.Chance(0.5))
            {
                p.Add(new Piece(".", null));
            }

            p.Add(new Piece(" ", null));
            p.Add(new Piece(number, "street.unit-number"));
            return p;
        }

        private List<Piece> PostBoxPieces(string cc)
        {
            string[] designators = (cc, _lang) switch
            {
                ("FR" or "MC", _) => ["BP", "BP", "B.P.", "Boîte postale", "CS", "TSA"],
                ("LU", _) => ["B.P.", "BP", "Boîte postale"],
                ("BE", "nl") => ["Postbus", "PB"],
                ("BE", _) => ["Boîte postale", "BP", "Postbus"],
                ("NL", _) => ["Postbus", "Postbus", "Antwoordnummer"],
                ("CH", "fr") => ["Case postale", "CP"],
                ("CH", "it") => ["Casella postale", "CP"],
                _ => ["Postfach", "Postfach", "Pf."],
            };
            string d = _r.Pick(designators);
            string number = (1 + _r.Int(_r.Chance(0.5) ? 999 : 99999)).ToString(CultureInfo.InvariantCulture);
            return [new Piece(Case(d), "street.box-designator"), new Piece(" ", null), new Piece(number, "street.box-number")];
        }

        private List<Piece> LastLine(string cc, string place, bool hasOrg, out bool cedex)
        {
            cedex = false;
            bool noPostal = _r.Chance(_rt.MissingPostalCode);
            bool noPlace = !noPostal && _r.Chance(_rt.MissingLocality);
            List<Piece> p = [];
            if (!noPostal)
            {
                string postal = _a.PostalCode;
                bool prefix = false;
                if (cc == "NL")
                {
                    if (postal.Length == 6 && _r.Chance(0.78))
                    {
                        postal = postal[..4] + " " + postal[4..];
                    }

                    postal = _mode == 2 ? postal.ToLowerInvariant() : postal;
                }
                else if (cc == "FR" && _r.Chance(0.015))
                {
                    postal = postal[..2] + " " + postal[2..];
                }

                // The structural layer's postcode pattern is the digits alone, so "L-" in front of Luxembourg's, like "F-" or "CH-", is a country code.
                double prefixRate = cc switch { "CH" => 0.12, "LI" => 0.35, "LU" => 0.55, "NL" => 0.01, _ => _rt.CountryCodePrefix };
                prefix = _r.Chance(prefixRate);

                if (prefix)
                {
                    p.Add(new Piece(Lexicon.PostalPrefix(cc), "country"));
                    p.Add(new Piece("-", null));
                    _events.Add("code-prefix");
                }

                p.Add(new Piece(postal, "postal-code"));
            }

            if (!noPlace)
            {
                string locality = place;
                if (_mode == 3 && cc is "FR" or "MC")
                {
                    locality = _a.LocalityPostal.Length > 0 ? _a.LocalityPostal : Lexicon.PostalAscii(place);
                }
                else if (_mode == 3 && cc == "NL")
                {
                    locality = place.ToUpperInvariant();
                }
                else
                {
                    locality = Case(place);
                }

                if (p.Count > 0)
                {
                    p.Add(new Piece(_mode == 3 && cc == "NL" && _r.Chance(0.4) ? "  " : " ", null));
                }

                p.Add(new Piece(locality, "locality"));
                if (cc == "CH" && _a.Canton.Length > 0 && _r.Chance(_rt.Canton))
                {
                    p.Add(new Piece(" ", null));
                    p.Add(new Piece(_a.Canton, "admin-area"));
                }

                if (cc == "FR" && _r.Chance(hasOrg ? _rt.CedexOfOrganization : _rt.Cedex))
                {
                    cedex = true;
                    string word = _mode == 1 || _mode == 3 || _r.Chance(0.7) ? "CEDEX" : "Cedex";
                    string code = _r.Chance(0.55) ? " " + (1 + _r.Int(20)).ToString("D2", CultureInfo.InvariantCulture) : string.Empty;
                    p.Add(new Piece(" ", null));
                    p.Add(new Piece(word + code, "sorting-code"));
                    _events.Add("cedex");
                }
            }

            if (p.Count == 0)
            {
                p.Add(new Piece(Case(place), "locality"));
            }

            return p;
        }

        private List<Piece> NameLine(bool organization, string lang, string city)
        {
            string l = lang is "lb" or "it" ? (_r.Chance(0.5) ? "fr" : "de") : lang;
            string family = _o.PickName("family", l, _r);
            string text;
            if (organization)
            {
                string pattern = _o.PickName("org", l, _r);
                text = pattern.Replace("{fam}", family, StringComparison.Ordinal).Replace("{word}", _o.PickName("word", l, _r), StringComparison.Ordinal)
                    .Replace("{city}", city, StringComparison.Ordinal);
            }
            else
            {
                string given = _o.PickName("given", l, _r);
                bool woman = _r.Chance(0.5);
                text = l switch
                {
                    "fr" => _r.Weighted([0.5, 0.2, 0.3]) switch
                    {
                        0 => given + " " + family,
                        1 => (woman ? "Madame " : "Monsieur ") + given + " " + family,
                        _ => (woman ? "Mme " : "M. ") + given + " " + family,
                    },
                    "nl" => _r.Weighted([0.5, 0.2, 0.15, 0.15]) switch
                    {
                        0 => given + " " + family,
                        1 => (woman ? "Mevrouw " : "De heer ") + given[0] + ". " + family,
                        2 => "Fam. " + family,
                        _ => given[0] + ". " + family,
                    },
                    _ => _r.Weighted([0.5, 0.2, 0.15, 0.15]) switch
                    {
                        0 => given + " " + family,
                        1 => (woman ? "Frau " : "Herrn ") + given + " " + family,
                        2 => "Familie " + family,
                        _ => given[0] + ". " + family,
                    },
                };
            }

            return [new Piece(Case(text), organization ? "organization" : "recipient")];
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
