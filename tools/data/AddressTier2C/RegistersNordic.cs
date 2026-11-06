using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using OneContact.Data.AddressTier2A;
using OneContact.Data.AddressUsCa;

namespace OneContact.Data.AddressTier2C;

// Finland and Iceland from their open address registers; Denmark and Sweden from GeoNames postal places and our own street vocabulary,
// because the Danish and Swedish registers are only offered behind a login (provenance/address-tier2-c.md).
internal static partial class Registers
{
    /// <summary>How many synthetic Danish and Swedish records are drawn before sampling; the draw is a pure function of the record number.</summary>
    internal const int SyntheticRecords = 165_000;

    private static string Templates => Path.Combine(AppContext.BaseDirectory, "templates");

    private sealed record NordicPlace(string Code, string Name, string Region);

    /// <summary>GeoNames postal file of one country: postal code, place name and first-level region, the first row of each code.</summary>
    private static Dictionary<string, NordicPlace> NordicPlaces(string dataRoot, string zipCountry, Regex codePattern, Func<string, string> region)
    {
        Dictionary<string, NordicPlace> map = new(StringComparer.Ordinal);
        using ZipArchive zip = ZipFile.OpenRead(Path.Combine(Directory(dataRoot, "nordic-geonames"), zipCountry + ".zip"));
        using StreamReader sr = new(zip.GetEntry(zipCountry + ".txt")!.Open(), new UTF8Encoding(false));
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            // country, code, place, admin1, admin1 code, admin2, admin2 code, admin3, admin3 code, latitude, longitude, accuracy
            string[] c = line.Split('\t');
            if (c.Length >= 6 && codePattern.IsMatch(c[1]) && Squeeze(c[2]).Length > 0 && !map.ContainsKey(c[1]))
            {
                string r = region(c[3]).Trim();
                map[c[1]] = new NordicPlace(c[1], Squeeze(c[2]), r.Length == 0 ? "?" : r);
            }
        }

        return map;
    }

    private static readonly Regex DanishCode = new("^[0-9]{4}$", RegexOptions.CultureInvariant);
    private static readonly Regex SwedishCode = new("^[0-9]{3} [0-9]{2}$", RegexOptions.CultureInvariant);
    private static readonly Regex FinnishCode = new("^[0-9]{5}$", RegexOptions.CultureInvariant);
    private static readonly Regex IcelandicCode = new("^[0-9]{3}$", RegexOptions.CultureInvariant);

    // GeoNames spells Denmark's five regions two ways ("Capital Region" and "Region Hovedstaden").
    private static string DanishRegion(string admin1) => admin1 switch
    {
        "Region Hovedstaden" => "Capital Region",
        "Region Midtjylland" => "Central Jutland",
        "Region Nordjylland" => "North Denmark",
        "Region Syddanmark" => "South Denmark",
        "Region Sjælland" => "Zealand",
        _ => admin1,
    };

    // ---------- Finland: DVV building addresses (CC BY 4.0), 2025-02-03 extract ----------

    private static readonly Regex FinnishNumber = new("^([0-9]{1,4})(?: ?([A-Za-z]))?$", RegexOptions.CultureInvariant);
    private static readonly Regex FinnishRange = new("^[0-9]{1,4}-[0-9]{1,4}$", RegexOptions.CultureInvariant);

    // dBASE III table, UTF-8 (osoiteet_2025.cpg): a 32-byte header, 32-byte field descriptors up to 0x0D, then fixed-width records.
    // Fields: rakennustu(nnus), sijaintiku(nta), sijaintima(akunta), rakennusty, osoitenume(ro), katunimi_f, katunimi_s, katunumero, postinumer, ...
    private static IEnumerable<Dictionary<string, string>> DbfRows(string path)
    {
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        byte[] header = new byte[32];
        fs.ReadExactly(header);
        uint count = BitConverter.ToUInt32(header, 4);
        int headerLength = BitConverter.ToUInt16(header, 8);
        int recordLength = BitConverter.ToUInt16(header, 10);
        List<(string Name, int Offset, int Length)> fields = [];
        byte[] descriptor = new byte[32];
        int offset = 1;
        while (fs.Position < headerLength - 1)
        {
            fs.ReadExactly(descriptor);
            if (descriptor[0] == 0x0D)
            {
                break;
            }

            int end = Array.IndexOf(descriptor, (byte)0, 0, 11);
            string name = Encoding.ASCII.GetString(descriptor, 0, end < 0 ? 11 : end);
            fields.Add((name, offset, descriptor[16]));
            offset += descriptor[16];
        }

        fs.Position = headerLength;
        using BufferedStream bs = new(fs, 1 << 22);
        byte[] record = new byte[recordLength];
        for (uint i = 0; i < count; i++)
        {
            bs.ReadExactly(record);
            if (record[0] == (byte)'*')
            {
                continue;
            }

            Dictionary<string, string> row = new(fields.Count, StringComparer.Ordinal);
            foreach ((string name, int off, int len) in fields)
            {
                row[name] = Encoding.UTF8.GetString(record, off, len).Trim();
            }

            yield return row;
        }
    }

    private static IEnumerable<Addr> Finland(string path, string dataRoot, Rejects rejects)
    {
        Dictionary<string, NordicPlace> fi = NordicPlaces(dataRoot, "FI", FinnishCode, _ => string.Empty);
        Dictionary<string, NordicPlace> ax = NordicPlaces(dataRoot, "AX", FinnishCode, _ => string.Empty);
        foreach (Dictionary<string, string> r in DbfRows(path))
        {
            string finnish = r["katunimi_f"];
            string swedish = r["katunimi_s"];
            if (finnish.Length == 0 && swedish.Length == 0)
            {
                rejects.Add("fi: no street name");
                continue;
            }

            // A bilingual address has both names; the post office name of GeoNames is Finnish, so only Finnish-only and
            // Swedish-only addresses are used, each in its own language.
            bool useFinnish = finnish.Length > 0;
            string number = r["katunumero"];
            string houseNumber;
            string suffix = string.Empty;
            Match m = FinnishNumber.Match(number);
            if (m.Success)
            {
                houseNumber = m.Groups[1].Value;
                suffix = m.Groups[2].Value;
            }
            else if (FinnishRange.IsMatch(number))
            {
                houseNumber = number;
            }
            else
            {
                rejects.Add(number.Length == 0 ? "fi: no house number" : "fi: unusual house number");
                continue;
            }

            string region = r["sijaintima"];
            string code = r["postinumer"];
            Dictionary<string, NordicPlace> places = region == "19" ? ax : fi;
            if (!places.TryGetValue(code, out NordicPlace? place) && !fi.TryGetValue(code, out place))
            {
                rejects.Add("fi: postcode not in the GeoNames list");
                continue;
            }

            yield return new Addr
            {
                Id = "dvv:" + r["rakennustu"] + ":" + r["osoitenume"],
                Country = "FI",
                Region = region.Length == 0 ? "?" : region,
                Language = useFinnish ? "fi" : "sv",
                Street = useFinnish ? finnish : swedish,
                HouseNumber = houseNumber,
                Suffix = suffix,
                Locality = place.Name,
                PostalCode = code,
            };
        }
    }

    // ---------- Iceland: Staðfangaskrá of HMS (published as CC BY) ----------

    private static IEnumerable<Addr> Iceland(string path, string dataRoot, Rejects rejects)
    {
        Dictionary<string, NordicPlace> places = NordicPlaces(dataRoot, "IS", IcelandicCode, _ => string.Empty);
        using StreamReader sr = new(path, new UTF8Encoding(false), true, 1 << 20);
        DelimitedTable t = new(sr, ',');
        while (t.Read())
        {
            string name = t["HEITI_NF"].Trim();
            string number = t["HUSNR"].Trim();
            string letter = t["BOKST"].Trim();
            if (name.Length == 0 || t["VIDSK"].Trim().Length > 0 || t["SERHEITI"].Trim().Length > 0)
            {
                // Plots, land, transformer stations and named institutions are not street addresses.
                rejects.Add("is: no street name, or a plot, land or special-name address");
                continue;
            }

            if (number.Length > 0 && !Regex.IsMatch(number, "^[0-9]{1,4}$", RegexOptions.CultureInvariant))
            {
                rejects.Add("is: unusual house number");
                continue;
            }

            if (letter.Length > 0 && !(number.Length > 0 && Regex.IsMatch(letter, "^[A-Za-zÁÐÉÍÓÚÝÞÆÖáðéíóúýþæö]$", RegexOptions.CultureInvariant)))
            {
                rejects.Add("is: unusual house letter");
                continue;
            }

            if (!places.TryGetValue(t["POSTNR"].Trim(), out NordicPlace? place))
            {
                rejects.Add("is: postcode not in the GeoNames list");
                continue;
            }

            // GeoNames numbers the Reykjavík districts ("Reykjavík - 1"); the post office name is the town.
            string locality = Regex.Replace(place.Name, @" - [0-9]+$", string.Empty, RegexOptions.CultureInvariant);
            yield return new Addr
            {
                Id = "hms:" + t["HNITNUM"].Trim(),
                Country = "IS",
                Region = t["SVFNR"].Trim().Length == 0 ? "?" : t["SVFNR"].Trim(),
                Language = "is",
                Street = name,
                HouseNumber = number,
                Suffix = letter,
                Locality = locality,
                PostalCode = place.Code,
            };
        }
    }

    // ---------- Denmark and Sweden: GeoNames postal places with a street name drawn from our own vocabulary ----------

    private static IEnumerable<Addr> NordicSynthetic(string cc, string dataRoot, Rejects rejects)
    {
        bool dk = cc == "DK";
        Dictionary<string, NordicPlace> map = NordicPlaces(dataRoot, cc, dk ? DanishCode : SwedishCode, dk ? DanishRegion : a => a);
        List<NordicPlace> places = map.Values.OrderBy(p => p.Code, StringComparer.Ordinal).ToList();
        List<string> stems = [];
        List<string> suffixes = [];
        List<string> fixedStreets = [];
        foreach (string line in File.ReadLines(Path.Combine(Templates, "tier2c-streets-nordic.tsv")))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] c = line.Split('\t');
            if (c[1] != cc)
            {
                continue;
            }

            (c[0] switch { "stem" => stems, "suffix" => suffixes, _ => fixedStreets }).Add(c[2]);
        }

        string language = dk ? "da" : "sv";
        for (int i = 0; i < SyntheticRecords; i++)
        {
            string id = $"{cc.ToLowerInvariant()}-synth:{i}";
            Prng r = Prng.For(0x4E4F5244, id);
            NordicPlace place = places[r.Int(places.Count)];
            string street = r.Chance(0.35) ? r.Pick(fixedStreets) : r.Pick(stems) + r.Pick(suffixes);
            int n = r.Chance(0.6) ? 1 + r.Int(40) : 1 + r.Int(200);
            string suffix = r.Chance(0.10) ? ((char)('A' + r.Int(5))).ToString() : string.Empty;
            yield return new Addr
            {
                Id = id,
                Country = cc,
                Region = place.Region,
                Language = language,
                Street = street,
                HouseNumber = n.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Suffix = suffix,
                Locality = place.Name,
                PostalCode = place.Code,
            };
        }

        _ = rejects;
    }

    private static IEnumerable<Addr> NordicRaw(string country, string dataRoot, Rejects rejects) => country switch
    {
        "FI" => Finland(Path.Combine(Directory(dataRoot, "fi-dvv"), "osoiteet_2025.dbf"), dataRoot, rejects),
        "IS" => Iceland(Path.Combine(Directory(dataRoot, "is-stadfangaskra"), "Stadfangaskra.csv"), dataRoot, rejects),
        _ => NordicSynthetic(country, dataRoot, rejects),
    };
}
