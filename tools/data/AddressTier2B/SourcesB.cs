using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using OneContact.Data.AddressTier2A;
using OneContact.Data.AddressUsCa;

namespace OneContact.Data.AddressTier2B;

/// <summary>One address in the shape the renderer reads, whichever source it came from. Street type and name are kept apart.</summary>
internal sealed record RecB
{
    internal string Id { get; init; } = string.Empty;

    /// <summary>ISO 3166 alpha-2 country: GB, JE, GG, IM, IE, ES, PT, IT, MT, SM, VA or AD.</summary>
    internal string Country { get; init; } = string.Empty;

    /// <summary>The unit sampling is balanced over (a region, province, distrito or country part).</summary>
    internal string Region { get; init; } = string.Empty;

    internal string Language { get; init; } = string.Empty;

    /// <summary>The street-type word as written in <see cref="Language"/> (Calle, Via, Road), or empty when the name carries none.</summary>
    internal string Type { get; init; } = string.Empty;

    internal string Name { get; init; } = string.Empty;

    /// <summary>True when the type is written in front of the name (Spanish, Italian, Portuguese, Maltese, Catalan), false after it (English).</summary>
    internal bool TypeBefore { get; init; }

    /// <summary>The street in the country's other language when the source has it (Welsh, German in South Tyrol), with its type and language.</summary>
    internal string AltType { get; init; } = string.Empty;

    internal string AltName { get; init; } = string.Empty;

    internal string AltLanguage { get; init; } = string.Empty;

    internal string Number { get; init; } = string.Empty;

    /// <summary>Letter or word that follows the number (Italian "A" in 12/A, "bis"), or empty.</summary>
    internal string Suffix { get; init; } = string.Empty;

    internal string Locality { get; init; } = string.Empty;

    internal string LocalityAlt { get; init; } = string.Empty;

    /// <summary>Hamlet or frazione written next to the street.</summary>
    internal string Dependent { get; init; } = string.Empty;

    /// <summary>County, province or provincial abbreviation written after the locality.</summary>
    internal string Admin { get; init; } = string.Empty;

    internal string AdminAbbr { get; init; } = string.Empty;

    internal string PostalCode { get; init; } = string.Empty;

    /// <summary>True when the postcode was drawn (inward part of a UK/Crown dependency or Eircode, Maltese digits, a postcode picked among a city's).</summary>
    internal bool PostalSynthetic { get; init; }
}

/// <summary>Where each source lives in the data cache and how its rows become <see cref="RecB"/> records.</summary>
internal static partial class SourcesB
{
    /// <summary>How many records the countries made only from postcode lists and our street vocabulary draw (set from the targets).</summary>
    internal static readonly Dictionary<string, long> SyntheticCount = new(StringComparer.Ordinal);

    internal static string Directory(string dataRoot, string prefix)
    {
        string[] found = System.IO.Directory.GetDirectories(dataRoot, prefix + "-*");
        return found.Length == 1 ? found[0] : throw new InvalidOperationException($"expected one {prefix}-* directory in {dataRoot}, found {found.Length}");
    }

    internal static IEnumerable<RecB> Read(string country, string dataRoot, string templates, Rejects rejects) => country switch
    {
        "GB" => UkRoads(Path.Combine(Directory(dataRoot, "osopennames"), "opname_csv_gb.zip"), rejects),
        "ES" => Spain(Directory(dataRoot, "catastro"), rejects),
        "IT" => Italy(Directory(dataRoot, "anncsu"), Directory(dataRoot, "istat"), Path.Combine(Directory(dataRoot, "geonames"), "IT.zip"), rejects),
        "PT" or "IE" or "MT" or "SM" or "VA" or "AD" or "JE" or "GG" or "IM" => Synthetic(country, Directory(dataRoot, "geonames"), templates, rejects),
        _ => throw new ArgumentException("unknown country " + country),
    };

    private static string Squeeze(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    // ---------- United Kingdom: OS Open Names (OGL v3) ----------

    // A reader that gives DelimitedTable the product's separate header line before the header-less data file.
    private sealed class HeaderReader(string header, TextReader rest) : TextReader
    {
        private bool _given;

        public override string? ReadLine()
        {
            if (_given)
            {
                return rest.ReadLine();
            }

            _given = true;
            return header;
        }
    }

    private static readonly HashSet<string> UkRoadTypes = new(LexB.EnglishTypes, StringComparer.Ordinal);

    private readonly record struct Postcode(string District, string Text, int X, int Y, string Place);

    // Roads come from "Named Road" features and postcode units from "Postcode" features. Each road is paired with the postcode units nearest to the
    // road's position in the same 10 km tile and the same postcode district: the pairing is a stand-in for an address register, so a postcode
    // is plausible for its street but is not a statement about which postcode the street's houses have.
    private static IEnumerable<RecB> UkRoads(string zipPath, Rejects rejects)
    {
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        string header;
        using (StreamReader hr = new(zip.GetEntry("Doc/OS_Open_Names_Header.csv")!.Open(), new UTF8Encoding(false), true))
        {
            header = hr.ReadLine()!;
        }

        foreach (ZipArchiveEntry entry in zip.Entries.Where(e => e.FullName.StartsWith("Data/", StringComparison.Ordinal) && e.FullName.EndsWith(".csv", StringComparison.Ordinal)).OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            List<Postcode> postcodes = [];
            List<(string Id, string Name, string Name2, string Lang2, string Lang1, int X, int Y, string District, string Place, string County, string Region)> roads = [];
            using (StreamReader sr = new(entry.Open(), new UTF8Encoding(false), true, 1 << 16))
            {
                DelimitedTable t = new(new HeaderReader(header, sr), ',');
                while (t.Read())
                {
                    string local = t["LOCAL_TYPE"];
                    if (local == "Postcode")
                    {
                        if (int.TryParse(t["GEOMETRY_X"], out int px) && int.TryParse(t["GEOMETRY_Y"], out int py) && t["NAME1"].Length >= 5)
                        {
                            postcodes.Add(new Postcode(t["POSTCODE_DISTRICT"].Length > 0 ? t["POSTCODE_DISTRICT"] : PostcodeDistrict(t["NAME1"]), t["NAME1"], px, py, t["POPULATED_PLACE"]));
                        }
                    }
                    else if (local == "Named Road" && int.TryParse(t["GEOMETRY_X"], out int rx) && int.TryParse(t["GEOMETRY_Y"], out int ry))
                    {
                        string region = t["REGION"].Length > 0 ? t["REGION"] : t["COUNTRY"];
                        roads.Add((t["ID"], t["NAME1"], t["NAME2"], t["NAME2_LANG"], t["NAME1_LANG"], rx, ry, t["POSTCODE_DISTRICT"], t["POPULATED_PLACE"], t["COUNTY_UNITARY"], region));
                    }
                }
            }

            // 1 km grid over the tile's postcode units.
            Dictionary<(int, int), List<int>> grid = [];
            for (int i = 0; i < postcodes.Count; i++)
            {
                (int, int) cell = (postcodes[i].X / 1000, postcodes[i].Y / 1000);
                if (!grid.TryGetValue(cell, out List<int>? list))
                {
                    grid[cell] = list = [];
                }

                list.Add(i);
            }

            foreach (var road in roads)
            {
                if (road.Name.Length == 0 || road.Place.Length == 0 || road.Region.Length == 0)
                {
                    rejects.Add("gb: road without a name, place or region");
                    continue;
                }

                (string type, string name) = SplitEnglishType(road.Name);
                if (name.Length == 0)
                {
                    rejects.Add("gb: road name is only a street type");
                    continue;
                }

                List<(long D, int I)> near = [];
                int cx = road.X / 1000;
                int cy = road.Y / 1000;
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (!grid.TryGetValue((cx + dx, cy + dy), out List<int>? cell))
                        {
                            continue;
                        }

                        foreach (int i in cell)
                        {
                            Postcode p = postcodes[i];
                            if (road.District.Length > 0 && p.District != road.District)
                            {
                                continue;
                            }

                            long ddx = p.X - road.X;
                            long ddy = p.Y - road.Y;
                            near.Add((ddx * ddx + ddy * ddy, i));
                        }
                    }
                }

                if (near.Count == 0)
                {
                    rejects.Add("gb: no postcode unit of the road's district nearby");
                    continue;
                }

                near.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D) : a.I.CompareTo(b.I));
                string lang = road.Lang1 == "cym" ? "cy" : road.Lang1 == "gla" ? "gd" : "en";
                string altLang = road.Lang2 == "cym" ? "cy" : road.Lang2 == "gla" ? "gd" : string.Empty;
                (string altType, string altName) = altLang.Length > 0 && road.Name2.Length > 0 ? SplitEnglishType(road.Name2) : (string.Empty, string.Empty);
                string admin = road.County.Contains(',', StringComparison.Ordinal) || road.County.Contains('(', StringComparison.Ordinal) ? string.Empty : road.County;
                for (int k = 0; k < Math.Min(2, near.Count); k++)
                {
                    string id = "osgb:" + road.Id + "#" + k;
                    ulong h = Prng.Hash(id, 0x4E554D);
                    yield return new RecB
                    {
                        Id = id,
                        Country = "GB",
                        Region = road.Region,
                        Language = lang,
                        Type = type,
                        Name = name,
                        TypeBefore = false,
                        AltType = altType,
                        AltName = altName,
                        AltLanguage = altLang,
                        Number = DrawNumber(h),
                        Suffix = (h >> 40) % 25 == 0 ? ((char)('A' + (h >> 44) % 4)).ToString() : string.Empty,
                        Locality = road.Place,
                        Admin = admin,
                        PostalCode = postcodes[near[k].I].Text,
                    };
                }
            }
        }
    }

    private static string PostcodeDistrict(string postcode) => postcode.Contains(' ', StringComparison.Ordinal) ? postcode[..postcode.IndexOf(' ', StringComparison.Ordinal)] : string.Empty;

    // The house number drawn for a register that lists streets only: many low numbers, a tail up to the low hundreds.
    internal static string DrawNumber(ulong h)
    {
        double u = (h >> 11) * (1.0 / 9007199254740992.0);
        return (1 + (int)(u * u * 180)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static (string Type, string Name) SplitEnglishType(string road)
    {
        string t = Squeeze(road);
        int sp = t.LastIndexOf(' ');
        if (sp > 0 && UkRoadTypes.Contains(t[(sp + 1)..]) && t.Length - sp > 2)
        {
            return (t[(sp + 1)..], t[..sp]);
        }

        return (string.Empty, t);
    }

    // ---------- Spain: Catastro INSPIRE addresses (CC BY 4.0 compatible licence, attribution to the Dirección General del Catastro) ----------

    private static readonly Dictionary<string, string> Provinces = new(StringComparer.Ordinal)
    {
        ["02"] = "Albacete", ["03"] = "Alicante", ["04"] = "Almería", ["05"] = "Ávila", ["06"] = "Badajoz", ["07"] = "Illes Balears", ["08"] = "Barcelona",
        ["09"] = "Burgos", ["10"] = "Cáceres", ["11"] = "Cádiz", ["12"] = "Castellón", ["13"] = "Ciudad Real", ["14"] = "Córdoba", ["15"] = "A Coruña",
        ["16"] = "Cuenca", ["17"] = "Girona", ["18"] = "Granada", ["19"] = "Guadalajara", ["21"] = "Huelva", ["22"] = "Huesca", ["23"] = "Jaén", ["24"] = "León",
        ["25"] = "Lleida", ["26"] = "La Rioja", ["27"] = "Lugo", ["28"] = "Madrid", ["29"] = "Málaga", ["30"] = "Murcia", ["32"] = "Ourense", ["33"] = "Asturias",
        ["34"] = "Palencia", ["35"] = "Las Palmas", ["36"] = "Pontevedra", ["37"] = "Salamanca", ["38"] = "Santa Cruz de Tenerife", ["39"] = "Cantabria",
        ["40"] = "Segovia", ["41"] = "Sevilla", ["42"] = "Soria", ["43"] = "Tarragona", ["44"] = "Teruel", ["45"] = "Toledo", ["46"] = "Valencia",
        ["47"] = "Valladolid", ["49"] = "Zamora", ["50"] = "Zaragoza", ["51"] = "Ceuta", ["52"] = "Melilla",
        ["01"] = "Álava", ["20"] = "Gipuzkoa", ["31"] = "Navarra", ["48"] = "Bizkaia",
    };

    // Where the street names are written in a co-official language, the share of addresses written in it (the type word follows).
    private static string LocalLanguage(string province) => province switch
    {
        "08" or "17" or "25" or "43" or "07" => "ca",
        "03" or "12" or "46" => "ca",
        "15" or "27" or "32" or "36" => "gl",
        _ => "es",
    };

    private static double LocalShare(string province) => province switch
    {
        "08" or "17" or "25" or "43" or "07" => 0.6,
        "03" or "12" or "46" => 0.35,
        "15" or "27" or "32" or "36" => 0.55,
        _ => 0.0,
    };

    private static IEnumerable<RecB> Spain(string dir, Rejects rejects)
    {
        foreach (string file in System.IO.Directory.GetFiles(dir, "*.zip").OrderBy(f => f, StringComparer.Ordinal))
        {
            using ZipArchive zip = ZipFile.OpenRead(file);
            ZipArchiveEntry? gml = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".gml", StringComparison.Ordinal));
            if (gml is null)
            {
                rejects.Add("es: zip without a GML file");
                continue;
            }

            Dictionary<string, string> streets = new(StringComparer.Ordinal);
            Dictionary<string, string> postcodes = new(StringComparer.Ordinal);
            string municipality = string.Empty;
            List<(string LocalId, string Number, string Street, string Postal)> addresses = [];
            using (Stream s = gml.Open())
            using (XmlReader xr = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = true }))
            {
                xr.MoveToContent();
                xr.Read();
                while (!xr.EOF)
                {
                    if (xr.NodeType != XmlNodeType.Element)
                    {
                        xr.Read();
                        continue;
                    }

                    // The members are wrapped in gml:featureMember; the feature is the first child element.
                    if (xr.LocalName == "featureMember")
                    {
                        xr.Read();
                        continue;
                    }

                    XElement e = (XElement)XNode.ReadFrom(xr);
                    switch (e.Name.LocalName)
                    {
                        case "Address":
                        {
                            string localId = e.Descendants().FirstOrDefault(x => x.Name.LocalName == "localId")?.Value ?? string.Empty;
                            string number = e.Descendants().FirstOrDefault(x => x.Name.LocalName == "LocatorDesignator")?.Elements().FirstOrDefault(x => x.Name.LocalName == "designator")?.Value ?? string.Empty;
                            string street = string.Empty;
                            string postal = string.Empty;
                            foreach (XElement c in e.Elements().Where(x => x.Name.LocalName == "component"))
                            {
                                string href = c.Attributes().FirstOrDefault(a => a.Name.LocalName == "href")?.Value.TrimStart('#') ?? string.Empty;
                                if (href.Contains(".TN.", StringComparison.Ordinal))
                                {
                                    street = href;
                                }
                                else if (href.Contains(".PD.", StringComparison.Ordinal))
                                {
                                    postal = href;
                                }
                            }

                            addresses.Add((localId, number.Trim(), street, postal));
                            break;
                        }

                        case "ThoroughfareName":
                            streets[e.Attributes().First(a => a.Name.LocalName == "id").Value] = e.Descendants().FirstOrDefault(x => x.Name.LocalName == "text")?.Value.Trim() ?? string.Empty;
                            break;
                        case "PostalDescriptor":
                            postcodes[e.Attributes().First(a => a.Name.LocalName == "id").Value] = e.Descendants().FirstOrDefault(x => x.Name.LocalName == "postCode")?.Value.Trim() ?? string.Empty;
                            break;
                        case "AdminUnitName":
                            municipality = e.Descendants().FirstOrDefault(x => x.Name.LocalName == "text")?.Value.Trim() ?? string.Empty;
                            break;
                    }
                }
            }

            string baseName = Path.GetFileNameWithoutExtension(file);
            string code = baseName[(baseName.LastIndexOf('.') + 1)..];
            string province = code.Length >= 2 ? code[..2] : "00";
            string regionName = Provinces.GetValueOrDefault(province, province);
            if (municipality.Length == 0)
            {
                rejects.Add("es: municipality without a name");
                continue;
            }

            foreach ((string localId, string number, string streetRef, string postalRef) in addresses)
            {
                string street = streets.GetValueOrDefault(streetRef, string.Empty);
                string postal = postcodes.GetValueOrDefault(postalRef, string.Empty);
                if (!Regex.IsMatch(number, @"^[0-9]{1,4}$", RegexOptions.CultureInvariant) || number == "0")
                {
                    rejects.Add(number == "0" || number.Length == 0 ? "es: house number zero or empty" : Regex.IsMatch(number, @"^[0-9]+$", RegexOptions.CultureInvariant) ? "es: house number longer than four digits" : "es: house number with letters or signs");
                    continue;
                }

                if (!Regex.IsMatch(postal, @"^[0-9]{5}$", RegexOptions.CultureInvariant))
                {
                    rejects.Add("es: no postcode");
                    continue;
                }

                string[] parts = Squeeze(street).Split(' ', 2);
                if (parts.Length < 2 || street.Contains('(', StringComparison.Ordinal) || LexB.CatastroType(parts[0]) is not { } type)
                {
                    rejects.Add(street.Contains('(', StringComparison.Ordinal) ? "es: street name with a parenthesised article" : "es: street without a known type code " + (parts.Length < 2 ? "(none)" : parts[0]));
                    continue;
                }

                string id = "cat:" + localId;
                bool local = Prng.Hash(id, 0x4C4F43) % 1000 < (ulong)(LocalShare(province) * 1000);
                string lang = local ? LocalLanguage(province) : "es";
                string typeWord = lang switch { "ca" => type.Ca, "gl" => type.Gl, _ => type.Es };
                yield return new RecB
                {
                    Id = id,
                    Country = "ES",
                    Region = regionName,
                    Language = lang,
                    Type = typeWord,
                    Name = LexB.TitleCase(parts[1], lang),
                    TypeBefore = true,
                    Number = number,
                    Locality = LexB.TitleCase(municipality, lang),
                    // File codes that are not INE provinces (53, 54, 55) have no province name to write after the locality.
                    Admin = Provinces.ContainsKey(province) ? regionName : string.Empty,
                    PostalCode = postal,
                };
            }
        }
    }
}
