using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using OneContact.Data.AddressTier2A;
using OneContact.Data.AddressUsCa;

namespace OneContact.Data.AddressTier2C;

/// <summary>Croatia, Hungary, Romania and Australia: one real register (Croatia), one register of postcodes and streets (Romania) and two countries synthesized over open place and postcode lists.</summary>
internal static partial class Registers
{
    /// <summary>Per-country selection targets of these four countries (records kept per country after sampling).</summary>
    internal static readonly Dictionary<string, long> TargetsC3 = new(StringComparer.Ordinal)
    {
        ["HR"] = 250_000, ["HU"] = 150_000, ["RO"] = 200_000, ["AU"] = 250_000,
    };

    private static IEnumerable<Addr> GroupC3(string country, string dataRoot, Rejects rejects) => country switch
    {
        "HR" => Croatia(Path.Combine(Directory(dataRoot, "hr-dgu"), "INSPIRE_Addresses_AD.zip"), rejects),
        "HU" => Hungary(Path.Combine(Directory(dataRoot, "hu-geonames"), "HU.zip"), rejects),
        "RO" => Romania(Directory(dataRoot, "ro-posta"), rejects),
        "AU" => Australia(Path.Combine(Directory(dataRoot, "au-geonames"), "AU.zip"), Path.Combine(Directory(dataRoot, "au-abs"), "sal-poa.tsv"), rejects),
        _ => throw new ArgumentException("unknown country " + country),
    };

    private static string TemplatesDir => Path.Combine(AppContext.BaseDirectory, "templates");

    // ---- Croatia: INSPIRE Addresses (Državna geodetska uprava, Otvorena dozvola) ----

    private static readonly XNamespace Ad = "http://inspire.ec.europa.eu/schemas/ad/4.0";
    private static readonly XNamespace Gml = "http://www.opengis.net/gml/3.2";
    private static readonly XNamespace Gn = "http://inspire.ec.europa.eu/schemas/gn/4.0";
    private static readonly XNamespace Base = "http://inspire.ec.europa.eu/schemas/base/3.3";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

    /// <summary>The members of one INSPIRE feature type, one element at a time, so a multi-gigabyte file is never held whole.</summary>
    private static IEnumerable<XElement> Members(ZipArchive zip, string entryName, string localName)
    {
        using Stream stream = zip.GetEntry(entryName)!.Open();
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = true });
        reader.MoveToContent();
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == localName && reader.NamespaceURI == Ad.NamespaceName)
            {
                yield return (XElement)XNode.ReadFrom(reader);
            }
            else
            {
                reader.Read();
            }
        }
    }

    private static string SpellingOf(XElement e) => e.Descendants(Gn + "text").FirstOrDefault()?.Value.Trim() ?? string.Empty;

    [GeneratedRegex(@"^(?<n>\d+)(?<s>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex CroatianNumber();

    // An Address member carries its number (a locator designator, with the letter as an extension or as part of the text) and links to a
    // thoroughfare name, a postal descriptor (post office name and postcode) and two administrative unit names: the settlement
    // (4th order) and the municipality (3rd order). Addresses without a street or a postcode are left out.
    private static IEnumerable<Addr> Croatia(string path, Rejects rejects)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);
        Dictionary<string, string> streets = Members(zip, "ThoroughfareName.gml", "ThoroughfareName")
            .ToDictionary(e => e.Attribute(Gml + "id")!.Value, SpellingOf, StringComparer.Ordinal);
        Dictionary<string, (string Name, string Code)> postal = Members(zip, "PostalDescriptor.gml", "PostalDescriptor")
            .ToDictionary(e => e.Attribute(Gml + "id")!.Value, e => (SpellingOf(e), e.Element(Ad + "postCode")?.Value.Trim() ?? string.Empty), StringComparer.Ordinal);
        Dictionary<string, (string Name, string Level)> admin = Members(zip, "AdminUnitName.gml", "AdminUnitName")
            .ToDictionary(e => e.Attribute(Gml + "id")!.Value, e => (SpellingOf(e), e.Element(Ad + "level")?.Attribute(XLink + "title")?.Value ?? string.Empty), StringComparer.Ordinal);

        foreach (XElement address in Members(zip, "Address.gml", "Address"))
        {
            string id = address.Element(Ad + "inspireId")?.Descendants(Base + "localId").FirstOrDefault()?.Value ?? string.Empty;
            string number = string.Empty;
            string extension = string.Empty;
            foreach (XElement d in address.Descendants(Ad + "LocatorDesignator"))
            {
                string kind = d.Element(Ad + "type")?.Attribute(XLink + "href")?.Value ?? string.Empty;
                string value = d.Element(Ad + "designator")?.Value.Trim() ?? string.Empty;
                if (kind.EndsWith("/addressNumber", StringComparison.Ordinal))
                {
                    number = value;
                }
                else if (kind.EndsWith("/addressNumberExtension", StringComparison.Ordinal))
                {
                    extension = value;
                }
            }

            string street = string.Empty;
            string post = string.Empty;
            string code = string.Empty;
            string settlement = string.Empty;
            foreach (XElement c in address.Elements(Ad + "component"))
            {
                string href = (c.Attribute(XLink + "href")?.Value ?? string.Empty).TrimStart('#');
                if (streets.TryGetValue(href, out string? s))
                {
                    street = s;
                }
                else if (postal.TryGetValue(href, out (string Name, string Code) pd))
                {
                    (post, code) = pd;
                }
                else if (admin.TryGetValue(href, out (string Name, string Level) au) && au.Level == "4thOrder")
                {
                    settlement = au.Name;
                }
            }

            Match m = CroatianNumber().Match(number);
            if (id.Length == 0 || street.Length == 0 || post.Length == 0 || code.Length != 5 || !m.Success)
            {
                rejects.Add("hr: missing street, post office, postcode or house number");
                continue;
            }

            string suffix = (m.Groups["s"].Value + extension).Trim();
            if (suffix.Length > 3 || suffix.Any(ch => !char.IsLetterOrDigit(ch)))
            {
                rejects.Add("hr: house-number extension is not a short letter or digit group");
                continue;
            }

            yield return new Addr
            {
                Id = "dgu:" + id,
                Country = "HR",
                Region = code[..2],
                Language = "hr",
                Street = street,
                HouseNumber = m.Groups["n"].Value,
                Suffix = suffix,
                Locality = post,
                Dependent = settlement == post ? string.Empty : settlement,
                PostalCode = code,
            };
        }
    }

    // ---- GeoNames postal codes (CC BY 4.0) for Hungary and Australia: places and postcodes are real, streets and numbers are drawn ----

    private static IEnumerable<string[]> GeoNamesRows(string zipPath, string entryName)
    {
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        using StreamReader sr = new(zip.GetEntry(entryName)!.Open(), new UTF8Encoding(false));
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            if (line.Length > 0)
            {
                yield return line.Split('\t');
            }
        }
    }

    /// <summary>Street names written for these templates: "name|type" per line, by country.</summary>
    private static List<string> StreetList(string country)
    {
        List<string> list = [];
        foreach (string line in File.ReadLines(Path.Combine(TemplatesDir, "tier2c3-streets.tsv")))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] c = line.Split('\t');
            if (c[0] == country)
            {
                string[] parts = c[1].Split('|');
                list.Add(parts[0] + " " + parts[1]);
            }
        }

        return list;
    }

    // Streets per place: more than the real place count needs, so a small village still gets as many examples as a town. Drawn from the
    // record id, so a run is repeatable.
    private const int HungaryStreetsPerPlace = 42;
    private const int AustraliaStreetsPerPlace = 14;

    private static IEnumerable<Addr> Hungary(string zipPath, Rejects rejects)
    {
        List<string> streets = StreetList("HU");
        HashSet<string> seen = [];
        foreach (string[] c in GeoNamesRows(zipPath, "HU.txt"))
        {
            string postal = c[1].Trim();
            string place = c[2].Trim();
            if (postal.Length != 4 || place.Length == 0 || !postal.All(char.IsAsciiDigit))
            {
                rejects.Add("hu: postcode is not four digits or no place name");
                continue;
            }

            if (!seen.Add(postal + "|" + place))
            {
                rejects.Add("hu: repeated postcode and place");
                continue;
            }

            for (int i = 0; i < HungaryStreetsPerPlace; i++)
            {
                string id = $"gn-hu:{postal}:{place}:{i}";
                ulong h = Prng.Hash(id, 0x4855);
                yield return new Addr
                {
                    Id = id,
                    Country = "HU",
                    Region = c.Length > 4 && c[4].Length > 0 ? c[4] : "?",
                    Language = "hu",
                    Street = streets[(int)(h % (ulong)streets.Count)],
                    HouseNumber = (1 + (int)((h >> 20) % 140)).ToString(CultureInfo.InvariantCulture),
                    Suffix = ((h >> 40) % 10 == 0) ? ((char)('A' + (int)((h >> 48) % 4))).ToString() : string.Empty,
                    Locality = place,
                    PostalCode = postal,
                };
            }
        }
    }

    private static readonly string[] AustralianStates = ["", "NSW", "VIC", "QLD", "SA", "WA", "TAS", "NT", "ACT"];

    /// <summary>"Abbotsford (NSW)" and "Greenmount (Mackay - Qld)" to "Abbotsford" and "Greenmount": ABS adds the state or the council to make names unique.</summary>
    private static string WithoutAbsQualifier(string name)
    {
        int open = name.LastIndexOf(" (", StringComparison.Ordinal);
        return open > 0 && name.EndsWith(')') ? name[..open] : name;
    }

    /// <summary>
    /// ABS suburbs and localities with the postal area each falls in (SAL to POA correspondence, ASGS Edition 3): the first digit of the
    /// SAL code is the state or territory. Special purpose codes (no usual address, migratory, outside Australia), the "Remainder"
    /// areas, other territories and rows below the ABS minimum output size are left out. Then the GeoNames pairs the ABS file does not
    /// have are added.
    /// </summary>
    private static IEnumerable<(string Postal, string Place, string State, string Source)> AustralianPlaces(string geoNamesZip, string absTsv, Rejects rejects)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in File.ReadLines(absTsv).Skip(1))
        {
            string[] c = line.Split('	');
            string code = c[0];
            string postal = c[2];
            if (code.Length != 5 || !code.All(char.IsAsciiDigit) || code[0] is '0' or '9' || code[1..] is "9494" or "9797" || c[1].Contains("Remainder", StringComparison.Ordinal))
            {
                rejects.Add("au: ABS special purpose code, remainder area or other territory");
                continue;
            }

            if (postal.Length != 4 || !postal.All(char.IsAsciiDigit) || c[6] != "0")
            {
                rejects.Add("au: ABS pair without a four-digit postal area, or below the minimum output size");
                continue;
            }

            string place = WithoutAbsQualifier(c[1]);
            string state = AustralianStates[code[0] - '0'];
            if (!seen.Add(postal + "|" + place + "|" + state))
            {
                rejects.Add("au: repeated postcode, place and state");
                continue;
            }

            yield return (postal, place, state, "abs");
        }

        foreach (string[] c in GeoNamesRows(geoNamesZip, "AU.txt"))
        {
            string postal = c[1].Trim();
            string place = c[2].Trim();
            string state = c.Length > 4 ? c[4].Trim() : string.Empty;
            if (postal.Length != 4 || place.Length == 0 || state.Length == 0 || !postal.All(char.IsAsciiDigit))
            {
                rejects.Add("au: postcode is not four digits, or no place or state");
                continue;
            }

            if (!seen.Add(postal + "|" + place + "|" + state))
            {
                rejects.Add("au: repeated postcode, place and state");
                continue;
            }

            yield return (postal, place, state, "gn");
        }
    }

    private static IEnumerable<Addr> Australia(string geoNamesZip, string absTsv, Rejects rejects)
    {
        List<string> streets = StreetList("AU");
        foreach ((string postal, string place, string state, string source) in AustralianPlaces(geoNamesZip, absTsv, rejects))
        {
            for (int i = 0; i < AustraliaStreetsPerPlace; i++)
            {
                string id = $"{source}-au:{postal}:{place}:{state}:{i}";
                ulong h = Prng.Hash(id, 0x4155);
                yield return new Addr
                {
                    Id = id,
                    Country = "AU",
                    Region = state,
                    Language = "en",
                    Street = streets[(int)(h % (ulong)streets.Count)],
                    HouseNumber = (1 + (int)((h >> 20) % 220)).ToString(CultureInfo.InvariantCulture),
                    Suffix = ((h >> 40) % 14 == 0) ? ((char)('A' + (int)((h >> 48) % 4))).ToString() : string.Empty,
                    Locality = place,
                    Admin = state,
                    PostalCode = postal,
                };
            }
        }
    }

    // ---- Romania: Poșta Română postal codes with SIRUTA (OGL-ROU-1.0), converted from the published workbook by tools/data/ro-infocod-to-tsv.py ----

    // Street types as the register writes them, in the definite form used before a name.
    private static readonly Dictionary<string, string> RomanianTypes = new(StringComparer.Ordinal)
    {
        ["Stradă"] = "Strada", ["Strada"] = "Strada", ["Bulevard"] = "Bulevardul", ["Aleea"] = "Aleea", ["Alee"] = "Aleea", ["Șosea"] = "Șoseaua",
        ["Calea"] = "Calea", ["Cale"] = "Calea", ["Piața"] = "Piața", ["Piață"] = "Piața", ["Splai"] = "Splaiul", ["Drum"] = "Drumul",
        ["Intrare"] = "Intrarea", ["Fundătură"] = "Fundătura", ["Fundătura"] = "Fundătura",
        ["Stradelă"] = "Stradela", ["Fundac"] = "Fundacul", ["Prelungire"] = "Prelungirea", ["Pasaj"] = "Pasajul", ["Uliță"] = "Ulița",
        ["Sosea"] = "Șoseaua", ["Șir"] = "Șirul", ["Parc"] = "Parcul", ["Scuar"] = "Scuarul", ["Esplanadă"] = "Esplanada", ["Piațetă"] = "Piațeta",
        ["Trecătoare"] = "Trecătoarea",
    };

    [GeneratedRegex(@"^nr\.\s*(?<a>\d+)(?<al>[A-Za-z]?)(?:\s*-\s*(?:(?<b>\d+)(?<bl>[A-Za-z]?)|(?<t>T)))?\b", RegexOptions.CultureInvariant)]
    private static partial Regex RomanianNumbers();

    /// <summary>Replaces the cedilla forms the workbook uses by the comma-below letters of today's orthography.</summary>
    private static string CommaBelow(string s) => s.Replace('ş', 'ș').Replace('ţ', 'ț').Replace('Ş', 'Ș').Replace('Ţ', 'Ț').Trim();

    private const int RomaniaNumbersPerRow = 3;

    private static IEnumerable<Addr> Romania(string dir, Rejects rejects)
    {
        foreach (Addr a in RomaniaStreets(Path.Combine(dir, "bucuresti.tsv"), "b", rejects))
        {
            yield return a;
        }

        foreach (Addr a in RomaniaStreets(Path.Combine(dir, "orase.tsv"), "o", rejects))
        {
            yield return a;
        }

        foreach (Addr a in RomaniaVillages(Path.Combine(dir, "localitati.tsv"), rejects))
        {
            yield return a;
        }
    }

    private static string Type(string register, Rejects rejects)
    {
        string t = CommaBelow(register);
        if (RomanianTypes.TryGetValue(t, out string? definite))
        {
            return definite;
        }

        rejects.Add("ro: street type not in the table");
        return string.Empty;
    }

    // Bucharest rows: type, name, number range, postcode, sector. Cities over 50,000: county, locality, type, name, number range, postcode.
    private static IEnumerable<Addr> RomaniaStreets(string path, string kind, Rejects rejects)
    {
        bool bucharest = kind == "b";
        int row = 0;
        foreach (string line in File.ReadLines(path).Skip(1))
        {
            row++;
            string[] c = line.Split('\t');
            string type = Type(bucharest ? c[0] : c[2], rejects);
            string name = CommaBelow(bucharest ? c[1] : c[3]);
            string range = (bucharest ? c[2] : c[4]).Trim();
            string postal = (bucharest ? c[3] : c[5]).Trim();
            if (type.Length == 0)
            {
                continue;
            }

            // An empty range is a postcode for the whole street (Poșta Română's file leaves the number column blank), so any number fits;
            // blocks ("bl. 54") and other forms are left out.
            Match m = RomanianNumbers().Match(range);
            bool wholeStreet = range.Length == 0;
            if (name.Length == 0 || postal.Length != 6 || !(m.Success || wholeStreet))
            {
                rejects.Add("ro: no street name or six-digit postcode, or a house-number cell that is a block list or an odd form");
                continue;
            }

            int a = wholeStreet ? 1 : int.Parse(m.Groups["a"].Value, CultureInfo.InvariantCulture);
            int b = wholeStreet ? a + 90 : m.Groups["b"].Success ? int.Parse(m.Groups["b"].Value, CultureInfo.InvariantCulture) : m.Groups["t"].Success ? a + 40 : a;
            b = Math.Min(Math.Max(b, a), a + 400);
            string sector = bucharest && c[4].Length > 0 ? "Sector " + c[4].Trim() : string.Empty;
            string county = bucharest ? string.Empty : CommaBelow(c[0]);
            string locality = bucharest ? "București" : CommaBelow(c[1]);
            for (int i = 0; i < RomaniaNumbersPerRow; i++)
            {
                string id = $"infocod:{kind}:{row}:{i}";
                ulong h = Prng.Hash(id, 0x524F);
                // Ranges of one side of a street run in steps of two ("1-25" are the odd numbers).
                int steps = (b - a) / 2 + 1;
                int number = wholeStreet ? a + (int)(h % 91UL) : a + 2 * (int)(h % (ulong)steps);
                bool first = i == 0 && !wholeStreet && m.Groups["al"].Success && m.Groups["al"].Value.Length > 0 && !m.Groups["b"].Success;
                yield return new Addr
                {
                    Id = id,
                    Country = "RO",
                    Region = bucharest ? "B" + c[4].Trim() : county,
                    Language = "ro",
                    Street = type + " " + name,
                    HouseNumber = number.ToString(CultureInfo.InvariantCulture),
                    Suffix = first ? m.Groups["al"].Value : string.Empty,
                    Locality = locality,
                    Dependent = sector,
                    Admin = county,
                    PostalCode = postal,
                };
            }
        }
    }

    // Localities under 50,000: county, locality, postcode. A village address is the village and a number ("Buciumeni 45"), the way
    // rural addresses are written when there are no street names; half carry the word "Sat" (village).
    private static IEnumerable<Addr> RomaniaVillages(string path, Rejects rejects)
    {
        int row = 0;
        foreach (string line in File.ReadLines(path).Skip(1))
        {
            row++;
            string[] c = line.Split('\t');
            string county = CommaBelow(c[0]);
            string locality = CommaBelow(c[1]);
            string postal = c[2].Trim();
            if (locality.Length == 0 || postal.Length != 6)
            {
                rejects.Add("ro: village row without a locality or six-digit postcode");
                continue;
            }

            for (int i = 0; i < RomaniaNumbersPerRow; i++)
            {
                string id = $"infocod:v:{row}:{i}";
                ulong h = Prng.Hash(id, 0x5256);
                yield return new Addr
                {
                    Id = id,
                    Country = "RO",
                    Region = county,
                    Language = "ro",
                    Street = (h & 1) == 0 ? locality : "Sat " + locality,
                    HouseNumber = (1 + (int)((h >> 8) % 300)).ToString(CultureInfo.InvariantCulture),
                    Locality = locality,
                    Admin = county,
                    PostalCode = postal,
                };
            }
        }
    }
}
