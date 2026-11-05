using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace OneContact.Data.AddressUsCa;

/// <summary>Why a register row did not become a usable record; the counts go in the manifest.</summary>
internal sealed class Rejects
{
    internal SortedDictionary<string, long> Counts { get; } = new(StringComparer.Ordinal);

    internal void Add(string reason) => Counts[reason] = Counts.GetValueOrDefault(reason) + 1;
}

/// <summary>Reads a comma-separated file with a header row, with quoted fields; fields are reached by column name.</summary>
internal sealed class CsvTable
{
    private readonly TextReader _reader;
    private readonly Dictionary<string, int> _columns = new(StringComparer.Ordinal);
    private readonly List<string> _fields = [];

    internal CsvTable(TextReader reader)
    {
        _reader = reader;
        string? header = reader.ReadLine() ?? throw new InvalidDataException("empty file");
        List<string> names = [];
        Split(header, names);
        for (int i = 0; i < names.Count; i++)
        {
            _columns[names[i].Trim('\uFEFF')] = i;
        }
    }

    internal bool Read()
    {
        string? line = _reader.ReadLine();
        if (line is null)
        {
            return false;
        }

        // a quoted field may hold a line break: keep joining lines while a quote is open.
        while (OpenQuote(line))
        {
            string? more = _reader.ReadLine();
            if (more is null)
            {
                break;
            }

            line += " " + more;
        }

        _fields.Clear();
        Split(line, _fields);
        return true;
    }

    internal string this[string column] => _columns.TryGetValue(column, out int i) && i < _fields.Count ? _fields[i] : string.Empty;

    internal bool Has(string column) => _columns.ContainsKey(column);

    private static bool OpenQuote(string line)
    {
        bool open = false;
        foreach (char c in line)
        {
            if (c == '"')
            {
                open = !open;
            }
        }

        return open;
    }

    private static void Split(string line, List<string> into)
    {
        int i = 0;
        StringBuilder? sb = null;
        while (i <= line.Length)
        {
            if (i < line.Length && line[i] == '"')
            {
                sb ??= new StringBuilder();
                sb.Clear();
                i++;
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    sb.Append(line[i++]);
                }

                into.Add(sb.ToString());
                while (i < line.Length && line[i] != ',')
                {
                    i++;
                }

                i++;
            }
            else
            {
                int comma = line.IndexOf(',', i);
                if (comma < 0)
                {
                    into.Add(line[i..]);
                    return;
                }

                into.Add(line[i..comma]);
                i = comma + 1;
            }
        }
    }
}

/// <summary>Shared checks that keep register oddities out of the labelled data.</summary>
internal static partial class Clean
{
    [GeneratedRegex(@"^[\p{L}0-9][\p{L}0-9 '.\-&/#]{0,34}$")]
    private static partial Regex NameShape();

    [GeneratedRegex(@"^[\p{L}][\p{L} '.\-]{1,29}$")]
    private static partial Regex CityShape();

    [GeneratedRegex(@"^[0-9]{1,6}[A-Za-z]?$")]
    private static partial Regex HouseShape();

    [GeneratedRegex(@"^[A-Za-z]{0,2}[0-9]{1,6}[A-Za-z]{0,2}[0-9]{0,6}$")]
    private static partial Regex HouseShapeWide();

    [GeneratedRegex(@"^[0-9A-Za-z]{1,5}$")]
    private static partial Regex UnitShape();

    [GeneratedRegex(@"^[ABCEGHJ-NPRSTVXY][0-9][ABCEGHJ-NPRSTV-Z][0-9][ABCEGHJ-NPRSTV-Z][0-9]$")]
    private static partial Regex CaPostal();

    internal static string Collapse(string s) => Regex.Replace(s.Trim(), @"\s+", " ");

    internal static bool GoodName(string s) => NameShape().IsMatch(s) && s.Any(char.IsLetterOrDigit);

    internal static bool GoodCity(string s) => CityShape().IsMatch(s);

    internal static bool GoodHouse(string s, bool wide) => !s.TrimStart('0').Equals(string.Empty, StringComparison.Ordinal) && (HouseShape().IsMatch(s) || (wide && HouseShapeWide().IsMatch(s)));

    internal static bool GoodUnit(string s) => UnitShape().IsMatch(s);

    internal static bool GoodCaPostal(string s) => CaPostal().IsMatch(s);

    internal static string TitleLikeUpper(string s) => s.ToUpperInvariant();
}

/// <summary>Turns National Address Database rows into records.</summary>
internal static class NadReader
{
    internal static IEnumerable<Rec> Read(string zipPath, Rules rules, Rejects rejects)
    {
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry entry = zip.Entries.Single(e => e.FullName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        using Stream stream = entry.Open();
        using StreamReader reader = new(stream, new UTF8Encoding(false), true, 1 << 20);
        CsvTable t = new(reader);
        while (t.Read())
        {
            Rec? rec = Build(t, rules, out string reason);
            if (rec is null)
            {
                rejects.Add(reason);
                continue;
            }

            yield return rec;
        }
    }

    private static Rec? Build(CsvTable t, Rules rules, out string reason)
    {
        reason = string.Empty;
        string state = t["State"].Trim().ToUpperInvariant();
        if (!rules.IsUsState(state) || rules.UsStateKind(state) == "military")
        {
            reason = "state-not-listed";
            return null;
        }

        string zip = t["Zip_Code"].Trim();
        if (zip.Length != 5 || !zip.All(char.IsAsciiDigit))
        {
            reason = "zip-not-five-digits";
            return null;
        }

        string pre = t["AddNum_Pre"].Trim();
        string number = t["Add_Number"].Trim();
        string suf = t["AddNum_Suf"].Trim();
        string fraction = string.Empty;
        if (number.Length == 0 || number.Any(c => !char.IsAsciiDigit(c)) || number.TrimStart('0').Length == 0)
        {
            reason = "no-house-number";
            return null;
        }

        string house = number.TrimStart('0');
        if (suf.Length > 0)
        {
            if (Regex.IsMatch(suf, @"^[0-9]/[0-9]$"))
            {
                fraction = suf;
            }
            else if (suf.Length <= 2 && suf.All(char.IsAsciiLetter))
            {
                house += suf;
            }
            else
            {
                reason = "house-suffix-unusual";
                return null;
            }
        }

        if (pre.Length > 0)
        {
            if (pre.Length > 4 || !pre.All(char.IsAsciiLetterOrDigit))
            {
                reason = "house-prefix-unusual";
                return null;
            }

            house = pre + house;
        }

        if (!Clean.GoodHouse(house, wide: true))
        {
            reason = "house-number-shape";
            return null;
        }

        string preDir = t["St_PreDir"].Trim();
        string postDir = t["St_PosDir"].Trim();
        string postType = t["St_PosTyp"].Trim();
        if (t["St_PosMod"].Trim().Length > 0)
        {
            reason = "post-modifier";
            return null;
        }

        if ((preDir.Length > 0 && !rules.TryUsDirection(preDir, out _, out _)) || (postDir.Length > 0 && !rules.TryUsDirection(postDir, out _, out _)))
        {
            reason = "directional-unknown";
            return null;
        }

        string typeStd = string.Empty;
        if (postType.Length > 0)
        {
            if (!rules.TryUsSuffix(postType, out typeStd, out _))
            {
                reason = "street-type-not-in-publication-28";
                return null;
            }
        }

        string name = Clean.Collapse(string.Join(' ', new[] { t["St_PreMod"], t["St_PreTyp"], t["St_PreSep"], t["St_Name"] }.Select(s => s.Trim()).Where(s => s.Length > 0)));
        if (!Clean.GoodName(name))
        {
            reason = "street-name-shape";
            return null;
        }

        // A name that still carries its own suffix ("Main St" with no suffix column) would be labelled wrongly; leave such rows out.
        if (postType.Length == 0)
        {
            int sp = name.LastIndexOf(' ');
            if (sp > 0 && rules.TryUsSuffix(name[(sp + 1)..], out string s1, out _) && name[(sp + 1)..].Length >= 2 && s1.Length > 0 && !IsNumbered(name))
            {
                reason = "type-inside-name";
                return null;
            }
        }

        string city = t["Post_City"].Trim();
        if (city.Length == 0 || city.Equals("Not stated", StringComparison.OrdinalIgnoreCase))
        {
            city = t["Inc_Muni"].Trim();
        }

        if (city.Length == 0 || city.Equals("Not stated", StringComparison.OrdinalIgnoreCase))
        {
            city = t["Uninc_Comm"].Trim();
        }

        city = Clean.Collapse(city);
        if (!Clean.GoodCity(city))
        {
            reason = "city-shape";
            return null;
        }

        string unit = string.Empty;
        string unitDesignator = string.Empty;
        string rawUnit = Clean.Collapse(t["Unit"]);
        if (rawUnit.Length > 0)
        {
            if (!TryUnit(rawUnit, rules, out unitDesignator, out unit))
            {
                reason = "unit-shape";
                return null;
            }
        }

        string uuid = t["UUID"].Trim('{', '}', ' ');
        string id = "nad:" + (uuid.Length > 0 ? uuid : t["OID_"]);
        return new Rec
        {
            Id = id,
            Country = "US",
            Region = state,
            HouseNumber = house,
            Fraction = fraction,
            PreDirectional = preDir,
            StreetName = name,
            StreetType = postType,
            StreetTypeStd = typeStd,
            PostDirectional = postDir,
            Unit = unit,
            UnitDesignator = unitDesignator,
            City = city,
            PostalCode = zip,
            Plus4 = t["Plus_4"].Trim() is { Length: 4 } p4 && p4.All(char.IsAsciiDigit) ? p4 : string.Empty,
            Language = "en",
        };
    }

    // "COUNTY ROAD 12", "STATE ROUTE 9": a number or letter after a type word makes the type part of the name, not a suffix.
    private static bool IsNumbered(string name) => name.Split(' ').Any(w => w.Length > 0 && w.All(char.IsAsciiDigit));

    /// <summary>Splits "APT 4", "#4", "Suite 200" or "4B" into designator and number; false for anything else.</summary>
    internal static bool TryUnit(string raw, Rules rules, out string designator, out string number)
    {
        designator = string.Empty;
        number = raw;
        if (raw.StartsWith('#'))
        {
            designator = "#";
            number = raw[1..].Trim();
        }
        else
        {
            int sp = raw.IndexOf(' ', StringComparison.Ordinal);
            if (sp > 0 && rules.TryUsUnit(raw[..sp].TrimEnd('.'), out string std))
            {
                designator = std;
                number = raw[(sp + 1)..].Trim();
            }
        }

        return Clean.GoodUnit(number) && number.Any(char.IsAsciiLetterOrDigit);
    }
}

/// <summary>Turns Open Database of Addresses rows into records.</summary>
internal static class OdaReader
{
    /// <summary>Providers whose rows are left out (see templates/oda-excluded-providers.tsv); set once at start.</summary>
    internal static HashSet<string> ExcludedProviders { get; set; } = new(StringComparer.Ordinal);

    internal static IEnumerable<Rec> Read(string zipPath, Rules rules, Rejects rejects)
    {
        using ZipArchive zip = ZipFile.OpenRead(zipPath);
        ZipArchiveEntry entry = zip.Entries.Single(e => e.FullName.StartsWith("ODA_", StringComparison.Ordinal) && e.FullName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
        using Stream stream = entry.Open();
        using StreamReader reader = new(stream, new UTF8Encoding(false), true, 1 << 20);
        CsvTable t = new(reader);
        while (t.Read())
        {
            if (ExcludedProviders.Contains(t["provider"].Trim()))
            {
                rejects.Add("provider-excluded");
                continue;
            }

            Rec? rec = Build(t, rules, out string reason);
            if (rec is null)
            {
                rejects.Add(reason);
                continue;
            }

            yield return rec;
        }
    }

    private static Rec? Build(CsvTable t, Rules rules, out string reason)
    {
        reason = string.Empty;
        if (!Rules.CaProvinceByPruid.TryGetValue(t["pruid"].Trim(), out string? province))
        {
            reason = "province-unknown";
            return null;
        }

        string house = t["street_no"].Trim();
        if (!Clean.GoodHouse(house, wide: false))
        {
            reason = "no-house-number";
            return null;
        }

        house = house.TrimStart('0');
        string street = Clean.Collapse(t["street"]);
        if (street.Length == 0 || !Clean.GoodName(street))
        {
            reason = "street-shape";
            return null;
        }

        if (!TrySplitStreet(street, rules, out string name, out string type, out string typeStd, out string dir, out bool before, out string lang))
        {
            reason = "street-unsplittable";
            return null;
        }

        string registerName = Clean.Collapse(t["str_name"]);
        if (registerName.Length > 0 && !Rules.Fold(name).EndsWith(Rules.Fold(registerName), StringComparison.Ordinal))
        {
            reason = "split-disagrees-with-register-name";
            return null;
        }

        string registerType = Clean.Collapse(t["str_type"]);
        if (registerType.Length > 0 && (!rules.TryCaType(registerType, out string rs, out _, out _) || rs != typeStd))
        {
            reason = "split-disagrees-with-register-type";
            return null;
        }

        if (province == "QC" && type.Length == 0 && lang == "en")
        {
            lang = "fr";
        }

        string city = Clean.Collapse(t["csdname"]);
        if (!Clean.GoodCity(city))
        {
            city = Clean.Collapse(t["city"]);
        }

        if (!Clean.GoodCity(city))
        {
            reason = "city-shape";
            return null;
        }

        string unit = Clean.Collapse(t["unit"]);
        string unitNumber = string.Empty;
        if (unit.Length > 0)
        {
            string u = unit.StartsWith('#') ? unit[1..].Trim() : unit;
            if (Clean.GoodUnit(u))
            {
                unitNumber = u;
            }
            else
            {
                reason = "unit-shape";
                return null;
            }
        }

        string postal = t["postal_code"].Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (!Clean.GoodCaPostal(postal))
        {
            postal = string.Empty;
        }

        string id = t["id"].Trim();
        if (id.Length == 0)
        {
            reason = "no-id";
            return null;
        }

        return new Rec
        {
            Id = "oda:" + id,
            Country = "CA",
            Region = province,
            HouseNumber = house,
            PreDirectional = string.Empty,
            StreetName = name,
            StreetType = type,
            StreetTypeStd = typeStd,
            PostDirectional = dir,
            TypeBefore = before,
            Unit = unitNumber,
            City = city,
            PostalCode = postal,
            Language = lang,
        };
    }

    /// <summary>
    /// Splits a written street into name, type and direction with the Canada Post tables: a trailing direction, then a French type
    /// before the name or an English type after it (Canada Post Addressing Guidelines, section 5).
    /// </summary>
    private static bool TrySplitStreet(string street, Rules rules, out string name, out string type, out string typeStd, out string dir, out bool before, out string lang)
    {
        name = type = typeStd = dir = string.Empty;
        before = false;
        lang = "en";
        List<string> words = street.Split(' ').ToList();
        if (words.Count >= 2 && rules.TryCaDirection(words[^1], out _, out string dirLang))
        {
            dir = words[^1];
            lang = dirLang;
            words.RemoveAt(words.Count - 1);
        }

        if (words.Count >= 2 && rules.TryCaType(words[0], out string std, out string tl, out bool b) && b)
        {
            type = words[0];
            typeStd = std;
            before = true;
            lang = tl;
            words.RemoveAt(0);
        }
        else if (words.Count >= 2 && rules.TryCaType(words[^1], out std, out tl, out b) && !b)
        {
            type = words[^1];
            typeStd = std;
            lang = tl;
            words.RemoveAt(words.Count - 1);
        }

        name = string.Join(' ', words);
        return name.Length > 0 && Clean.GoodName(name);
    }
}
