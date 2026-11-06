using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using OneContact.Data.AddressTier2A;

namespace OneContact.Data.AddressTier2C;

/// <summary>One register address in the shape the renderer reads, whichever register it came from.</summary>
internal sealed record Addr
{
    internal string Id { get; init; } = string.Empty;

    /// <summary>ISO 3166 alpha-2 country the address belongs to.</summary>
    internal string Country { get; init; } = string.Empty;

    /// <summary>The unit sampling is balanced over: county, district, municipality or the first postcode digit.</summary>
    internal string Region { get; init; } = string.Empty;

    /// <summary>Language of the names as written (no, cs, lv, et).</summary>
    internal string Language { get; init; } = string.Empty;

    /// <summary>Street name as the register writes it; for a street-less village address, the village or farm name the number belongs to.</summary>
    internal string Street { get; init; } = string.Empty;

    /// <summary>House number as written, without letter, entrance or flat ("12", "123/4", "") .</summary>
    internal string HouseNumber { get; init; } = string.Empty;

    /// <summary>Letter or building part that follows the number and belongs to it ("B", "k-2").</summary>
    internal string Suffix { get; init; } = string.Empty;

    /// <summary>Flat or room number when the register gives one.</summary>
    internal string Unit { get; init; } = string.Empty;

    internal string Locality { get; init; } = string.Empty;

    /// <summary>Parish, city district or part of a municipality that is written on its own line or after the locality.</summary>
    internal string Dependent { get; init; } = string.Empty;

    /// <summary>County or municipality that is written after the locality where the country's custom puts one (Latvia, Estonia).</summary>
    internal string Admin { get; init; } = string.Empty;

    internal string PostalCode { get; init; } = string.Empty;
}

/// <summary>Where one register lives in the data cache and how its rows become <see cref="Addr"/> records.</summary>
internal static partial class Registers
{
    internal static string Directory(string dataRoot, string prefix)
    {
        string[] found = System.IO.Directory.GetDirectories(dataRoot, prefix + "-*");
        return found.Length == 1 ? found[0] : throw new InvalidOperationException($"expected one {prefix}-* directory in {dataRoot}, found {found.Length}");
    }

    internal static IEnumerable<Addr> Read(string country, string dataRoot, Rejects rejects) => Raw(country, dataRoot, rejects).Select(Clean);

    private static Addr Clean(Addr a) => a with
    {
        Street = Squeeze(a.Street), Locality = Squeeze(a.Locality), Dependent = Squeeze(a.Dependent), Admin = Squeeze(a.Admin), Unit = Squeeze(a.Unit),
    };

    private static string Squeeze(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static IEnumerable<Addr> Raw(string country, string dataRoot, Rejects rejects) => country switch
    {
        "NO" => Norway(Path.Combine(Directory(dataRoot, "no-matrikkelen"), "Basisdata_0000_Norge_25833_MatrikkelenAdresse_CSV.zip"), rejects),
        "CZ" => Czechia(Path.Combine(Directory(dataRoot, "cz-ruian"), "20261031_OB_ADR_csv.zip"), rejects),
        "LV" => Latvia(Path.Combine(Directory(dataRoot, "lv-varis"), "aw_eka.csv"), rejects),
        "EE" => Estonia(Path.Combine(Directory(dataRoot, "ee-ads"), "AADRESSID_KOMPONENTIDEGA.csv"), rejects),
        _ => throw new ArgumentException("unknown country " + country),
    };

    // Matrikkelen Adresse CSV (Kartverket, CC BY 4.0): only road addresses (vegadresse) have a street name and number; farm and cadastral
    // addresses (matrikkeladresse) are written "124/1" and are left out. The register writes the post office name in capitals.
    private static IEnumerable<Addr> Norway(string path, Rejects rejects)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);
        ZipArchiveEntry entry = zip.Entries.First(e => e.Name.EndsWith(".csv", StringComparison.Ordinal));
        using StreamReader sr = new(entry.Open(), new UTF8Encoding(false), true, 1 << 20);
        DelimitedTable t = new(sr, ';');
        while (t.Read())
        {
            if (t["adressetype"] != "vegadresse")
            {
                rejects.Add("no: not a road address");
                continue;
            }

            string street = t["adressenavn"].Trim();
            string number = t["nummer"].Trim();
            string postal = t["postnummer"].Trim();
            string place = t["poststed"].Trim();
            if (street.Length == 0 || number.Length == 0 || postal.Length != 4 || place.Length == 0)
            {
                rejects.Add("no: missing street, number, postcode or post office");
                continue;
            }

            string municipality = t["kommunenummer"];
            yield return new Addr
            {
                Id = "matrikkelen:" + t["uuidAdresse"],
                Country = "NO",
                Region = municipality.Length >= 2 ? municipality[..2] : "?",
                Language = "no",
                Street = street,
                HouseNumber = number,
                Suffix = t["bokstav"].Trim(),
                Locality = TitleCase(place),
                PostalCode = postal,
            };
        }
    }

    /// <summary>"SANDNES" to "Sandnes", "KRISTIANSAND S" to "Kristiansand S", "NES PÅ ADALEN" is left to the postal-case variant.</summary>
    internal static string TitleCase(string upper)
    {
        StringBuilder sb = new(upper.Length);
        bool start = true;
        foreach (char c in upper)
        {
            sb.Append(start ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
            start = c is ' ' or '-';
        }

        return sb.ToString();
    }

    // RÚIAN address places by municipality (ČÚZK, CC BY 4.0), Windows-1250 text: Název ulice, Číslo domovní, Číslo orientační (+ znak), PSČ.
    // A village address has no street; the part of the municipality plays that role ("Libhošť 1"). Registration numbers (č.ev.) are left out.
    private static IEnumerable<Addr> Czechia(string path, Rejects rejects)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Encoding cp1250 = Encoding.GetEncoding(1250);
        using ZipArchive zip = ZipFile.OpenRead(path);
        foreach (ZipArchiveEntry entry in zip.Entries.Where(e => e.Name.EndsWith(".csv", StringComparison.Ordinal)).OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            using StreamReader sr = new(entry.Open(), cp1250, false, 1 << 16);
            DelimitedTable t = new(sr, ';');
            while (t.Read())
            {
                if (t["Typ SO"] != "č.p.")
                {
                    rejects.Add("cz: registration number (č.ev.) or no building type");
                    continue;
                }

                string postal = t["PSČ"].Trim();
                string house = t["Číslo domovní"].Trim();
                string municipality = t["Název obce"].Trim();
                if (postal.Length != 5 || house.Length == 0 || municipality.Length == 0)
                {
                    rejects.Add("cz: missing postcode, house number or municipality");
                    continue;
                }

                string street = t["Název ulice"].Trim();
                string part = t["Název části obce"].Trim();
                string district = t["Název obvodu Prahy"].Trim();
                string orientation = t["Číslo orientační"].Trim();
                string sign = t["Znak čísla orientačního"].Trim();
                // Prague is addressed by its numbered district ("Praha 1"), Brno and the other divided cities by the town and its part.
                string locality = district.Length > 0 ? district : municipality;
                string number = orientation.Length == 0 ? house : house + "/" + orientation + sign;
                bool streetless = street.Length == 0;
                yield return new Addr
                {
                    Id = "ruian:" + t["Kód ADM"],
                    Country = "CZ",
                    Region = postal[..1],
                    Language = "cs",
                    Street = streetless ? (part.Length > 0 ? part : municipality) : street,
                    HouseNumber = streetless ? house : number,
                    Locality = locality,
                    Dependent = streetless || part == municipality || part == locality ? string.Empty : part,
                    PostalCode = postal,
                };
            }
        }
    }

    [GeneratedRegex(@"^(?<name>.+?) (?<number>\d+[A-Za-z]?)(?: k-(?<block>\d+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex LatvianStreet();

    // VARIS building addresses (VZD, CC BY 4.0). STD is the register's own standard text: "Brīvības iela 10, Rīga, LV-1010", or for a
    // farm "\"Riņņi\", Vecates pag., Valmieras nov., LV-4211". Parish (pag.) goes to the dependent locality, municipality (nov.) to the admin area.
    private static IEnumerable<Addr> Latvia(string path, Rejects rejects)
    {
        using StreamReader sr = new(path, new UTF8Encoding(false), true, 1 << 20);
        DelimitedTable t = new(sr, ',');
        while (t.Read())
        {
            if (t["STATUSS"] != "EKS")
            {
                rejects.Add("lv: not an existing building");
                continue;
            }

            string[] parts = t["STD"].Split(", ", StringSplitOptions.RemoveEmptyEntries);
            string postal = t["ATRIB"].Trim();
            if (parts.Length < 3 || !postal.StartsWith("LV-", StringComparison.Ordinal) || postal.Length != 7 || parts[^1] != postal)
            {
                rejects.Add("lv: address text without a place or LV postcode");
                continue;
            }

            string first = parts[0];
            string name;
            string number = string.Empty;
            string block = string.Empty;
            Match m = LatvianStreet().Match(first);
            if (first.StartsWith('"') && first.EndsWith('"') && first.Length > 2)
            {
                name = first[1..^1];
            }
            else if (m.Success)
            {
                name = m.Groups["name"].Value;
                number = m.Groups["number"].Value;
                block = m.Groups["block"].Value;
            }
            else
            {
                rejects.Add("lv: no house number or house name");
                continue;
            }

            string[] middle = parts[1..^1];
            string parish = middle.FirstOrDefault(p => p.EndsWith(" pag.", StringComparison.Ordinal)) ?? string.Empty;
            string municipality = middle.FirstOrDefault(p => p.EndsWith(" nov.", StringComparison.Ordinal)) ?? string.Empty;
            string settlement = middle.FirstOrDefault(p => !p.EndsWith(" pag.", StringComparison.Ordinal) && !p.EndsWith(" nov.", StringComparison.Ordinal)) ?? string.Empty;
            if (settlement.Length == 0)
            {
                // Rural address without a village name: the parish stands as the locality.
                settlement = parish;
                parish = string.Empty;
            }

            if (settlement.Length == 0)
            {
                rejects.Add("lv: no locality");
                continue;
            }

            yield return new Addr
            {
                Id = "varis:" + t["KODS"],
                Country = "LV",
                Region = municipality.Length > 0 ? municipality : settlement,
                Language = "lv",
                Street = name,
                HouseNumber = number,
                Suffix = block.Length > 0 ? "k-" + block : string.Empty,
                Locality = settlement,
                Dependent = parish,
                Admin = municipality,
                PostalCode = postal[3..],
            };
        }
    }

    [GeneratedRegex(@"^(?<name>.+?) (?<number>\d+[A-Za-z]?)(?:[-/](?<unit>[0-9A-Za-z]+))?$", RegexOptions.CultureInvariant)]
    private static partial Regex EstonianStreet();

    // ADS address versions with components (Maa- ja Ruumiamet, open spatial data licence), tab separated: lahiaadress is the short address
    // ("Kase põik 9-12"), taisaadress the full one ("Jõgeva maakond, Põltsamaa vald, Pisisaare küla, Kase põik 9-12"), sihtnumber the postcode.
    // Only valid versions (olek K, no end date) with a postcode are used. Read line by line: stray quotes make the file unsafe for a CSV reader.
    private static IEnumerable<Addr> Estonia(string path, Rejects rejects)
    {
        using StreamReader sr = new(path, new UTF8Encoding(false), true, 1 << 20);
        string[] header = (sr.ReadLine() ?? string.Empty).Split('\t');
        int Col(string n) => Array.IndexOf(header, n);
        int iId = Col("adr_id"), iFull = Col("taisaadress"), iShort = Col("lahiaadress"), iState = Col("olek"), iEnd = Col("kehtetu"), iPost = Col("sihtnumber");
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            string[] c = line.Split('\t');
            if (c.Length <= iPost)
            {
                rejects.Add("ee: short row");
                continue;
            }

            if (c[iState] != "K" || c[iEnd].Length > 0)
            {
                rejects.Add("ee: not a valid version");
                continue;
            }

            string postal = c[iPost].Trim();
            string shortAddress = c[iShort].Trim();
            string[] parts = c[iFull].Split(", ", StringSplitOptions.RemoveEmptyEntries);
            if (postal.Length != 5 || shortAddress.Length == 0 || parts.Length < 3)
            {
                rejects.Add("ee: missing postcode, short address or components");
                continue;
            }

            string county = parts[0];
            string municipality = parts[1];
            string settlement = parts[^2];
            string dependent = string.Empty;
            if (settlement.EndsWith(" linnaosa", StringComparison.Ordinal) && parts.Length >= 4)
            {
                dependent = settlement;
                settlement = municipality;
            }

            if (settlement.EndsWith(" linn", StringComparison.Ordinal))
            {
                settlement = settlement[..^5];
            }

            string name = shortAddress;
            string number = string.Empty;
            string unit = string.Empty;
            Match m = EstonianStreet().Match(shortAddress);
            if (m.Success)
            {
                name = m.Groups["name"].Value;
                number = m.Groups["number"].Value;
                unit = m.Groups["unit"].Value;
            }

            yield return new Addr
            {
                Id = "ads:" + c[iId],
                Country = "EE",
                Region = county,
                Language = "et",
                Street = name,
                HouseNumber = number,
                Unit = unit,
                Locality = settlement,
                Dependent = dependent,
                Admin = municipality == settlement || municipality == settlement + " linn" ? string.Empty : municipality,
                PostalCode = postal,
            };
        }
    }
}
