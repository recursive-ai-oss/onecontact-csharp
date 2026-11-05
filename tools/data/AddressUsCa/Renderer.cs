using System.Globalization;
using System.Text;

namespace OneContact.Data.AddressUsCa;

/// <summary>The rates the generator draws with. They are written into the manifest, so a run states what it did.</summary>
internal sealed class Rates
{
    // Casing of the whole example (Publication 28 section 222 asks for capitals; running text is mostly title case).
    internal double[] CaseWeights { get; init; } = [0.30, 0.45, 0.08, 0.17];

    // Which kind of delivery line: street, PO box, rural route, highway contract route, military, general delivery.
    internal double[] UsDelivery { get; init; } = [93.2, 3.4, 1.1, 0.5, 1.0, 0.8];

    internal double[] CaDelivery { get; init; } = [95.0, 3.0, 1.2, 0.0, 0.0, 0.8];

    internal double SyntheticUnit { get; init; } = 0.09;

    internal double RecipientLabel { get; init; } = 0.45;

    internal double RecipientLine { get; init; } = 0.14;

    internal double OrganizationLabel { get; init; } = 0.18;

    internal double OrganizationLine { get; init; } = 0.06;

    internal double CountryLabel { get; init; } = 0.06;

    internal double CountryLine { get; init; } = 0.08;

    internal double MissingPostalCode { get; init; } = 0.05;

    internal double MissingRegion { get; init; } = 0.04;

    internal double MissingCity { get; init; } = 0.005;

    internal double ZipPlusFour { get; init; } = 0.12;

    internal double Period { get; init; } = 0.05;

    // Noise: a typing or OCR edit in some labelled piece; a missing or doubled space between two pieces.
    internal double TypoExample { get; init; } = 0.06;

    internal double GlueExample { get; init; } = 0.012;

    internal double DoubleSpaceExample { get; init; } = 0.01;

    internal double AbbreviatedSaint { get; init; } = 0.05;

    // Puerto Rico: how often the urbanization line is written when the record has one (Publication 28 section 292 asks for it; informal writing drops it).
    internal double Urbanization { get; init; } = 0.75;

    public override string ToString() =>
        $"case={string.Join('/', CaseWeights)} usDelivery={string.Join('/', UsDelivery)} caDelivery={string.Join('/', CaDelivery)} syntheticUnit={SyntheticUnit} " +
        $"recipient={RecipientLabel}/{RecipientLine} organization={OrganizationLabel}/{OrganizationLine} country={CountryLabel}/{CountryLine} missingPostal={MissingPostalCode} " +
        $"missingRegion={MissingRegion} missingCity={MissingCity} zipPlusFour={ZipPlusFour} period={Period} typo={TypoExample} glue={GlueExample} doubleSpace={DoubleSpaceExample} abbreviatedSaint={AbbreviatedSaint} urbanization={Urbanization}";
}

/// <summary>Renders register records into labelled free-text addresses through the committed layouts.</summary>
internal sealed class Renderer
{
    private static readonly HashSet<string> KeepUpper = new(StringComparer.Ordinal)
    {
        "PO", "RR", "HC", "GD", "CP", "NE", "NW", "SE", "SW", "NO", "SO", "PSC", "CMR", "APO", "FPO", "DPO", "DC", "USA", "US", "PMB", "STN", "RPO", "SUCC", "COP", "LCD", "AE", "AP", "AA",
    };

    private static readonly HashSet<string> EnglishSmall = new(StringComparer.Ordinal) { "of", "and", "the" };

    private static readonly HashSet<string> FrenchSmall = new(StringComparer.Ordinal) { "de", "du", "des", "la", "le", "les", "et", "sur", "sous", "aux", "au", "en" };

    private readonly Rules _rules;
    private readonly Templates _templates;
    private readonly Rates _rates;
    private readonly ulong _seed;

    internal Renderer(Rules rules, Templates templates, Rates rates, ulong seed)
    {
        _rules = rules;
        _templates = templates;
        _rates = rates;
        _seed = seed;
    }

    internal Example Render(Rec rec)
    {
        Prng r = Prng.For(_seed, rec.Id);
        Ctx c = new(this, rec, r);
        return c.Run();
    }

    private sealed class Ctx
    {
        private readonly Renderer _o;
        private readonly Rec _rec;
        private readonly Prng _r;
        private readonly List<string> _events = [];
        private readonly string _lang;
        private int _mode;
        private bool _hasRecipient;
        private bool _hasOrganization;
        private bool _hasCountry;
        private bool _hasUnit;
        private int _unitPlacement; // 0 inline after the street, 1 own line, 2 before the house number (Canada)
        private bool _unitComma;
        private string _unitNumber = string.Empty;
        private string _unitStd = string.Empty;
        private int _delivery;
        private string _state = string.Empty;
        private string _city = string.Empty;
        private string _urbanization = string.Empty;
        private string _zip = string.Empty;
        private bool _stateLong;

        internal Ctx(Renderer o, Rec rec, Prng r)
        {
            _o = o;
            _rec = rec;
            _r = r;
            _lang = rec.Language;
        }

        private Rates Rt => _o._rates;

        internal Example Run()
        {
            bool us = _rec.Country == "US";
            // Puerto Rico has its own layouts: the urbanization line comes before the street line (Publication 28 section 292).
            Layout layout = _o._templates.Pick(us && _rec.Region == "PR" ? "US-PR" : _rec.Country, _lang, _r);
            _mode = _r.Weighted(Rt.CaseWeights);
            bool label = layout.IsLabel;
            _hasRecipient = _r.Chance(label ? Rt.RecipientLabel : Rt.RecipientLine);
            _hasOrganization = _r.Chance(label ? Rt.OrganizationLabel : Rt.OrganizationLine);
            _hasCountry = _r.Chance(label ? Rt.CountryLabel : Rt.CountryLine);
            _delivery = _r.Weighted(us ? Rt.UsDelivery : Rt.CaDelivery);

            _state = _rec.Region;
            _city = _rec.City;
            _zip = _rec.PostalCode;
            if (_delivery == 4)
            {
                (_city, _state, _zip) = MilitaryLine();
            }

            if (_zip.Length > 0 && _r.Chance(Rt.MissingPostalCode))
            {
                _zip = string.Empty;
                _events.Add("missing-postal-code");
            }

            if (_r.Chance(Rt.MissingRegion))
            {
                _state = string.Empty;
                _events.Add("missing-region");
            }

            if (_r.Chance(Rt.MissingCity))
            {
                _city = string.Empty;
                _events.Add("missing-city");
            }

            if (_rec.Urbanization.Length > 0 && _delivery == 0)
            {
                if (_r.Chance(Rt.Urbanization))
                {
                    _urbanization = _rec.Urbanization;
                }
                else
                {
                    _events.Add("missing-urbanization");
                }
            }

            _stateLong = _state.Length > 0 && _delivery != 4 && _r.Chance(us ? 0.28 : 0.30);
            DecideUnit();

            List<Piece> pieces = [];
            bool firstLine = true;
            foreach (List<LayoutItem> line in layout.Lines)
            {
                List<Piece> fragment = RenderLine(line);
                if (fragment.Count == 0)
                {
                    continue;
                }

                if (!firstLine)
                {
                    pieces.Add(new Piece("\n", null));
                }

                pieces.AddRange(fragment);
                firstLine = false;
            }

            Noise(pieces);
            return Assemble(pieces, layout);
        }

        private List<Piece> RenderLine(List<LayoutItem> line)
        {
            List<Piece> fragment = [];
            foreach (LayoutItem item in line)
            {
                List<Piece> slot = Slot(item.Slot);
                if (slot.Count == 0)
                {
                    continue;
                }

                if (fragment.Count > 0 && item.Before.Length > 0)
                {
                    fragment.Add(new Piece(item.Before, null));
                }
                else if (fragment.Count > 0 && item.Before.Length == 0 && item.Slot != "street")
                {
                    // adjacent slots with no separator in the template still need one token boundary.
                    fragment.Add(new Piece(" ", null));
                }
                else if (fragment.Count == 0 && item.Before.Trim().Length > 0 && item.Before.Trim()[0] == '(')
                {
                    fragment.Add(new Piece(item.Before.TrimStart(), null));
                }

                fragment.AddRange(slot);
                if (item.After.Length > 0)
                {
                    fragment.Add(new Piece(item.After, null));
                }
            }

            return fragment;
        }

        private List<Piece> Slot(string name) => name switch
        {
            "recipient" => _hasRecipient ? Recipient() : [],
            "organization" => _hasOrganization ? Organization() : [],
            "street" => Street(),
            "unit" => UnitLine(),
            "urbanization" => _urbanization.Length == 0 ? [] : UrbanizationLine(),
            "city" => _city.Length == 0 ? [] : [new Piece(Fmt(_city, false), "locality")],
            "state" => _state.Length == 0 ? [] : [new Piece(StateText(), "admin-area")],
            "zip" => _zip.Length == 0 ? [] : [new Piece(PostalText(), "postal-code")],
            "country" => _hasCountry ? Country() : [],
            _ => throw new InvalidDataException("unknown slot " + name),
        };

        // ---------------------------------------------------------------- recipient and organization

        private List<Piece> Recipient()
        {
            IReadOnlyList<string> given = _o._templates.Names("given");
            IReadOnlyList<string> sur = _o._templates.Names("surname");
            string g = _r.Pick(given);
            string s = _r.Pick(sur);
            StringBuilder sb = new();
            int form = _r.Weighted([55, 12, 10, 8, 6, 5, 4]);
            switch (form)
            {
                case 0: sb.Append(g).Append(' ').Append(s); break;
                case 1: sb.Append(g).Append(' ').Append((char)('A' + _r.Int(26))).Append(". ").Append(s); break;
                case 2: sb.Append(_r.Pick(_o._templates.Names("title"))).Append(' ').Append(g).Append(' ').Append(s); break;
                case 3: sb.Append(s).Append(", ").Append(g); break;
                case 4: sb.Append("Attn: ").Append(g).Append(' ').Append(s); break;
                case 5: sb.Append("c/o ").Append(g).Append(' ').Append(s); break;
                default: sb.Append(g).Append(' ').Append(s).Append(' ').Append(s).Append("-").Append(_r.Pick(sur)); break;
            }

            string text = sb.ToString();
            // The whole line is the recipient span, "Attn:" and "c/o" included: they only introduce the name.
            return [new Piece(FmtFree(text), "recipient")];
        }

        /// <summary>The urbanization line: <c>URB</c> (or its spellings) and the name, one span; a few names stand alone without the word (section 292).</summary>
        private List<Piece> UrbanizationLine()
        {
            string word = _r.Weighted([55, 10, 20, 15, 12]) switch { 0 => "URB", 1 => "Urb.", 2 => "Urbanización", 3 => "Urbanizacion", _ => string.Empty };
            string text = (word.Length == 0 ? string.Empty : Fmt(word, false) + " ") + Fmt(_urbanization, false);
            _events.Add(word.Length == 0 ? "urbanization-bare" : "urbanization");
            return [new Piece(text, "urbanization")];
        }

        private List<Piece> Organization()
        {
            IReadOnlyList<string> words = _o._templates.Names("orgword");
            IReadOnlyList<string> nouns = _o._templates.Names("orgnoun");
            IReadOnlyList<string> sur = _o._templates.Names("surname");
            bool fr = _lang == "fr";
            StringBuilder sb = new();
            switch (_r.Weighted([40, 25, 20, 15]))
            {
                case 0: sb.Append(_r.Pick(words)).Append(' ').Append(_r.Pick(nouns)); break;
                case 1: sb.Append(_r.Pick(sur)).Append(' ').Append(_r.Pick(nouns)); break;
                case 2: sb.Append(_r.Pick(sur)).Append(" & ").Append(_r.Pick(sur)); break;
                default: sb.Append(_r.Pick(words)).Append(' ').Append(_r.Pick(words)).Append(' ').Append(_r.Pick(nouns)); break;
            }

            if (_r.Chance(0.6))
            {
                sb.Append(' ').Append(_r.Pick(_o._templates.Names(fr ? "form_fr" : "form_en")));
            }

            return [new Piece(FmtFree(sb.ToString()), "organization")];
        }

        private List<Piece> Country()
        {
            string[] us = ["USA", "United States", "U.S.A.", "United States of America", "US"];
            string[] ca = ["Canada", "Canada", "CAN"];
            string text = _rec.Country == "US" ? _r.Pick(us) : _r.Pick(ca);
            return [new Piece(_mode == 2 ? text.ToLowerInvariant() : _mode == 0 && text.Length > 3 ? text.ToUpperInvariant() : text, "country")];
        }

        // ---------------------------------------------------------------- region and postal code

        private string StateText()
        {
            if (_rec.Country == "US")
            {
                return _stateLong ? Fmt(_o._rules.UsStateName(_state), false) : Fmt(_state, true);
            }

            if (_stateLong && Rules.CaProvinceNames.TryGetValue(_state, out (string English, string French) names))
            {
                return Fmt(_lang == "fr" ? names.French : names.English, false);
            }

            return Fmt(_state, true);
        }

        private string PostalText()
        {
            if (_rec.Country == "US")
            {
                if (_zip.Length == 5 && _delivery != 4 && (_rec.Plus4.Length > 0 ? _r.Chance(0.5) : _r.Chance(Rt.ZipPlusFour)))
                {
                    // The register's own extension when it has one; otherwise four random digits, so the format is seen without claiming a real +4.
                    _events.Add(_rec.Plus4.Length > 0 ? "zip-plus-four" : "zip-plus-four-random");
                    return _zip + "-" + (_rec.Plus4.Length > 0 ? _rec.Plus4 : _r.Int(10000).ToString("D4", CultureInfo.InvariantCulture));
                }

                return _zip;
            }

            string code = _zip.Length == 6 ? (_r.Chance(0.85) ? _zip[..3] + " " + _zip[3..] : _zip) : _zip;
            return _mode == 2 ? code.ToLowerInvariant() : code.ToUpperInvariant();
        }

        // ---------------------------------------------------------------- military

        private (string City, string State, string Zip) MilitaryLine()
        {
            _events.Add("military-address");
            (string, string, string)[] kinds =
            [
                ("APO", "AE", "09" + _r.Int(1000).ToString("D3", CultureInfo.InvariantCulture)),
                ("FPO", "AE", "09" + _r.Int(1000).ToString("D3", CultureInfo.InvariantCulture)),
                ("APO", "AP", "962" + _r.Int(100).ToString("D2", CultureInfo.InvariantCulture)),
                ("FPO", "AP", "966" + _r.Int(100).ToString("D2", CultureInfo.InvariantCulture)),
                ("APO", "AA", "340" + _r.Int(100).ToString("D2", CultureInfo.InvariantCulture)),
                ("DPO", "AE", "09" + _r.Int(1000).ToString("D3", CultureInfo.InvariantCulture)),
            ];
            return kinds[_r.Int(kinds.Length)];
        }

        // ---------------------------------------------------------------- unit

        private void DecideUnit()
        {
            string number = _rec.Unit;
            bool fromRegister = number.Length > 0;
            if (!fromRegister && _delivery == 0 && _r.Chance(Rt.SyntheticUnit))
            {
                number = _r.Weighted([55, 25, 20]) switch
                {
                    0 => (1 + _r.Int(_r.Chance(0.5) ? 30 : 500)).ToString(CultureInfo.InvariantCulture),
                    1 => (1 + _r.Int(40)).ToString(CultureInfo.InvariantCulture) + (char)('A' + _r.Int(6)),
                    _ => ((char)('A' + _r.Int(12))).ToString(),
                };
                _events.Add("synthetic-unit");
            }

            if (number.Length == 0 || _delivery != 0)
            {
                _hasUnit = false;
                return;
            }

            _hasUnit = true;
            _unitNumber = number;
            _unitStd = ChooseUnitDesignator(fromRegister);
            _unitComma = _r.Chance(0.4);
            if (_rec.Country == "CA")
            {
                _unitPlacement = _r.Weighted([40, 15, 45]);
                if (_unitPlacement == 2 && _lang == "fr" && _r.Chance(0.3))
                {
                    _unitPlacement = 0;
                }
            }
            else
            {
                _unitPlacement = _r.Weighted([85, 15]);
            }
        }

        private string ChooseUnitDesignator(bool fromRegister)
        {
            bool bigNumber = int.TryParse(_unitNumber, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 100;
            if (_rec.Country == "CA")
            {
                if (_lang == "fr")
                {
                    return _r.Weighted([60, 30, 10]) switch { 0 => "APP", 1 => "BUREAU", _ => "UNITÉ" };
                }

                return _r.Weighted(bigNumber ? [20, 55, 25] : [55, 15, 30]) switch { 0 => "APT", 1 => "SUITE", _ => "UNIT" };
            }

            if (fromRegister && _rec.UnitDesignator.Length > 0 && _o._rules.TryUsUnit(_rec.UnitDesignator, out string std))
            {
                return std;
            }

            return _r.Weighted(bigNumber ? [20, 50, 10, 12, 8] : [52, 10, 22, 4, 12]) switch
            {
                0 => "APT",
                1 => "STE",
                2 => "UNIT",
                3 => "RM",
                _ => "#",
            };
        }

        /// <summary>The unit as pieces: designator, optional separator, number.</summary>
        private List<Piece> UnitPieces()
        {
            List<Piece> p = [];
            string number = _unitNumber;
            if (_unitStd == "#")
            {
                p.Add(new Piece("#", "street.unit-designator"));
                if (_r.Chance(0.15))
                {
                    p.Add(new Piece(" ", null));
                }

                p.Add(new Piece(number.ToUpperInvariant(), "street.unit-number"));
                return p;
            }

            string word = SpellUnit(_unitStd, out bool periodOk);
            p.Add(new Piece(Fmt(word, false), "street.unit-designator"));
            if (periodOk && _r.Chance(Rt.Period))
            {
                p.Add(new Piece(".", null));
            }

            p.Add(new Piece(" ", null));
            p.Add(new Piece(_mode == 2 ? number.ToLowerInvariant() : number.ToUpperInvariant(), "street.unit-number"));
            return p;
        }

        private string SpellUnit(string std, out bool periodOk)
        {
            periodOk = false;
            if (_rec.Country == "US")
            {
                IReadOnlyList<string> same = _o._rules.UsUnitSpellings(std);
                string full = same.OrderByDescending(s => s.Length).First();
                switch (_r.Weighted([58, 34, 8]))
                {
                    case 0: periodOk = true; return std;
                    case 1: return full;
                    default: return _r.Pick(same);
                }
            }

            if (_lang == "fr")
            {
                switch (std)
                {
                    case "APP": periodOk = true; return _r.Chance(0.25) ? "APPARTEMENT" : "APP";
                    case "BUREAU": return "BUREAU";
                    default: return _r.Chance(0.3) ? "UNITE" : "UNITÉ";
                }
            }

            switch (std)
            {
                case "APT": periodOk = true; return _r.Chance(0.3) ? "APARTMENT" : "APT";
                case "SUITE": periodOk = true; return _r.Chance(0.35) ? "STE" : "SUITE";
                default: return "UNIT";
            }
        }

        private List<Piece> UnitLine()
        {
            if (!_hasUnit || _unitPlacement != 1)
            {
                return [];
            }

            return UnitPieces();
        }

        // ---------------------------------------------------------------- street

        private List<Piece> Street()
        {
            return _delivery switch
            {
                1 => PostOfficeBox(),
                2 => RuralRoute(highway: false),
                3 => RuralRoute(highway: true),
                4 => Military(),
                5 => GeneralDelivery(),
                _ => CivicStreet(),
            };
        }

        private List<Piece> CivicStreet()
        {
            List<Piece> p = [];
            bool unitFirst = _hasUnit && _unitPlacement == 2 && _rec.Country == "CA";
            if (unitFirst)
            {
                p.Add(new Piece(_unitNumber.ToUpperInvariant(), "street.unit-number"));
                p.Add(new Piece("-", null));
            }

            p.Add(new Piece(_rec.HouseNumber.ToUpperInvariant(), "street.house-number"));
            if (_rec.Fraction.Length > 0)
            {
                p.Add(new Piece(" ", null));
                p.Add(new Piece(_rec.Fraction, "street.fraction"));
            }

            bool frenchComma = _lang == "fr" && _r.Chance(0.2);
            if (frenchComma)
            {
                p.Add(new Piece(",", null));
            }

            if (_rec.PreDirectional.Length > 0)
            {
                p.Add(new Piece(" ", null));
                AddDirectional(p, _rec.PreDirectional, "street.pre-directional");
            }

            bool typeBefore = _rec.TypeBefore && _rec.StreetTypeStd.Length > 0;
            if (typeBefore)
            {
                p.Add(new Piece(" ", null));
                AddType(p);
            }

            p.Add(new Piece(" ", null));
            p.Add(new Piece(FmtName(_rec.StreetName), "street.name"));
            if (!typeBefore && _rec.StreetTypeStd.Length > 0)
            {
                p.Add(new Piece(" ", null));
                AddType(p);
            }

            if (_rec.PostDirectional.Length > 0)
            {
                p.Add(new Piece(" ", null));
                AddDirectional(p, _rec.PostDirectional, "street.post-directional");
            }

            if (_hasUnit && _unitPlacement == 0)
            {
                p.Add(new Piece(_unitComma ? ", " : " ", null));
                p.AddRange(UnitPieces());
            }

            return p;
        }

        private void AddType(List<Piece> p)
        {
            string std = _rec.StreetTypeStd;
            string text;
            bool period = false;
            if (_rec.Country == "US")
            {
                IReadOnlyList<string> spellings = _o._rules.UsSuffixSpellings(std);
                _o._rules.TryUsSuffix(std, out _, out string primary);
                switch (_r.Weighted([55, 35, 10]))
                {
                    case 0: text = std; period = _r.Chance(Rt.Period); break;
                    case 1: text = primary; break;
                    default: text = _r.Pick(spellings); break;
                }
            }
            else
            {
                IReadOnlyList<string> spellings = _o._rules.CaTypeSpellings(_lang, std);
                string longest = spellings.OrderByDescending(s => s.Length).ThenBy(s => s, StringComparer.Ordinal).First();
                switch (_r.Weighted([55, 40, 5]))
                {
                    case 0: text = std; period = _r.Chance(_lang == "fr" ? 0.18 : Rt.Period); break;
                    case 1: text = longest; break;
                    default: text = _r.Pick(spellings); break;
                }
            }

            p.Add(new Piece(Fmt(text, false), "street.type"));
            if (period && text.Length > 1)
            {
                p.Add(new Piece(".", null));
            }
        }

        private void AddDirectional(List<Piece> p, string written, string label)
        {
            string abbr;
            string full;
            if (_rec.Country == "US")
            {
                _o._rules.TryUsDirection(written, out abbr, out full);
                bool abbreviate = _r.Chance(0.72);
                p.Add(new Piece(Fmt(abbreviate ? abbr : full, abbreviate), label));
                if (abbreviate && _r.Chance(Rt.Period))
                {
                    p.Add(new Piece(".", null));
                }

                return;
            }

            _o._rules.TryCaDirection(written, out abbr, out string lang);
            IReadOnlyList<string> spellings = _o._rules.CaDirectionSpellings(lang, abbr);
            bool abbrev = _r.Chance(0.6) || spellings.Count == 1;
            string text = abbrev ? abbr : _r.Pick(spellings.Skip(1).ToList());
            p.Add(new Piece(Fmt(text, abbrev), label));
        }

        private List<Piece> PostOfficeBox()
        {
            List<Piece> p = [];
            string number = BoxNumber();
            if (_rec.Country == "CA" && _lang == "fr")
            {
                string d = _r.Weighted([55, 25, 20]) switch { 0 => "CP", 1 => "C.P.", _ => "Case postale" };
                p.Add(new Piece(Fmt(d, d == "CP"), "street.box-designator"));
            }
            else
            {
                string d = _r.Weighted([50, 25, 15, 10]) switch { 0 => "PO BOX", 1 => "P.O. Box", 2 => "Post Office Box", _ => "P O Box" };
                p.Add(new Piece(Fmt(d, false), "street.box-designator"));
            }

            p.Add(new Piece(" ", null));
            p.Add(new Piece(number, "street.box-number"));
            if (_rec.Country == "CA" && _r.Chance(0.45))
            {
                p.Add(new Piece(" ", null));
                string st = _lang == "fr" ? _r.Pick(new[] { "SUCC", "Succursale" }) : _r.Pick(new[] { "STN", "Station", "RPO" });
                p.Add(new Piece(Fmt(st, st.Length <= 4), "street.station-designator"));
                p.Add(new Piece(" ", null));
                p.Add(new Piece(_r.Chance(0.5) ? ((char)('A' + _r.Int(26))).ToString() : Fmt(_r.Pick(new[] { "Main", "Centre", "Downtown", "Terminal" }), false), "street.station-name"));
            }

            _events.Add("po-box");
            return p;
        }

        private List<Piece> RuralRoute(bool highway)
        {
            List<Piece> p = [];
            string d = highway
                ? _r.Weighted([60, 25, 15]) switch { 0 => "HC", 1 => "HCR", _ => "Highway Contract Route" }
                : _r.Weighted([55, 20, 15, 10]) switch { 0 => "RR", 1 => "R.R.", 2 => "Rural Route", _ => "RR" };
            p.Add(new Piece(Fmt(d, d.Length <= 3), "street.route-designator"));
            p.Add(new Piece(" ", null));
            p.Add(new Piece((1 + _r.Int(_r.Chance(0.8) ? 9 : 99)).ToString(CultureInfo.InvariantCulture), "street.route-number"));
            if (_rec.Country == "US" || _r.Chance(0.25))
            {
                p.Add(new Piece(" ", null));
                p.Add(new Piece(Fmt(_r.Chance(0.9) ? "BOX" : "Box", false), "street.box-designator"));
                p.Add(new Piece(" ", null));
                p.Add(new Piece(BoxNumber(), "street.box-number"));
            }

            _events.Add(highway ? "highway-contract-route" : "rural-route");
            return p;
        }

        private List<Piece> Military()
        {
            List<Piece> p = [];
            string d = _r.Pick(new[] { "PSC", "UNIT", "CMR", "PSC", "UNIT" });
            p.Add(new Piece(Fmt(d, d != "UNIT"), "street.military-designator"));
            p.Add(new Piece(" ", null));
            p.Add(new Piece((1 + _r.Int(9999)).ToString(CultureInfo.InvariantCulture), "street.military-number"));
            p.Add(new Piece(" ", null));
            p.Add(new Piece(Fmt("BOX", false), "street.box-designator"));
            p.Add(new Piece(" ", null));
            p.Add(new Piece(BoxNumber(), "street.box-number"));
            return p;
        }

        private List<Piece> GeneralDelivery()
        {
            _events.Add("general-delivery");
            string text = _rec.Country == "CA"
                ? (_lang == "fr" ? _r.Pick(new[] { "PR", "Poste restante" }) : _r.Pick(new[] { "GD", "General Delivery", "General Delivery" }))
                : "General Delivery";
            return [new Piece(Fmt(text, text.Length <= 2), "street.general-delivery")];
        }

        private string BoxNumber()
        {
            int digits = _r.Weighted([6, 20, 30, 28, 16]);
            int max = (int)Math.Pow(10, digits + 1);
            return (1 + _r.Int(max - 1)).ToString(CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- casing

        /// <summary>Applies the example's casing. <paramref name="code"/> marks letters that stay capitals in title case (state codes, directionals).</summary>
        private string Fmt(string text, bool code)
        {
            int mode = _mode == 3 ? (_r.Chance(0.5) ? 0 : 1) : _mode;
            return mode switch
            {
                0 => text.ToUpperInvariant(),
                2 => text.ToLowerInvariant(),
                _ => code ? text.ToUpperInvariant() : Title(text, _lang == "fr"),
            };
        }

        /// <summary>Free text such as names: capitals and lower case follow the example's mode but keep the case the lists were written in for title case.</summary>
        private string FmtFree(string text)
        {
            int mode = _mode == 3 ? (_r.Chance(0.5) ? 0 : 1) : _mode;
            return mode switch { 0 => text.ToUpperInvariant(), 2 => text.ToLowerInvariant(), _ => text };
        }

        private string FmtName(string name)
        {
            string n = name;
            if (_rec.Country == "CA" && _r.Chance(Rt.AbbreviatedSaint))
            {
                n = AbbreviateSaint(n);
            }

            return Fmt(n, false);
        }

        private static string AbbreviateSaint(string name)
        {
            string up = name.ToUpperInvariant();
            if (up.StartsWith("SAINTE-", StringComparison.Ordinal) || up.StartsWith("SAINTE ", StringComparison.Ordinal))
            {
                return "Ste" + name[6..];
            }

            if (up.StartsWith("SAINT-", StringComparison.Ordinal) || up.StartsWith("SAINT ", StringComparison.Ordinal))
            {
                return "St" + name[5..];
            }

            return name;
        }

        private static string Title(string text, bool french)
        {
            StringBuilder sb = new(text.Length);
            string[] words = text.Split(' ');
            for (int w = 0; w < words.Length; w++)
            {
                if (w > 0)
                {
                    sb.Append(' ');
                }

                string word = words[w];
                if (KeepUpper.Contains(word.ToUpperInvariant()) && word == word.ToUpperInvariant() && word.Length <= 4)
                {
                    sb.Append(word);
                    continue;
                }

                if (w > 0 && (EnglishSmall.Contains(word.ToLowerInvariant()) || (french && FrenchSmall.Contains(word.ToLowerInvariant()))))
                {
                    sb.Append(word.ToLowerInvariant());
                    continue;
                }

                bool start = true;
                int seg = 0;
                for (int i = 0; i < word.Length; i++)
                {
                    char ch = word[i];
                    if (ch == '-' || ch == '.' || ch == '\'' || ch == '’')
                    {
                        sb.Append(ch);
                        start = ch == '-' || ch == '.' || seg == 1;
                        seg = 0;
                        continue;
                    }

                    sb.Append(start ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch));
                    start = false;
                    seg++;
                }
            }

            string s = sb.ToString();
            // 1ST, 2ND ... keep the ordinal suffix lower case in title case ("1st"): the digits ended the segment, so it was lowered already.
            return s;
        }

        // ---------------------------------------------------------------- noise

        private void Noise(List<Piece> pieces)
        {
            if (_r.Chance(Rt.TypoExample))
            {
                int edits = _r.Chance(0.2) ? 2 : 1;
                for (int e = 0; e < edits; e++)
                {
                    List<int> candidates = [];
                    for (int i = 0; i < pieces.Count; i++)
                    {
                        Piece pc = pieces[i];
                        if (pc.Label is not null && pc.Text.Length >= 4 && pc.Label != "country")
                        {
                            candidates.Add(i);
                        }
                    }

                    if (candidates.Count == 0)
                    {
                        break;
                    }

                    Piece target = pieces[candidates[_r.Int(candidates.Count)]];
                    target.Text = Typo(target.Text, target.Label!);
                    _events.Add("typo");
                }
            }
        }

        private string Typo(string s, string label)
        {
            string edited = Edit(s, label);
            // An edit must leave a span that still starts and ends on a character and has no stray spaces; otherwise keep the original.
            return edited.Length > 0 && edited == edited.Trim() && !edited.Contains("  ", StringComparison.Ordinal) ? edited : s;
        }

        private string Edit(string s, string label)
        {
            bool digits = label is "postal-code" or "street.house-number" or "street.box-number" or "street.unit-number" or "street.route-number" or "street.military-number";
            int i = 1 + _r.Int(s.Length - 2);
            if (!char.IsLetterOrDigit(s[i]))
            {
                return s;
            }

            switch (_r.Weighted([25, 25, 20, 15, 15]))
            {
                case 0:
                    // transposition of two neighbouring letters or digits
                    return char.IsLetterOrDigit(s[i + 1]) ? s[..i] + s[i + 1] + s[i] + s[(i + 2)..] : s;
                case 1:
                    // a dropped character
                    return s.Remove(i, 1);
                case 2:
                    // a doubled character
                    return s.Insert(i, s[i].ToString());
                case 3:
                    // a neighbouring-key slip
                    return s[..i] + Neighbour(s[i]) + s[(i + 1)..];
                default:
                    // an OCR confusion of look-alike glyphs
                    return s[..i] + Ocr(s[i], digits) + s[(i + 1)..];
            }
        }

        private char Neighbour(char c)
        {
            const string rows = "qwertyuiop asdfghjkl zxcvbnm";
            int at = rows.IndexOf(char.ToLowerInvariant(c), StringComparison.Ordinal);
            if (at < 0 || rows[at] == ' ')
            {
                return c;
            }

            int step = _r.Chance(0.5) ? 1 : -1;
            int to = at + step;
            if (to < 0 || to >= rows.Length || rows[to] == ' ')
            {
                to = at - step;
            }

            if (to < 0 || to >= rows.Length || rows[to] == ' ')
            {
                return c;
            }

            char n = rows[to];
            return char.IsUpper(c) ? char.ToUpperInvariant(n) : n;
        }

        private static char Ocr(char c, bool digits) => c switch
        {
            'O' or 'o' => '0',
            '0' => 'O',
            'I' or 'l' => '1',
            '1' => 'l',
            'S' or 's' => '5',
            '5' => 'S',
            'B' => '8',
            '8' => 'B',
            'Z' => '2',
            _ => digits ? c : (char.IsUpper(c) ? 'I' : 'l'),
        };

        // ---------------------------------------------------------------- assembly

        private Example Assemble(List<Piece> pieces, Layout layout)
        {
            if (_r.Chance(Rt.GlueExample))
            {
                // A missing space between two pieces that are separated by a plain space.
                for (int i = 1; i < pieces.Count - 1; i++)
                {
                    if (pieces[i].Label is null && pieces[i].Text == " " && pieces[i - 1].Label is not null && pieces[i + 1].Label is not null)
                    {
                        pieces[i].Text = string.Empty;
                        _events.Add("glued");
                        break;
                    }
                }
            }

            if (_r.Chance(Rt.DoubleSpaceExample))
            {
                for (int i = 1; i < pieces.Count - 1; i++)
                {
                    if (pieces[i].Label is null && pieces[i].Text == " ")
                    {
                        pieces[i].Text = "  ";
                        _events.Add("double-space");
                        break;
                    }
                }
            }

            StringBuilder text = new();
            List<(int Start, int End, string Label)> entities = [];
            foreach (Piece pc in pieces)
            {
                int start = text.Length;
                text.Append(pc.Text);
                if (pc.Label is not null && pc.Text.Length > 0)
                {
                    entities.Add((start, text.Length, pc.Label));
                }
            }

            string t = text.ToString();
            List<(int Start, int End)> tokens = Tokenizer.Tokenize(t, entities);
            List<(int Start, int End, string Label)> fields = [];
            int sStart = int.MaxValue;
            int sEnd = 0;
            foreach ((int Start, int End, string Label) e in entities)
            {
                if (e.Label.StartsWith("street.", StringComparison.Ordinal))
                {
                    sStart = Math.Min(sStart, e.Start);
                    sEnd = Math.Max(sEnd, e.End);
                }
            }

            if (sStart != int.MaxValue)
            {
                fields.Add((sStart, sEnd, "street-address"));
            }

            return new Example
            {
                Id = _rec.Id,
                Country = _rec.Country,
                Language = _lang,
                Layout = layout.Id,
                Text = t,
                Tokens = tokens,
                Entities = entities,
                Fields = fields,
                Events = _events,
            };
        }
    }
}

/// <summary>
/// Word tokens for the examples. Letters, digits and combining marks form a token; every other non-space character is a token on its
/// own (hyphens and apostrophes do not join words here, so a span edge inside "4-123" or "O'Brien" is still a token edge); tokens are
/// then split at every labelled span edge. Based on the pipeline's rule tokenizer, minus its joiners.
/// </summary>
internal static class Tokenizer
{
    internal static List<(int Start, int End)> Tokenize(string text, List<(int Start, int End, string Label)> entities)
    {
        HashSet<int> cuts = [];
        foreach ((int s, int e, _) in entities)
        {
            cuts.Add(s);
            cuts.Add(e);
        }

        List<(int, int)> tokens = [];
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            int start = i;
            if (IsWordPart(text, i))
            {
                i++;
                while (i < text.Length && IsWordPart(text, i) && !cuts.Contains(i))
                {
                    i++;
                }
            }
            else
            {
                i++;
                if (char.IsHighSurrogate(text[i - 1]) && i < text.Length && char.IsLowSurrogate(text[i]))
                {
                    i++;
                }
            }

            tokens.Add((start, i));
        }

        return tokens;
    }

    private static bool IsWordPart(string text, int i)
    {
        UnicodeCategory cat = char.GetUnicodeCategory(text[i]);
        return cat is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
    }
}
