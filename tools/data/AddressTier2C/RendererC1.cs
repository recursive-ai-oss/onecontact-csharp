using System.Globalization;
using System.Text.RegularExpressions;
using OneContact.Data.AddressUsCa;

namespace OneContact.Data.AddressTier2C;

// Layouts of the group C-1 countries: Mexico, Poland, Slovenia, Slovakia and Lithuania. They plug into the shared renderer
// at three places (delivery line, post box, last line); nothing here draws a random number for another country, so the
// output of the other countries does not change.
internal sealed partial class Renderer
{
    private static readonly Dictionary<string, string[]> CountryNamesC1 = new(StringComparer.Ordinal)
    {
        ["MX"] = ["México", "Mexico", "MÉXICO"],
        ["PL"] = ["Polska", "Poland"],
        ["SI"] = ["Slovenija", "Slovenia"],
        ["SK"] = ["Slovensko", "Slovakia", "Slovenská republika"],
        ["LT"] = ["Lietuva", "Lithuania"],
    };

    private static readonly Dictionary<string, (string[] Words, double[] Weights)> MexicanStreetTypes = new(StringComparer.Ordinal)
    {
        ["CALLE"] = (["Calle", "C.", ""], [0.45, 0.15, 0.40]),
        ["AVENIDA"] = (["Avenida", "Av.", "Av"], [0.45, 0.40, 0.15]),
        ["BOULEVARD"] = (["Boulevard", "Blvd.", "Bulevar"], [0.4, 0.45, 0.15]),
        ["CALZADA"] = (["Calzada", "Calz."], [0.6, 0.4]),
        ["CARRETERA"] = (["Carretera", "Carr."], [0.6, 0.4]),
        ["CERRADA"] = (["Cerrada", "Cda."], [0.7, 0.3]),
        ["PRIVADA"] = (["Privada", "Priv."], [0.7, 0.3]),
        ["PROLONGACION"] = (["Prolongación", "Prol."], [0.6, 0.4]),
        ["CIRCUITO"] = (["Circuito", "Cto."], [0.7, 0.3]),
        ["ANDADOR"] = (["Andador", "And."], [0.7, 0.3]),
        ["CALLEJON"] = (["Callejón", "Cjón."], [0.7, 0.3]),
        ["EJE VIAL"] = (["Eje Vial", "Eje"], [0.5, 0.5]),
        ["PERIFERICO"] = (["Periférico", "Perif."], [0.7, 0.3]),
        ["VIADUCTO"] = (["Viaducto"], [1]),
        ["AUTOPISTA"] = (["Autopista"], [1]),
        ["PEATONAL"] = (["Peatonal"], [1]),
        ["RETORNO"] = (["Retorno"], [1]),
        ["DIAGONAL"] = (["Diagonal"], [1]),
        ["CONTINUACION"] = (["Continuación", "Cont."], [0.7, 0.3]),
        ["PASAJE"] = (["Pasaje"], [1]),
        ["CORREDOR"] = (["Corredor"], [1]),
        ["AMPLIACION"] = (["Ampliación", "Ampl."], [0.7, 0.3]),
        ["CIRCUNVALACION"] = (["Circunvalación"], [1]),
    };

    private static readonly Dictionary<string, (string[] Words, double[] Weights)> MexicanPlaceTypes = new(StringComparer.Ordinal)
    {
        ["COLONIA"] = (["Col.", "Colonia", "Col"], [0.6, 0.3, 0.1]),
        ["FRACCIONAMIENTO"] = (["Fracc.", "Fraccionamiento"], [0.6, 0.4]),
        ["BARRIO"] = (["Barrio"], [1]),
        ["PUEBLO"] = (["Pueblo"], [1]),
        ["UNIDAD HABITACIONAL"] = (["Unidad Habitacional", "U. H."], [0.6, 0.4]),
        ["EJIDO"] = (["Ejido"], [1]),
        ["RESIDENCIAL"] = (["Residencial"], [1]),
        ["CONJUNTO HABITACIONAL"] = (["Conjunto Habitacional"], [1]),
    };

    [GeneratedRegex(@"^(Al\.|Pl\.|Os\.|Rondo|Aleja|Plac|Osiedle|Skwer|Bulwar|Droga|Trakt)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PolishStreetWithKind();

    internal static bool IsGroupC1(string country) => country is "MX" or "PL" or "SI" or "SK" or "LT";

    private sealed partial class Ctx
    {
        private bool C1 => IsGroupC1(_a.Country);

        private static string[] CountryNamesFor(string cc) => CountryNamesC1[cc];

        private static string Number(string house, string suffix, string joiner) => suffix.Length == 0 ? house : house + joiner + suffix;

        // Street name and number of the five countries; the number follows the name everywhere here.
        private List<Piece> DeliveryC1()
        {
            string cc = _a.Country;
            string street = _a.Street;
            if (cc == "MX" && _a.StreetType.Length > 0)
            {
                (string[] words, double[] weights) = MexicanStreetTypes.GetValueOrDefault(_a.StreetType, ([""], [1.0]));
                string word = words[_r.Weighted(weights)];
                street = word.Length == 0 ? street : word + " " + street;
            }
            else if (cc == "PL" && _a.StreetType == "STREET" && !PolishStreetWithKind().IsMatch(street))
            {
                // "ul. Sadowa 30", "Sadowa 30", "ulica Sadowa 30".
                string word = new[] { "ul. ", "", "ulica " }[_r.Weighted([0.78, 0.14, 0.08])];
                street = word + street;
            }

            List<Piece> p = [new Piece(Case(street), "street.name")];
            if (_a.HouseNumber.Length == 0)
            {
                return p;
            }

            p.Add(new Piece(_r.Chance(0.03) ? ", " : " ", null));
            switch (cc)
            {
                case "MX":
                    MexicanNumber(p);
                    break;
                case "PL":
                    PolishNumber(p);
                    break;
                case "SI":
                    // "Cankarjeva ulica 27", "Dolenjska cesta 11 c", "11c".
                    p.Add(new Piece(Number(_a.HouseNumber, _a.Suffix, _r.Chance(0.5) ? " " : string.Empty), "street.house-number"));
                    break;
                case "SK":
                    // The orientation number alone ("Hlavná 12") or with the registration number before it ("Hlavná 15/12").
                    string sk = _a.AltNumber.Length > 0 && _r.Chance(0.4) ? _a.AltNumber + "/" + _a.HouseNumber : _a.HouseNumber;
                    p.Add(new Piece(sk, "street.house-number"));
                    break;
                default:
                    LithuanianNumber(p);
                    break;
            }

            return p;
        }

        private void MexicanNumber(List<Piece> p)
        {
            string number = Number(_a.HouseNumber, _a.Suffix, _r.Chance(0.7) ? string.Empty : "-");
            if (_a.HouseNumber == "S/N")
            {
                p.Add(new Piece(_mode == 2 ? "s/n" : _r.Chance(0.7) ? "S/N" : "s/n", "street.house-number"));
                return;
            }

            switch (_r.Weighted([0.62, 0.12, 0.18, 0.08]))
            {
                case 1:
                    p.Add(new Piece("#", null));
                    p.Add(new Piece(number, "street.house-number"));
                    break;
                case 2:
                    p.Add(new Piece(Case("No.") + " " + number, "street.house-number"));
                    break;
                case 3:
                    p.Add(new Piece(Case("Núm.") + " " + number, "street.house-number"));
                    break;
                default:
                    p.Add(new Piece(number, "street.house-number"));
                    break;
            }

            if (_a.Unit.Length > 0 && _r.Chance(0.7))
            {
                // "Int. 5", "Interior 5", "Depto. 5".
                string designator = _r.Pick(new[] { "Int.", "Interior", "Depto.", "Int" });
                p.Add(new Piece(_r.Chance(0.6) ? ", " : " ", null));
                p.Add(new Piece(Case(designator), "street.unit-designator"));
                p.Add(new Piece(" ", null));
                p.Add(new Piece(_a.Unit, "street.unit-number"));
                _events.Add("unit:register");
            }
        }

        private void PolishNumber(List<Piece> p)
        {
            p.Add(new Piece(_mode == 2 ? _a.HouseNumber.ToLowerInvariant() : _a.HouseNumber, "street.house-number"));
            if (_a.StreetType == "STREET" && _r.Chance(0.22))
            {
                // Flat numbers: "30/5", "30 m. 5", "30 lok. 5", "30, m. 5".
                string flat = (1 + _r.Int(120)).ToString(CultureInfo.InvariantCulture);
                switch (_r.Weighted([0.5, 0.25, 0.15, 0.10]))
                {
                    case 0:
                        p.Add(new Piece("/", null));
                        break;
                    case 1:
                        p.Add(new Piece(" ", null));
                        p.Add(new Piece(Case("m."), "street.unit-designator"));
                        p.Add(new Piece(" ", null));
                        break;
                    case 2:
                        p.Add(new Piece(" ", null));
                        p.Add(new Piece(Case("lok."), "street.unit-designator"));
                        p.Add(new Piece(" ", null));
                        break;
                    default:
                        p.Add(new Piece(", ", null));
                        p.Add(new Piece(Case("m."), "street.unit-designator"));
                        p.Add(new Piece(" ", null));
                        break;
                }

                p.Add(new Piece(flat, "street.unit-number"));
                _events.Add("unit:synthetic");
            }
        }

        private void LithuanianNumber(List<Piece> p)
        {
            string number = _a.HouseNumber;
            if (_a.Suffix.Length > 0)
            {
                // Building block: "15 k. 7" or "15, korpusas 7" (the register's own text).
                number += _r.Chance(0.55) ? " k. " + _a.Suffix : ", korpusas " + _a.Suffix;
            }

            p.Add(new Piece(_mode == 2 ? number.ToLowerInvariant() : number, "street.house-number"));
            if (_a.Unit.Length > 0 && _r.Chance(0.85))
            {
                // Flats: "10-5", "10, butas 5", "10 b. 5", "10/5".
                switch (_r.Weighted([0.6, 0.2, 0.1, 0.1]))
                {
                    case 0:
                        p.Add(new Piece("-", null));
                        break;
                    case 1:
                        p.Add(new Piece(", ", null));
                        p.Add(new Piece(Case("butas"), "street.unit-designator"));
                        p.Add(new Piece(" ", null));
                        break;
                    case 2:
                        p.Add(new Piece(" ", null));
                        p.Add(new Piece(Case("b."), "street.unit-designator"));
                        p.Add(new Piece(" ", null));
                        break;
                    default:
                        p.Add(new Piece("/", null));
                        break;
                }

                p.Add(new Piece(_a.Unit, "street.unit-number"));
                _events.Add("unit:register");
            }
        }

        private List<Piece> PostBoxC1(string cc)
        {
            string[] designators = cc switch
            {
                "MX" => ["Apartado Postal", "Apdo. Postal", "Apdo."],
                "PL" => ["Skrytka pocztowa", "skr. poczt.", "Skrytka"],
                "SI" => ["Poštni predal", "p. p.", "PP"],
                "SK" => ["Poštový priečinok", "P.O.BOX", "PO BOX"],
                _ => ["Pašto dėžutė", "p. d.", "PD"],
            };
            string number = (1 + _r.Int(_r.Chance(0.5) ? 999 : 9999)).ToString(CultureInfo.InvariantCulture);
            return [new Piece(Case(_r.Pick(designators)), "street.box-designator"), new Piece(" ", null), new Piece(number, "street.box-number")];
        }

        private string Prefixed(string[] words, double[] weights, string name)
        {
            string word = words[_r.Weighted(weights)];
            return word.Length == 0 ? name : word + " " + name;
        }

        // The last line, and a line of its own for the neighbourhood (Mexico), settlement (Slovenia, Slovakia) or village and municipality (Lithuania).
        private List<Piece> LastLineC1(string cc, out List<Piece> dependentLine)
        {
            dependentLine = [];
            bool noPostal = _r.Chance(_rt.MissingPostalCode);
            bool noPlace = !noPostal && _r.Chance(_rt.MissingLocality);
            string place = Case(_a.Locality);
            string postal = _a.PostalCode;
            List<Piece> p = [];
            switch (cc)
            {
                case "MX":
                    MexicanLastLine(p, place, postal, noPostal, noPlace, out dependentLine);
                    break;
                case "LT":
                    LithuanianLastLine(p, place, postal, noPostal, noPlace, out dependentLine);
                    break;
                default:
                    // "97-300 Piotrków Trybunalski", "6250 Ilirska Bistrica", "811 01 Bratislava": postcode first.
                    if (!noPostal)
                    {
                        if (_r.Chance(_rt.CountryCodePrefix))
                        {
                            p.Add(new Piece(cc, "country"));
                            p.Add(new Piece("-", null));
                            _events.Add("code-prefix");
                        }

                        string written = cc switch
                        {
                            "PL" => _r.Chance(0.9) ? postal[..2] + "-" + postal[2..] : postal,
                            "SK" => _r.Chance(0.85) ? postal[..3] + " " + postal[3..] : postal,
                            _ => postal,
                        };
                        p.Add(new Piece(written, "postal-code"));
                    }

                    if (!noPlace)
                    {
                        if (p.Count > 0)
                        {
                            p.Add(new Piece(" ", null));
                        }

                        p.Add(new Piece(place, "locality"));
                    }

                    if (_a.Dependent.Length > 0 && _r.Chance(_rt.Dependent * 0.6))
                    {
                        dependentLine = [new Piece(Case(_a.Dependent), "dependent-locality")];
                        _events.Add("dependent-locality");
                    }

                    break;
            }

            if (p.Count == 0)
            {
                p.Add(new Piece(Case(_a.Locality), "locality"));
            }

            return p;
        }

        // "06760 Cuauhtémoc, Ciudad de México", "C.P. 06760, Cuauhtémoc, Ciudad de México", "Cuauhtémoc, Ciudad de México, C.P. 06760", "CP 06760 Cuauhtémoc";
        // the neighbourhood is its own line: "Col. Roma Sur".
        private void MexicanLastLine(List<Piece> p, string place, string postal, bool noPostal, bool noPlace, out List<Piece> dependentLine)
        {
            dependentLine = [];
            if (_a.Dependent.Length > 0 && _r.Chance(0.85))
            {
                (string[] words, double[] weights) = MexicanPlaceTypes.GetValueOrDefault(_a.PlaceType, ([""], [1.0]));
                // The name may already begin with the kind ("Barrio Uno" of kind BARRIO): no second prefix then.
                bool kindInName = _a.Dependent.StartsWith(words[0], StringComparison.OrdinalIgnoreCase) || _a.Dependent.StartsWith(_a.PlaceType, StringComparison.OrdinalIgnoreCase);
                dependentLine = [new Piece(Case(kindInName ? _a.Dependent : Prefixed(words, weights, _a.Dependent)), "dependent-locality")];
                _events.Add("dependent-locality");
            }

            int style = _r.Weighted([0.55, 0.25, 0.12, 0.08]);
            bool withState = style != 3 && _r.Chance(0.85);
            string state = Case(_a.Admin);
            if (style == 2)
            {
                if (!noPlace)
                {
                    p.Add(new Piece(place, "locality"));
                }

                if (withState)
                {
                    if (p.Count > 0)
                    {
                        p.Add(new Piece(", ", null));
                    }

                    p.Add(new Piece(state, "admin-area"));
                }

                if (!noPostal)
                {
                    if (p.Count > 0)
                    {
                        p.Add(new Piece(", ", null));
                    }

                    p.Add(new Piece(Case("C.P.") + " " + postal, "postal-code"));
                }

                return;
            }

            if (!noPostal)
            {
                if (style == 0 && _r.Chance(_rt.CountryCodePrefix))
                {
                    p.Add(new Piece("MX", "country"));
                    p.Add(new Piece("-", null));
                    _events.Add("code-prefix");
                }

                p.Add(new Piece(style == 1 ? Case("C.P.") + " " + postal : style == 3 ? Case("CP") + " " + postal : postal, "postal-code"));
            }

            if (!noPlace)
            {
                if (p.Count > 0)
                {
                    p.Add(new Piece(style == 1 ? ", " : " ", null));
                }

                p.Add(new Piece(place, "locality"));
            }

            if (withState)
            {
                if (p.Count > 0)
                {
                    p.Add(new Piece(", ", null));
                }

                p.Add(new Piece(state, "admin-area"));
            }
        }

        // "LT-01103 Vilnius"; a village address has its own line "Akmeniškių k., Vilniaus r. sav." before it.
        private void LithuanianLastLine(List<Piece> p, string place, string postal, bool noPostal, bool noPlace, out List<Piece> dependentLine)
        {
            dependentLine = [];
            if (_a.Dependent.Length > 0 && _r.Chance(_rt.Dependent))
            {
                dependentLine = [new Piece(Case(_a.Dependent), "dependent-locality")];
                if (_r.Chance(0.5))
                {
                    dependentLine.Add(new Piece(", ", null));
                    dependentLine.Add(new Piece(Case(_a.Admin), "admin-area"));
                }

                _events.Add("dependent-locality");
            }
            else if (_r.Chance(0.06))
            {
                dependentLine = [new Piece(Case(_a.Admin), "admin-area")];
                _events.Add("municipality");
            }

            if (!noPostal)
            {
                p.Add(new Piece(_r.Chance(0.9) ? "LT-" + postal : postal, "postal-code"));
            }

            if (!noPlace)
            {
                if (p.Count > 0)
                {
                    p.Add(new Piece(" ", null));
                }

                p.Add(new Piece(place, "locality"));
            }
        }
    }
}
