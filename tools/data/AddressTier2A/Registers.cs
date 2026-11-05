using System.IO.Compression;
using System.Text;

namespace OneContact.Data.AddressTier2A;

/// <summary>One register address in the shape the renderer reads, whichever register it came from.</summary>
internal sealed record Addr
{
    internal string Id { get; init; } = string.Empty;

    /// <summary>ISO 3166 alpha-2 country the address belongs to.</summary>
    internal string Country { get; init; } = string.Empty;

    /// <summary>The unit sampling is balanced over: département, region, canton, state or Regierungsbezirk.</summary>
    internal string Region { get; init; } = string.Empty;

    /// <summary>Language of <see cref="Street"/> and <see cref="Locality"/> as written (fr, nl, de, it, lb).</summary>
    internal string Language { get; init; } = string.Empty;

    internal string Street { get; init; } = string.Empty;

    /// <summary>The same street in a second official language when the register has one (Brussels, German-speaking Wallonia).</summary>
    internal string StreetAlt { get; init; } = string.Empty;

    internal string AltLanguage { get; init; } = string.Empty;

    internal string HouseNumber { get; init; } = string.Empty;

    /// <summary>Box, apartment or letter-box designation that belongs to the number (Belgian "bus 3").</summary>
    internal string Box { get; init; } = string.Empty;

    internal string Locality { get; init; } = string.Empty;

    internal string LocalityAlt { get; init; } = string.Empty;

    /// <summary>The postal form of the locality when the register keeps one (La Poste's upper-case line 6 name).</summary>
    internal string LocalityPostal { get; init; } = string.Empty;

    /// <summary>Hamlet or lieu-dit the register gives next to the street.</summary>
    internal string Place { get; init; } = string.Empty;

    internal string PostalCode { get; init; } = string.Empty;

    /// <summary>The Swiss canton code when the register gives one.</summary>
    internal string Canton { get; init; } = string.Empty;
}

/// <summary>Where one register lives in the data cache and how its rows become <see cref="Addr"/> records.</summary>
internal static class Registers
{
    /// <summary>Opens the single cache directory whose name starts with the given register prefix.</summary>
    internal static string Directory(string dataRoot, string prefix)
    {
        string[] found = System.IO.Directory.GetDirectories(dataRoot, prefix + "-*");
        return found.Length == 1 ? found[0] : throw new InvalidOperationException($"expected one {prefix}-* directory in {dataRoot}, found {found.Length}");
    }

    internal static IEnumerable<Addr> Read(string country, string dataRoot, Rejects rejects) => Raw(country, dataRoot, rejects).Select(Clean);

    // The registers keep stray blanks inside names ("Châteauneuf sur ", double spaces); runs of white space become one space and the ends are trimmed.
    private static Addr Clean(Addr a) => a with
    {
        Street = Squeeze(a.Street), StreetAlt = Squeeze(a.StreetAlt), Locality = Squeeze(a.Locality), LocalityAlt = Squeeze(a.LocalityAlt),
        LocalityPostal = Squeeze(a.LocalityPostal), Place = Squeeze(a.Place), Box = Squeeze(a.Box),
    };

    private static string Squeeze(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static IEnumerable<Addr> Raw(string country, string dataRoot, Rejects rejects)
    {
        return country switch
        {
            "FR" => France(Path.Combine(Directory(dataRoot, "ban"), "adresses-france.csv.gz"), rejects),
            "BE" => Belgium(Directory(dataRoot, "best"), rejects),
            "LU" => Luxembourg(Path.Combine(Directory(dataRoot, "lu"), "addresses.csv"), rejects),
            "AT" => Austria(Path.Combine(Directory(dataRoot, "at"), "Adresse_Relationale_Tabellen_Stichtagsdaten.zip"), rejects),
            "CH" => Swiss(Path.Combine(Directory(dataRoot, "swisstopo"), "amtliches-gebaeudeadressverzeichnis_ch_2056.csv.zip"), "CH", rejects),
            "LI" => Swiss(Path.Combine(Directory(dataRoot, "swisstopo"), "amtliches-gebaeudeadressverzeichnis_li_2056.csv.zip"), "LI", rejects),
            "DE" => Germany(Path.Combine(Directory(dataRoot, "de-nrw"), "gebref_EPSG25832_ASCII.zip"), rejects),
            "NL" => Bag.Read(Directory(dataRoot, "bag"), rejects),
            "MC" => Monaco.Generate(),
            _ => throw new ArgumentException("unknown country " + country),
        };
    }

    private static StreamReader OpenEntry(ZipArchive zip, string name)
    {
        ZipArchiveEntry entry = zip.GetEntry(name) ?? throw new FileNotFoundException(name);
        return new StreamReader(entry.Open(), new UTF8Encoding(false), true, 1 << 20);
    }

    // BAN CSV: id;id_fantoir;numero;rep;nom_voie;code_postal;code_insee;nom_commune;...;nom_ld;libelle_acheminement;...
    // Licence Ouverte 2.0. Overseas departments 971-976 are kept; the collectivities (975, 977, 978 and 98x) are outside the unit's scope.
    private static IEnumerable<Addr> France(string path, Rejects rejects)
    {
        using FileStream fs = File.OpenRead(path);
        using GZipStream gz = new(fs, CompressionMode.Decompress);
        using StreamReader sr = new(gz, new UTF8Encoding(false), false, 1 << 20);
        DelimitedTable t = new(sr, ';');
        while (t.Read())
        {
            string insee = t["code_insee"];
            string street = t["nom_voie"].Trim();
            string number = t["numero"].Trim();
            string postal = t["code_postal"].Trim();
            if (insee.Length < 5 || street.Length == 0 || postal.Length != 5)
            {
                rejects.Add("fr: missing street, postcode or commune code");
                continue;
            }

            string region = insee[..2];
            if (region == "97")
            {
                region = insee[..3];
                if (region is "975" or "977" or "978")
                {
                    rejects.Add("fr: collectivity outside the départements");
                    continue;
                }
            }
            else if (region == "98")
            {
                rejects.Add("fr: collectivity outside the départements");
                continue;
            }

            // 99999 is the BAN's placeholder for a street that has no numbered addresses.
            if (number.Length == 0 || number == "99999" || number == "0")
            {
                rejects.Add("fr: no house number");
                continue;
            }

            string rep = t["rep"].Trim();
            yield return new Addr
            {
                Id = "ban:" + t["id"],
                Country = "FR",
                Region = region,
                Language = "fr",
                Street = street,
                HouseNumber = rep.Length == 0 ? number : number + "\u0001" + rep,
                Locality = t["nom_commune"].Trim(),
                LocalityPostal = t["libelle_acheminement"].Trim(),
                Place = t["nom_ld"].Trim(),
                PostalCode = postal,
            };
        }
    }

    // BeST openaddress CSVs (the three regional files): street and place names in up to three languages, box_number is the "bus".
    private static IEnumerable<Addr> Belgium(string dir, Rejects rejects)
    {
        foreach (string file in new[] { "openaddress-bebru.zip", "openaddress-bevlg.zip", "openaddress-bewal.zip" })
        {
            using ZipArchive zip = ZipFile.OpenRead(Path.Combine(dir, file));
            ZipArchiveEntry entry = zip.Entries.First(e => e.Name.EndsWith(".csv", StringComparison.Ordinal));
            using StreamReader sr = new(entry.Open(), new UTF8Encoding(false), true, 1 << 20);
            DelimitedTable t = new(sr, ',');
            while (t.Read())
            {
                if (t["status"] != "current")
                {
                    rejects.Add("be: status is not current");
                    continue;
                }

                string region = t["region_code"].Replace("BE-", string.Empty, StringComparison.Ordinal);
                string fr = t["streetname_fr"].Trim();
                string nl = t["streetname_nl"].Trim();
                string de = t["streetname_de"].Trim();
                string number = t["house_number"].Trim();
                string postal = t["postcode"].Trim();
                if (number.Length == 0 || postal.Length != 4 || (fr.Length == 0 && nl.Length == 0 && de.Length == 0))
                {
                    rejects.Add("be: missing street, number or postcode");
                    continue;
                }

                string box = t["box_number"].Trim();
                if (box.Length > 6)
                {
                    box = string.Empty;
                }

                string frPlace = FirstNonEmpty(Before(t["postname_fr"], '/'), t["municipality_name_fr"]);
                string nlPlace = FirstNonEmpty(Before(t["postname_nl"], '/'), t["municipality_name_nl"]);
                string dePlace = FirstNonEmpty(t["municipality_name_de"], frPlace, nlPlace);
                (string lang, string street, string altLang, string alt, string place, string placeAlt) = region switch
                {
                    "BRU" when fr.Length > 0 && nl.Length > 0 => ("fr", fr, "nl", nl, FirstNonEmpty(frPlace, nlPlace), FirstNonEmpty(nlPlace, frPlace)),
                    "VLG" when nl.Length > 0 => ("nl", nl, string.Empty, string.Empty, FirstNonEmpty(nlPlace, frPlace), string.Empty),
                    _ when de.Length > 0 => ("de", de, fr.Length > 0 ? "fr" : string.Empty, fr, dePlace, FirstNonEmpty(frPlace, dePlace)),
                    _ when fr.Length > 0 => ("fr", fr, string.Empty, string.Empty, FirstNonEmpty(frPlace, nlPlace), string.Empty),
                    _ => ("nl", nl, string.Empty, string.Empty, FirstNonEmpty(nlPlace, frPlace), string.Empty),
                };
                if (place.Length == 0)
                {
                    rejects.Add("be: no locality name");
                    continue;
                }

                yield return new Addr
                {
                    Id = "best:" + t["address_id"],
                    Country = "BE",
                    Region = region,
                    Language = lang,
                    Street = street,
                    StreetAlt = alt,
                    AltLanguage = altLang,
                    HouseNumber = number,
                    Box = box,
                    Locality = place,
                    LocalityAlt = placeAlt,
                    PostalCode = postal,
                };
            }
        }
    }

    // Luxembourg BD-L-TC addresses (data.public.lu, CC0): rue;numero;localite;code_postal;...;commune
    private static IEnumerable<Addr> Luxembourg(string path, Rejects rejects)
    {
        using StreamReader sr = new(path, new UTF8Encoding(false), true, 1 << 20);
        DelimitedTable t = new(sr, ';');
        while (t.Read())
        {
            string street = t["rue"].Trim();
            string number = t["numero"].Trim();
            string postal = t["code_postal"].Trim();
            string place = t["localite"].Trim();
            if (street.Length == 0 || number.Length == 0 || postal.Length != 4 || place.Length == 0)
            {
                rejects.Add("lu: missing street, number, locality or postcode");
                continue;
            }

            yield return new Addr
            {
                Id = "lu:" + t["id_geoportail"],
                Country = "LU",
                Region = t["commune"].Trim(),
                Language = "lb",
                Street = street,
                HouseNumber = number,
                Locality = place,
                PostalCode = postal,
            };
        }
    }

    // Austrian address register, relational tables: ADRESSE (PLZ, house number parts, SKZ) joined to STRASSE (name, Zustellort) on SKZ and GKZ.
    private static IEnumerable<Addr> Austria(string path, Rejects rejects)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);
        Dictionary<string, (string Name, string Place)> streets = new(StringComparer.Ordinal);
        using (StreamReader sr = OpenEntry(zip, "STRASSE.csv"))
        {
            DelimitedTable st = new(sr, ';');
            while (st.Read())
            {
                streets[st["GKZ"] + "|" + st["SKZ"]] = (st["STRASSENNAME"].Trim(), st["ZUSTELLORT"].Trim());
            }
        }

        using StreamReader ar = OpenEntry(zip, "ADRESSE.csv");
        DelimitedTable t = new(ar, ';');
        while (t.Read())
        {
            if (!streets.TryGetValue(t["GKZ"] + "|" + t["SKZ"], out (string Name, string Place) street) || street.Name.Length == 0 || street.Place.Length == 0)
            {
                rejects.Add("at: no street or Zustellort");
                continue;
            }

            string number = t["HNR_ADR_ZUSAMMEN"].Trim();
            string postal = t["PLZ"].Trim();
            if (number.Length == 0 || postal.Length != 4 || !char.IsDigit(number[0]))
            {
                rejects.Add("at: no house number or postcode");
                continue;
            }

            string gkz = t["GKZ"];
            yield return new Addr
            {
                Id = "at:" + t["ADRCD"],
                Country = "AT",
                Region = gkz.Length > 0 ? gkz[..1] : "?",
                Language = "de",
                Street = street.Name,
                HouseNumber = number,
                Locality = street.Place,
                PostalCode = postal,
            };
        }
    }

    // swisstopo official directory of building addresses: STN_LABEL;ADR_NUMBER;...;ZIP_LABEL ("4052 Basel");COM_NAME;COM_CANTON;ADR_STATUS
    private static IEnumerable<Addr> Swiss(string path, string country, Rejects rejects)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);
        ZipArchiveEntry entry = zip.Entries.First(e => e.Name.EndsWith(".csv", StringComparison.Ordinal));
        using StreamReader sr = new(entry.Open(), new UTF8Encoding(false), true, 1 << 20);
        DelimitedTable t = new(sr, ';');
        while (t.Read())
        {
            if (t["ADR_STATUS"] != "real")
            {
                rejects.Add(country.ToLowerInvariant() + ": status is not real");
                continue;
            }

            string street = t["STN_LABEL"].Trim();
            string number = t["ADR_NUMBER"].Trim();
            string zip4 = t["ZIP_LABEL"].Trim();
            int space = zip4.IndexOf(' ', StringComparison.Ordinal);
            if (street.Length == 0 || number.Length == 0 || space != 4)
            {
                rejects.Add(country.ToLowerInvariant() + ": missing street, number or ZIP label");
                continue;
            }

            // The register writes a sub-entrance as "31.1"; no postal convention writes a number that way, so those rows are left out.
            if (number.Contains('.', StringComparison.Ordinal))
            {
                rejects.Add(country.ToLowerInvariant() + ": number with a dot");
                continue;
            }

            string place = zip4[(space + 1)..].Trim();
            if (country == "LI" && place.EndsWith(" FL", StringComparison.Ordinal))
            {
                place = place[..^3];
            }

            string canton = t["COM_CANTON"].Trim();
            string lang = country == "LI" ? "de" : Lexicon.GuessSwissLanguage(street, canton);
            yield return new Addr
            {
                Id = "swisstopo:" + t["ADR_EGAID"],
                Country = country,
                Region = country == "LI" ? "LI" : canton,
                Language = lang,
                Street = street,
                HouseNumber = number,
                Locality = place,
                PostalCode = zip4[..4],
                Canton = country == "CH" ? canton : string.Empty,
            };
        }
    }

    // NRW building references (gebref.txt, dl-de/zero-2-0): ...;Gemeinde(10);...;Strassenschluessel(13);Strasse(14);Hausnummer(15);Zusatz(16);...
    // The file has no postcodes. Each municipality gets one synthetic code inside NRW's real ranges (see Lexicon.NrwPostalCode).
    private static IEnumerable<Addr> Germany(string path, Rejects rejects)
    {
        using ZipArchive zip = ZipFile.OpenRead(path);
        using StreamReader sr = OpenEntry(zip, "gebref.txt");
        string? line;
        while ((line = sr.ReadLine()) is not null)
        {
            string[] c = line.Split(';');
            if (c.Length < 17)
            {
                rejects.Add("de: short row");
                continue;
            }

            string street = c[14].Trim();
            string number = c[15].Trim();
            string place = c[10].Trim();
            if (street.Length == 0 || number.Length == 0 || place.Length == 0)
            {
                rejects.Add("de: missing street, number or municipality");
                continue;
            }

            string suffix = c[16].Trim();
            yield return new Addr
            {
                Id = "nrw:" + c[1],
                Country = "DE",
                Region = c[6].Trim(),
                Language = "de",
                Street = Lexicon.ExpandGermanStreet(street),
                HouseNumber = suffix.Length == 0 ? number : number + "\u0001" + suffix,
                Locality = place,
                PostalCode = Lexicon.NrwPostalCode(c[3] + c[5] + c[7] + c[9], place),
            };
        }
    }

    private static string FirstNonEmpty(params string[] items)
    {
        foreach (string s in items)
        {
            if (!string.IsNullOrWhiteSpace(s))
            {
                return s.Trim();
            }
        }

        return string.Empty;
    }

    private static string Before(string s, char c)
    {
        int i = s.IndexOf(c, StringComparison.Ordinal);
        return (i < 0 ? s : s[..i]).Trim();
    }
}
