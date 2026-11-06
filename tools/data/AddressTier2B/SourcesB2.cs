using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using OneContact.Data.AddressTier2A;
using OneContact.Data.AddressUsCa;

namespace OneContact.Data.AddressTier2B;

internal static partial class SourcesB
{
    // ---------- Italy: ANNCSU indirizzario (CC BY 4.0) with the ISTAT list of comuni and GeoNames postal codes ----------

    private readonly record struct Comune(string Name, string AltName, string Region, string Province);

    private static Dictionary<string, Comune> IstatComuni(string dir)
    {
        // ISTAT "Elenco comuni italiani" CSV (windows-1252, read as Latin-1, ';'): the six-digit comune code, the Italian name, the other-language name, region and province sigla.
        Dictionary<string, Comune> map = new(StringComparer.Ordinal);
        string path = System.IO.Directory.GetFiles(dir, "*.csv").Single();
        using StreamReader sr = new(path, Encoding.Latin1);
        DelimitedTable t = new(sr, ';');
        while (t.Read())
        {
            string code = t["Codice Comune formato alfanumerico"];
            if (code.Length == 0)
            {
                continue;
            }

            map[code] = new Comune(Squeeze(t["Denominazione in italiano"]), Squeeze(t["Denominazione altra lingua"]), Squeeze(t["Denominazione Regione"]), t["Sigla automobilistica"].Trim());
        }

        return map;
    }

    private static Dictionary<string, List<string>> ItalianPostcodes(string zipPath)
    {
        // GeoNames IT.txt: country, postal code, place name, region, region code, province, province sigla. A comune is found by its sigla and its name.
        Dictionary<string, List<string>> map = new(StringComparer.Ordinal);
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        using StreamReader sr = new(zip.GetEntry("IT.txt")!.Open(), new UTF8Encoding(false));
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            string[] c = line.Split('\t');
            if (c.Length < 7 || !Regex.IsMatch(c[1], "^[0-9]{5}$", RegexOptions.CultureInvariant))
            {
                continue;
            }

            string key = c[6] + "|" + LexB.PlaceKey(c[2]);
            if (!map.TryGetValue(key, out List<string>? list))
            {
                map[key] = list = [];
            }

            if (!list.Contains(c[1]))
            {
                list.Add(c[1]);
            }
        }

        foreach (List<string> l in map.Values)
        {
            l.Sort(StringComparer.Ordinal);
        }

        return map;
    }

    private static IEnumerable<RecB> Italy(string dir, string istatDir, string geonamesZip, Rejects rejects)
    {
        Dictionary<string, Comune> comuni = IstatComuni(istatDir);
        Dictionary<string, List<string>> cap = ItalianPostcodes(geonamesZip);
        foreach (string file in System.IO.Directory.GetFiles(dir, "INDIR_*.zip").OrderBy(f => f, StringComparer.Ordinal))
        {
            using ZipArchive zip = ZipFile.OpenRead(file);
            ZipArchiveEntry entry = zip.Entries.First(e => e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
            using StreamReader sr = new(entry.Open(), new UTF8Encoding(false), true, 1 << 20);
            DelimitedTable t = new(sr, ';');
            while (t.Read())
            {
                string istat = t["CODICE_ISTAT"];
                string odonimo = Squeeze(t["ODONIMO"]);
                string civico = t["CIVICO"].Trim();
                if (odonimo.Length == 0 || t["METRICO"].Length > 0 || !Regex.IsMatch(civico, "^[0-9]{1,4}$", RegexOptions.CultureInvariant) || civico == "0")
                {
                    rejects.Add("it: no street or no plain civic number");
                    continue;
                }

                if (!comuni.TryGetValue(istat, out Comune comune))
                {
                    rejects.Add("it: comune code not in the ISTAT list");
                    continue;
                }

                if (!cap.TryGetValue(comune.Province + "|" + LexB.PlaceKey(comune.Name), out List<string>? caps))
                {
                    rejects.Add("it: no postal code for the comune");
                    continue;
                }

                string id = "anncsu:" + t["PROGRESSIVO_ACCESSO"];
                (string type, string name) = SplitItalian(odonimo);
                if (name.Length == 0)
                {
                    rejects.Add("it: street is only a type");
                    continue;
                }

                string exponent = t["ESPONENTE"].Trim();
                if (exponent.Length > 6 || !Regex.IsMatch(exponent, "^[A-Za-z]*$", RegexOptions.CultureInvariant))
                {
                    exponent = string.Empty;
                }

                string frazione = Squeeze(t["LOCALITA'"]);
                bool sameAsComune = LexB.PlaceKey(frazione) == LexB.PlaceKey(comune.Name);
                string alt1 = Squeeze(t["DIZIONE_LINGUA1"]);
                (string altType, string altName) = alt1.Length > 0 ? SplitGerman(alt1) : (string.Empty, string.Empty);
                yield return new RecB
                {
                    Id = id,
                    Country = "IT",
                    Region = comune.Region,
                    Language = "it",
                    Type = type,
                    Name = name,
                    TypeBefore = true,
                    AltType = altType,
                    AltName = altName,
                    AltLanguage = altName.Length > 0 ? "de" : string.Empty,
                    Number = civico,
                    Suffix = exponent.ToUpperInvariant(),
                    Locality = comune.Name,
                    LocalityAlt = comune.AltName,
                    Dependent = frazione.Length > 0 && !sameAsComune ? LexB.TitleCase(frazione, "it") : string.Empty,
                    AdminAbbr = comune.Province,
                    PostalCode = caps[(int)(Prng.Hash(id, 0x434150) % (ulong)caps.Count)],
                    PostalSynthetic = caps.Count > 1,
                };
            }
        }
    }

    private static (string Type, string Name) SplitItalian(string upper)
    {
        string title = LexB.TitleCase(upper, "it");
        string[] parts = title.Split(' ', 2);
        string first = parts[0].ToLowerInvariant();
        if (parts.Length == 2 && LexB.IsItalianType(first))
        {
            return (parts[0], parts[1]);
        }

        return (string.Empty, title);
    }

    // South Tyrol's second names are German ("Hauptstraße"-style compounds), kept whole as one name.
    private static (string Type, string Name) SplitGerman(string upper) => (string.Empty, LexB.TitleCase(upper, "de"));

    // ---------- Countries with no open street list: postcode lists plus our own street vocabulary ----------

    private static readonly string AllowedUkLetters = "ABDEFGHJLNPQRSTUWXYZ";
    private static readonly string EircodeAlphabet = "0123456789ACDEFHKNPRTVWXY";

    private sealed record Place(string Code, string Name, string Alt, string Region, string Admin);

    private static List<Place> GeoNamesPlaces(string zipPath, string cc)
    {
        // GeoNames postal-code file: country, code, place, admin1, admin1 code, admin2, admin2 code, admin3, admin3 code, latitude, longitude, accuracy.
        List<Place> list = [];
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        using StreamReader sr = new(zip.GetEntry(cc + ".txt")!.Open(), new UTF8Encoding(false));
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            string[] c = line.Split('\t');
            if (c.Length >= 6)
            {
                list.Add(new Place(c[1], Squeeze(c[2]), Squeeze(c[3]), Squeeze(c[3]), Squeeze(c[5])));
            }
        }

        return list;
    }

    private static List<(string Language, string A, string B)> Streets(string templates, string cc)
    {
        List<(string, string, string)> list = [];
        foreach (string line in File.ReadLines(Path.Combine(templates, "tier2b-streets.tsv")))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] c = line.Split('\t');
            if (c[1] == cc)
            {
                string[] ab = c[3].Split('|');
                list.Add((c[2], ab[0], ab[1]));
            }
        }

        return list;
    }

    private static IEnumerable<RecB> Synthetic(string cc, string geonamesDir, string templates, Rejects rejects)
    {
        string zipPath = Path.Combine(geonamesDir, cc + ".zip");
        List<Place> places = GeoNamesPlaces(zipPath, cc);
        if (cc == "PT")
        {
            places = places.Where(p => Regex.IsMatch(p.Code, @"^[0-9]{4}-[0-9]{3}$", RegexOptions.CultureInvariant)).ToList();
        }
        else if (cc is "JE" or "GG" or "IM")
        {
            places = places.Where(p => Regex.IsMatch(p.Code, @"^[A-Z]{2}[0-9]{1,2}$", RegexOptions.CultureInvariant)).ToList();
        }
        else if (cc == "VA")
        {
            places = [new Place("00120", "Città del Vaticano", string.Empty, "VA", string.Empty)];
        }

        string streetCountry = cc switch { "JE" or "GG" or "IM" => "UKI", _ => cc };
        List<(string Language, string A, string B)> streets = Streets(templates, streetCountry);
        long n = SyntheticCount.GetValueOrDefault(cc, 10_000);
        for (long i = 0; i < n; i++)
        {
            string id = $"{cc.ToLowerInvariant()}:{i}";
            ulong h = Prng.Hash(id, 0x53594E);
            Place p = places[(int)(h % (ulong)places.Count)];
            (string lang, string a, string b) = streets[(int)((h >> 20) % (ulong)streets.Count)];
            bool nameFirst = streetCountry is "IE" or "UKI" || (streetCountry == "MT" && lang == "en");
            string type = nameFirst ? b : a;
            string name = nameFirst ? a : b;
            string number = DrawNumber(Prng.Hash(id, 0x4E554D));
            string postal = p.Code;
            bool synthetic = false;
            Prng r = Prng.For(0x504F53, id);
            switch (cc)
            {
                case "IE":
                    postal = p.Code + " " + string.Concat(Enumerable.Range(0, 4).Select(_ => EircodeAlphabet[r.Int(EircodeAlphabet.Length)]));
                    synthetic = true;
                    break;
                case "MT":
                    postal = p.Code + " " + (1 + r.Int(9)).ToString(System.Globalization.CultureInfo.InvariantCulture) + r.Int(1000).ToString("000", System.Globalization.CultureInfo.InvariantCulture);
                    synthetic = true;
                    break;
                case "JE" or "GG" or "IM":
                    postal = p.Code + " " + (1 + r.Int(9)).ToString(System.Globalization.CultureInfo.InvariantCulture) + AllowedUkLetters[r.Int(AllowedUkLetters.Length)] + AllowedUkLetters[r.Int(AllowedUkLetters.Length)];
                    synthetic = true;
                    break;
                case "SM":
                    postal = p.Code;
                    break;
            }

            string language = cc switch { "MT" => lang, "AD" => "ca", "SM" or "VA" => "it", "IE" or "JE" or "GG" or "IM" => "en", _ => lang };
            if (cc == "AD" && r.Chance(0.12))
            {
                // Andorran addresses are also written in Spanish and French.
                (language, type) = r.Chance(0.6) ? ("es", SpanishOfCatalan(type)) : ("fr", FrenchOfCatalan(type));
            }

            string locality = cc == "MT" && lang == "mt" && p.Alt.Length > 0 ? p.Alt : p.Name;
            yield return new RecB
            {
                Id = id,
                Country = cc,
                Region = cc == "PT" || cc == "IE" ? p.Region : cc,
                Language = language,
                Type = type,
                Name = name,
                TypeBefore = !nameFirst,
                Number = number,
                Locality = locality,
                LocalityAlt = cc == "PT" ? p.Admin : cc == "MT" && p.Alt.Length > 0 ? (lang == "mt" ? p.Name : p.Alt) : string.Empty,
                PostalCode = postal,
                PostalSynthetic = synthetic,
            };
        }

        _ = rejects;
    }

    private static string SpanishOfCatalan(string type) => type switch { "Carrer" => "Calle", "Avinguda" => "Avenida", "Plaça" => "Plaza", "Camí" => "Camino", "Passeig" => "Paseo", "Carretera" => "Carretera", _ => type };

    private static string FrenchOfCatalan(string type) => type switch { "Carrer" => "Rue", "Avinguda" => "Avenue", "Plaça" => "Place", "Camí" => "Chemin", "Passeig" => "Promenade", "Carretera" => "Route", _ => type };
}
