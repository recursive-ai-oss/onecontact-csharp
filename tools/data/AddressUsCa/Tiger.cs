using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace OneContact.Data.AddressUsCa;

/// <summary>
/// Reads the dBASE attribute table of a Census TIGER/Line shapefile (the <c>.dbf</c> inside the county zip): a header, then one
/// fixed-width text record per feature. Only the character columns are needed, so numbers and dates come back as trimmed text.
/// </summary>
internal sealed class DbfTable
{
    private readonly Dictionary<string, (int Offset, int Length)> _columns = new(StringComparer.Ordinal);
    private byte[] _record = [];
    private readonly Stream _stream;
    private readonly int _recordLength;
    private int _remaining;

    internal DbfTable(Stream stream)
    {
        _stream = stream;
        byte[] head = new byte[32];
        ReadExactly(head);
        _remaining = BitConverter.ToInt32(head, 4);
        int headerLength = BitConverter.ToUInt16(head, 8);
        _recordLength = BitConverter.ToUInt16(head, 10);
        int offset = 1; // the first byte of every record is the deletion flag
        int read = 32;
        byte[] field = new byte[32];
        while (read < headerLength)
        {
            ReadExactly(field.AsSpan(0, 1));
            read++;
            if (field[0] == 0x0D)
            {
                break;
            }

            ReadExactly(field.AsSpan(1, 31));
            read += 31;
            string name = Encoding.ASCII.GetString(field, 0, 11).TrimEnd('\0');
            int length = field[16];
            _columns[name] = (offset, length);
            offset += length;
        }

        // skip anything between the field list and the first record
        byte[] rest = new byte[Math.Max(0, headerLength - read)];
        ReadExactly(rest);
        _record = new byte[_recordLength];
    }

    internal bool Has(string column) => _columns.ContainsKey(column);

    internal string this[string column] =>
        _columns.TryGetValue(column, out (int Offset, int Length) c) ? Encoding.UTF8.GetString(_record, c.Offset, c.Length).Trim().Trim('\0') : string.Empty;

    /// <summary>Moves to the next live record; false at the end.</summary>
    internal bool Read()
    {
        while (_remaining > 0)
        {
            _remaining--;
            ReadExactly(_record);
            if (_record[0] != (byte)'*')
            {
                return true;
            }
        }

        return false;
    }

    private void ReadExactly(Span<byte> buffer)
    {
        int done = 0;
        while (done < buffer.Length)
        {
            int n = _stream.Read(buffer[done..]);
            if (n <= 0)
            {
                throw new EndOfStreamException("dbf ended early");
            }

            done += n;
        }
    }
}

/// <summary>A place the Census overlaps with a ZIP Code tabulation area, with the overlapped land area.</summary>
internal readonly record struct ZipPlace(string Name, bool Community, long Area);

/// <summary>
/// ZIP code to place names, from the Census 2020 ZCTA-to-place relationship file. The address-range features carry a ZIP but no
/// city, so a city is a place that overlaps the ZIP, drawn by overlapped land area. The pairing is synthetic: it is a plausible
/// locality for the ZIP, not the postal city of a particular street.
/// </summary>
internal sealed class ZipPlaces
{
    private static readonly string[] Suffixes = ["zona urbana", "comunidad", "city", "town", "village", "borough", "municipality", "CDP", "City"];

    private readonly Dictionary<(string Zip, string State), List<ZipPlace>> _byZip = [];

    internal ZipPlaces(string path)
    {
        foreach (string line in File.ReadLines(path, Encoding.UTF8).Skip(1))
        {
            string[] c = line.Split('|');
            if (c.Length < 18 || c[1].Length != 5 || c[9].Length < 3 || !long.TryParse(c[16], out long area) || area <= 0)
            {
                continue;
            }

            string name = Strip(c[10], out bool community);
            if (name.Length == 0)
            {
                continue;
            }

            (string, string) key = (c[1], c[9][..2]);
            if (!_byZip.TryGetValue(key, out List<ZipPlace>? list))
            {
                _byZip[key] = list = [];
            }

            list.Add(new ZipPlace(name, community, area));
        }

        foreach (List<ZipPlace> list in _byZip.Values)
        {
            list.Sort((a, b) => b.Area != a.Area ? b.Area.CompareTo(a.Area) : string.CompareOrdinal(a.Name, b.Name));
        }
    }

    private static string Strip(string namelsad, out bool community)
    {
        community = namelsad.EndsWith(" comunidad", StringComparison.Ordinal);
        foreach (string s in Suffixes)
        {
            if (namelsad.EndsWith(" " + s, StringComparison.Ordinal))
            {
                return namelsad[..^(s.Length + 1)].Trim();
            }
        }

        return namelsad.Trim();
    }

    /// <summary>The places of a ZIP within one state, largest overlap first; empty when the ZIP is unknown there.</summary>
    internal IReadOnlyList<ZipPlace> For(string zip, string stateFips) =>
        _byZip.TryGetValue((zip, stateFips), out List<ZipPlace>? l) ? l : [];

    /// <summary>Draws a place with probability proportional to its overlapped area, from a hash of the record.</summary>
    internal static ZipPlace? Draw(IEnumerable<ZipPlace> places, string id, ulong salt)
    {
        List<ZipPlace> list = places.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        long total = list.Sum(p => p.Area);
        long at = (long)((Prng.Hash(id, salt) >> 11) % (ulong)total);
        foreach (ZipPlace p in list)
        {
            if (at < p.Area)
            {
                return p;
            }

            at -= p.Area;
        }

        return list[^1];
    }
}

/// <summary>Turns TIGER/Line address-range features into records for the regions the National Address Database does not cover.</summary>
internal static partial class TigerReader
{
    private const ulong CitySalt = 0x43495459303031UL;
    private const ulong UrbSalt = 0x55524230303031UL;
    private const ulong NumberSalt = 0x4E554D42303031UL;
    private const ulong StyleSalt = 0x5354594C303031UL;

    // State and territory FIPS codes (the first two digits of a county file name) to postal codes.
    private static readonly Dictionary<string, string> RegionByFips = new(StringComparer.Ordinal)
    {
        ["15"] = "HI", ["26"] = "MI", ["28"] = "MS", ["32"] = "NV", ["33"] = "NH",
        ["60"] = "AS", ["66"] = "GU", ["69"] = "MP", ["72"] = "PR", ["78"] = "VI",
    };

    // American Samoa and the Northern Mariana Islands have no address ranges in the files; their road names come from the edges file
    // and the ZIP follows the island (USPS: 96799 for all of American Samoa, 96950 Saipan, 96951 Rota, 96952 Tinian).
    private static readonly Dictionary<string, string> ZipByCounty = new(StringComparer.Ordinal)
    {
        ["60010"] = "96799", ["60020"] = "96799", ["60030"] = "96799", ["60040"] = "96799", ["60050"] = "96799",
        ["69100"] = "96951", ["69110"] = "96950", ["69120"] = "96952",
    };

    // Road classes that are streets people live on: primary and secondary roads, local roads, private roads.
    private static readonly HashSet<string> StreetClasses = new(StringComparer.Ordinal) { "S1100", "S1200", "S1400", "S1740" };

    // Spanish words the Census abbreviates at the start of a Puerto Rico street name, and what we write for them.
    // USPS Publication 28 appendix I section 51.1: the prefix stays in the street name ("585 AVE FD ROOSEVELT").
    private static readonly Dictionary<string, string[]> SpanishPrefix = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cll"] = ["Calle"], ["Call"] = ["Calle"], ["Calle"] = ["Calle"],
        ["Cam"] = ["Camino"], ["Camino"] = ["Camino"],
        ["Pso"] = ["Paseo"], ["Paseo"] = ["Paseo"],
        ["Plz"] = ["Plaza"], ["Plaza"] = ["Plaza"],
        ["Ave"] = ["Ave", "Avenida"], ["Avenida"] = ["Ave", "Avenida"],
        ["Carr"] = ["Carr", "Carretera"], ["Carretera"] = ["Carr", "Carretera"],
        ["Via"] = ["Via"], ["Callejón"] = ["Callejón"], ["Callejon"] = ["Callejón"],
        ["Expreso"] = ["Expreso"], ["Arterial"] = ["Arterial"], ["Blvd"] = ["Blvd"], ["Parque"] = ["Parque"],
        ["Vereda"] = ["Vereda"], ["Pasaje"] = ["Pasaje"],
    };

    [GeneratedRegex(@"^([A-Za-z]{0,2})([0-9]{1,6})([A-Za-z]?)$")]
    private static partial Regex HouseRange();

    internal static IEnumerable<Rec> Read(string directory, Rules rules, Rejects rejects, IReadOnlyDictionary<string, int> perRange)
    {
        ZipPlaces places = new(Path.Combine(directory, "zcta-place.txt"));
        foreach (string file in Directory.GetFiles(directory, "tl_*_addrfeat.zip").Concat(Directory.GetFiles(directory, "tl_*_edges.zip")).OrderBy(p => p, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            bool edges = name.EndsWith("_edges.zip", StringComparison.Ordinal);
            string county = name.Split('_')[2];
            if (!RegionByFips.TryGetValue(county[..2], out string? region))
            {
                continue;
            }

            int draws = perRange.TryGetValue(region, out int d) ? d : perRange.GetValueOrDefault("*", 3);
            using ZipArchive zip = ZipFile.OpenRead(file);
            ZipArchiveEntry entry = zip.Entries.Single(e => e.FullName.EndsWith(".dbf", StringComparison.OrdinalIgnoreCase));
            using Stream stream = entry.Open();
            DbfTable t = new(stream);
            while (t.Read())
            {
                foreach (Rec rec in edges ? FromEdge(t, county, region, draws, rules, places, rejects) : FromRange(t, county, region, draws, rules, places, rejects))
                {
                    yield return rec;
                }
            }
        }
    }

    private static IEnumerable<Rec> FromRange(DbfTable t, string county, string region, int draws, Rules rules, ZipPlaces places, Rejects rejects)
    {
        if (!StreetClasses.Contains(t["EDGE_MTFCC"]))
        {
            rejects.Add("not-a-street-class");
            yield break;
        }

        string full = Clean.Collapse(t["FULLNAME"]);
        if (full.Length == 0)
        {
            rejects.Add("no-street-name");
            yield break;
        }

        foreach (char side in new[] { 'L', 'R' })
        {
            string from = t[side + "FROMHN"];
            string to = t[side + "TOHN"];
            string zip = t["ZIP" + side];
            if (from.Length == 0 || to.Length == 0)
            {
                continue;
            }

            string rangeId = t["ARID" + side];
            if (rangeId.Length == 0)
            {
                rangeId = t["TLID"] + side;
            }

            if (zip.Length != 5 || !zip.All(char.IsAsciiDigit))
            {
                rejects.Add("zip-not-five-digits");
                continue;
            }

            string parity = t["PARITY" + side];
            for (int i = 0; i < draws; i++)
            {
                string id = $"tiger:{region}:{rangeId}:{i}";
                if (!TryHouseNumber(from, to, parity, id, out string house))
                {
                    rejects.Add("house-range-unusable");
                    break;
                }

                Rec? rec = Build(id, region, county[..2], house, full, zip, rules, places, out string reason);
                if (rec is null)
                {
                    rejects.Add(reason);
                    continue;
                }

                yield return rec;
            }
        }
    }

    // American Samoa and the Northern Mariana Islands: the files hold road names but no address ranges, so the house number is
    // drawn at random. Such records are marked by their id prefix and counted separately in the manifest.
    private static IEnumerable<Rec> FromEdge(DbfTable t, string county, string region, int draws, Rules rules, ZipPlaces places, Rejects rejects)
    {
        if (!ZipByCounty.TryGetValue(county, out string? zip) || !StreetClasses.Contains(t["MTFCC"]))
        {
            rejects.Add("edge-not-used");
            yield break;
        }

        string full = Clean.Collapse(t["FULLNAME"]);
        if (full.Length == 0)
        {
            rejects.Add("no-street-name");
            yield break;
        }

        for (int i = 0; i < draws; i++)
        {
            string id = $"tigeredge:{region}:{t["TLID"]}:{i}";
            string house = (1 + (int)(Prng.Hash(id, NumberSalt) % 998)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Rec? rec = Build(id, region, county[..2], house, full, zip, rules, places, out string reason);
            if (rec is null)
            {
                rejects.Add(reason);
                continue;
            }

            yield return rec;
        }
    }

    /// <summary>
    /// A house number inside a range: both ends share their letter prefix and suffix (Puerto Rico writes <c>H00</c> to <c>H98</c>), the
    /// parity column says even (E), odd (O) or both (B); the number is drawn from a hash of the record id.
    /// </summary>
    private static bool TryHouseNumber(string from, string to, string parity, string id, out string house)
    {
        house = string.Empty;
        Match a = HouseRange().Match(from);
        Match b = HouseRange().Match(to);
        if (!a.Success || !b.Success || !string.Equals(a.Groups[1].Value, b.Groups[1].Value, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(a.Groups[3].Value, b.Groups[3].Value, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        long x = long.Parse(a.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        long y = long.Parse(b.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        long lo = Math.Max(1, Math.Min(x, y));
        long hi = Math.Max(x, y);
        if (hi < lo)
        {
            return false;
        }

        long n = lo + (long)(Prng.Hash(id, NumberSalt) % (ulong)(hi - lo + 1));
        bool wantEven = parity == "E";
        bool wantOdd = parity == "O";
        if ((wantEven && n % 2 != 0) || (wantOdd && n % 2 == 0))
        {
            n = n + 1 <= hi ? n + 1 : n - 1;
            if (n < lo || (wantEven && n % 2 != 0) || (wantOdd && n % 2 == 0))
            {
                return false;
            }
        }

        house = a.Groups[1].Value + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + a.Groups[3].Value;
        return Clean.GoodHouse(house, wide: true);
    }

    private static Rec? Build(string id, string region, string fips, string house, string full, string zip, Rules rules, ZipPlaces places, out string reason)
    {
        reason = string.Empty;
        if (!TryStreet(full, region == "PR", id, rules, out string preDir, out string name, out string type, out string typeStd, out string postDir, out reason))
        {
            return null;
        }

        IReadOnlyList<ZipPlace> overlapping = places.For(zip, fips);
        if (overlapping.Count == 0)
        {
            reason = "zip-without-place";
            return null;
        }

        string urbanization = string.Empty;
        ZipPlace? city;
        if (region == "PR")
        {
            // A Puerto Rico "city" is the municipio's urban zone; the communities inside the ZIP stand in for urbanization names.
            List<ZipPlace> urban = overlapping.Where(p => !p.Community).ToList();
            List<ZipPlace> community = overlapping.Where(p => p.Community).ToList();
            city = ZipPlaces.Draw(urban.Count > 0 ? urban : community, id, CitySalt);
            ZipPlace? urb = ZipPlaces.Draw(community, id, UrbSalt);
            if (urb is { } u && Clean.GoodCity(u.Name))
            {
                urbanization = u.Name;
            }
        }
        else
        {
            city = ZipPlaces.Draw(overlapping, id, CitySalt);
        }

        if (city is not { } c || !Clean.GoodCity(c.Name))
        {
            reason = "city-shape";
            return null;
        }

        return new Rec
        {
            Id = id,
            Country = "US",
            Region = region,
            HouseNumber = house,
            PreDirectional = preDir,
            StreetName = name,
            StreetType = type,
            StreetTypeStd = typeStd,
            PostDirectional = postDir,
            City = c.Name,
            Urbanization = urbanization,
            PostalCode = zip,
            Language = region == "PR" ? "es" : "en",
        };
    }

    /// <summary>
    /// Splits the Census street name ("N Main St", "Cll Jupiter", "Pr- 8177") into the parts the templates render. The name is the
    /// Census spelling with its type and directional abbreviations; the Publication 28 tables recognise them.
    /// </summary>
    private static bool TryStreet(string full, bool puertoRico, string id, Rules rules, out string preDir, out string name, out string type, out string typeStd, out string postDir, out string reason)
    {
        preDir = name = type = typeStd = postDir = string.Empty;
        reason = string.Empty;
        List<string> words = full.Split(' ').ToList();
        if (puertoRico && words.Count >= 1 && (words[0].StartsWith("Pr-", StringComparison.OrdinalIgnoreCase) || words[0].Equals("Pr", StringComparison.OrdinalIgnoreCase)))
        {
            // "Pr- 8177": a state route. Written PR-8177 or CARR 8177.
            string number = words[0].Length > 3 ? words[0][3..] : (words.Count > 1 ? words[1] : string.Empty);
            if (number.Length == 0 || !number.All(char.IsAsciiDigit))
            {
                reason = "route-name-unusual";
                return false;
            }

            name = Prng.Hash(id, StyleSalt) % 2 == 0 ? "PR-" + number : "Carr " + number;
            return true;
        }

        if (puertoRico && SpanishPrefix.TryGetValue(words[0], out string[]? spellings) && words.Count >= 2)
        {
            if (words.Count >= 3 && rules.TryUsDirection(words[^1], out _, out _))
            {
                postDir = words[^1];
                words.RemoveAt(words.Count - 1);
            }

            words[0] = spellings[(int)(Prng.Hash(id, StyleSalt) % (ulong)spellings.Length)];
            name = string.Join(' ', words);
            if (!Clean.GoodName(name))
            {
                reason = "street-name-shape";
                return false;
            }

            return true;
        }

        if (words.Count >= 3 && rules.TryUsDirection(words[^1], out _, out _) && rules.TryUsSuffix(words[^2], out _, out _))
        {
            postDir = words[^1];
            words.RemoveAt(words.Count - 1);
        }

        if (words.Count >= 2 && rules.TryUsSuffix(words[^1], out typeStd, out _) && typeStd.Length > 0 && !words[^1].All(char.IsAsciiDigit))
        {
            type = words[^1];
            words.RemoveAt(words.Count - 1);
        }
        else if (postDir.Length > 0)
        {
            reason = "direction-without-type";
            return false;
        }

        if (words.Count >= 2 && rules.TryUsDirection(words[0], out _, out _))
        {
            preDir = words[0];
            words.RemoveAt(0);
        }

        name = string.Join(' ', words);
        if (!Clean.GoodName(name))
        {
            reason = "street-name-shape";
            return false;
        }

        return true;
    }
}
