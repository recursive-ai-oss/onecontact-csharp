using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace OneContact.Data.Names;

/// <summary>One name with the sex of the people who carry it ('M', 'F', or '-' when the source does not say) and how many do.</summary>
internal readonly record struct NameCount(string Name, char Sex, double Count);

/// <summary>The names read from one source, with the numbers the manifest and the provenance log report.</summary>
internal sealed class NameSet
{
    internal required string Id { get; init; }

    internal required List<NameCount> Names { get; init; }

    /// <summary>Rows (or cells) read from the file(s) and rows kept after cleaning and the minimum count.</summary>
    internal long RowsRead;

    internal long RowsKept => Names.Count;
}

/// <summary>
/// Reads the open national name-frequency lists of <c>pins/names-tier2.pin</c> into <see cref="NameSet"/>s. Every list is an aggregate count of
/// how many people carry a name (never a record of a person). Each reader names the source it reads; the terms are in
/// <c>tools/data/provenance/names-tier2.md</c>.
/// </summary>
internal static class Tier2Sources
{
    /// <summary>Names written in capitals are cased the usual way; names with digits, symbols or other scripts are dropped.</summary>
    internal static string? Clean(string raw, bool family)
    {
        string s = raw.Trim();
        if (s.Length == 0)
        {
            return null;
        }

        s = string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (s.Length < 2 || s.Length > 30 || !char.IsLetter(s[0]) || !char.IsLetter(s[^1]) || !Txt.IsLatinText(s))
        {
            return null;
        }

        foreach (char c in s)
        {
            if (!char.IsLetter(c) && c is not ('-' or '\'') && !(family && c == ' '))
            {
                return null;
            }
        }

        return Txt.IsAllUpper(s) ? Txt.NameCase(s) : s;
    }

    private sealed class Acc
    {
        private readonly Dictionary<(string, char), double> _sum = new();
        private readonly bool _family;
        internal long Read;

        internal Acc(bool family) => _family = family;

        internal void Add(string raw, char sex, double count)
        {
            Read++;
            string? n = Clean(raw, _family);
            if (n == null || count <= 0)
            {
                return;
            }

            _sum[(n, sex)] = _sum.GetValueOrDefault((n, sex)) + count;
        }

        internal NameSet Done(string id, double min)
        {
            // Deterministic order: by name, then sex.
            List<NameCount> list = _sum.Where(kv => kv.Value >= min)
                .OrderBy(kv => kv.Key.Item1, StringComparer.Ordinal).ThenBy(kv => kv.Key.Item2)
                .Select(kv => new NameCount(kv.Key.Item1, kv.Key.Item2, kv.Value)).ToList();
            return new NameSet { Id = id, Names = list, RowsRead = Read };
        }
    }

    private static double Num(string s) =>
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;

    private static IEnumerable<string[]> Csv(string path, char sep = ',', bool header = true)
    {
        using StreamReader sr = new(path, Encoding.UTF8, true);
        using CsvReader reader = new(sr, sep);
        string?[] row = new string?[8];
        bool[] keep = [true, true, true, true, true, true, true, true];
        bool first = true;
        while (reader.Read(row, keep, out int count))
        {
            if (first)
            {
                first = false;
                if (header)
                {
                    continue;
                }
            }

            string[] r = new string[Math.Min(count, 8)];
            for (int i = 0; i < r.Length; i++)
            {
                r[i] = row[i] ?? "";
            }

            yield return r;
        }
    }

    private static string P(string dir, string rel) => Path.Combine(dir, rel);

    // ---- Poland: PESEL register statistics (dane.gov.pl, CC0). Name, count; one file per sex. ---------------------------------------------

    internal static NameSet PlGiven(string dir, char sex, string file, double min)
    {
        Acc a = new(false);
        foreach (string[] r in Xlsx.Rows(P(dir, file)).Skip(1))
        {
            if (r.Length >= 3)
            {
                a.Add(r[0], sex, Num(r[2]));
            }
        }

        return a.Done("pl-given-" + sex, min);
    }

    internal static NameSet PlFamily(string dir, char sex, string file, double min)
    {
        Acc a = new(true);
        foreach (string[] r in Xlsx.Rows(P(dir, file)).Skip(1))
        {
            if (r.Length >= 2)
            {
                a.Add(r[0], sex, Num(r[1]));
            }
        }

        return a.Done("pl-family-" + sex, min);
    }

    // ---- Finland: DVV Finnish names (CC BY 4.0). Sheets "Miehet ens" / "Naiset ens" (first given name), "Nimet" (surnames). -----------------

    internal static NameSet FiGiven(string dir, double min)
    {
        Acc a = new(false);
        foreach ((string sheet, char sex) in new[] { ("Miehet ens", 'M'), ("Naiset ens", 'F') })
        {
            foreach (string[] r in Xlsx.Rows(P(dir, "fi/etunimitilasto-2026-08-04-dvv.xlsx"), sheet).Skip(1))
            {
                if (r.Length >= 2)
                {
                    a.Add(r[0], sex, Num(r[1]));
                }
            }
        }

        return a.Done("fi-given", min);
    }

    internal static NameSet FiFamily(string dir, double min)
    {
        Acc a = new(true);
        foreach (string[] r in Xlsx.Rows(P(dir, "fi/sukunimitilasto-2026-08-04-dvv.xlsx"), "Nimet").Skip(1))
        {
            if (r.Length >= 2)
            {
                a.Add(r[0], '-', Num(r[1]));
            }
        }

        return a.Done("fi-family", min);
    }

    // ---- Latvia: PMLP "Personu vārdi" (CC0): Vardi, Dzimums, Skaits. Single-word names only (a combination of several names is not one name). --

    internal static NameSet LvGiven(string dir, double min)
    {
        Acc a = new(false);
        foreach (string[] r in Csv(P(dir, "lv/vardi-dz-20260701.csv")))
        {
            if (r.Length >= 3 && !r[0].Contains(' ', StringComparison.Ordinal))
            {
                a.Add(r[0], r[1].StartsWith("S", StringComparison.Ordinal) ? 'F' : 'M', Num(r[2]));
            }
        }

        return a.Done("lv-given", min);
    }

    // ---- Ireland: CSO baby names (CC BY 4.0), long tables: statistic, label, year, year, code, name, unit, value. Sums over the years from FromYear. --

    internal static NameSet IeNames(string dir, string file, char sex, bool family, int fromYear, double min)
    {
        Acc a = new(family);
        foreach (string[] r in Csv(P(dir, file)))
        {
            if (r.Length < 8 || r[1].EndsWith("Rank", StringComparison.Ordinal) || r[7].Length == 0)
            {
                continue;
            }

            if (int.Parse(r[3], CultureInfo.InvariantCulture) >= fromYear)
            {
                a.Add(r[5], sex, Num(r[7]));
            }
        }

        return a.Done(Path.GetFileNameWithoutExtension(file), min);
    }

    // ---- Belgium: Statbel, first names and family names of the population by municipality (CC BY 4.0), zipped, "|" separated. ---------------

    internal static NameSet BeNames(string dir, string file, char sex, bool family, double min)
    {
        Acc a = new(family);
        using ZipArchive zip = ZipFile.OpenRead(P(dir, file));
        foreach (ZipArchiveEntry e in zip.Entries.OrderBy(x => x.FullName, StringComparer.Ordinal))
        {
            using StreamReader sr = new(e.Open(), Encoding.UTF8, true);
            sr.ReadLine();
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] f = line.Split('|');
                if (f.Length >= 5)
                {
                    a.Add(f[3], sex, Num(f[4]));
                }
            }
        }

        return a.Done(Path.GetFileNameWithoutExtension(file), min);
    }

    // ---- Austria: Statistik Austria, first names of newborns by year, district and sex (CC BY 4.0), ";" separated. --------------------------

    internal static NameSet AtGiven(string dir, int fromYear, double min)
    {
        Acc a = new(false);
        foreach (string[] r in Csv(P(dir, "at/OGDEXT_VORNAMEN_1.csv"), ';'))
        {
            if (r.Length >= 5 && int.Parse(r[0], CultureInfo.InvariantCulture) >= fromYear)
            {
                a.Add(r[3], r[2] == "1" ? 'M' : 'F', Num(r[4]));
            }
        }

        return a.Done("at-given", min);
    }

    // ---- Switzerland: FSO (BFS) last names of the permanent resident population by canton (terms: open, source required); Zurich first names (CC0). --

    internal static NameSet ChFamily(string dir, double min)
    {
        Acc a = new(true);
        foreach (string[] r in Csv(P(dir, "ch/bfs-lastnames-by-canton-2024.csv")))
        {
            if (r.Length >= 5)
            {
                a.Add(r[1], '-', Num(r[4]));
            }
        }

        return a.Done("ch-family", min);
    }

    internal static NameSet ChGiven(string dir, int fromBirthYear, double min)
    {
        Acc a = new(false);
        foreach (string[] r in Csv(P(dir, "ch/zurich-BEV370OD3701.csv")))
        {
            // Jahrgang (birth year), Vorname, SexLang, AnzBestWir (people of the city's resident population).
            if (r.Length >= 4 && int.Parse(r[0], CultureInfo.InvariantCulture) >= fromBirthYear)
            {
                a.Add(r[1], r[2].StartsWith("w", StringComparison.Ordinal) ? 'F' : 'M', Num(r[3]));
            }
        }

        return a.Done("ch-given", min);
    }

    // ---- Germany: Rostock registry office, first names of newborns 2025 (CC0): vorname, anzahl, position. ---------------------------------

    internal static NameSet DeGiven(string dir, double min)
    {
        Acc a = new(false);
        foreach ((string file, char sex) in new[] { ("de/rostock-male-2025.csv", 'M'), ("de/rostock-female-2025.csv", 'F') })
        {
            foreach (string[] r in Csv(P(dir, file)))
            {
                if (r.Length >= 2)
                {
                    a.Add(r[0], sex, Num(r[1]));
                }
            }
        }

        return a.Done("de-given", min);
    }

    // ---- Great Britain: ONS baby names, England and Wales 2025 (OGL v3), Table_6 (all names with at least three births): rank, name, count. ---

    internal static NameSet GbGiven(string dir, double min)
    {
        Acc a = new(false);
        foreach ((string file, char sex) in new[] { ("gb/ons-boys-2025.xlsx", 'M'), ("gb/ons-girls-2025.xlsx", 'F') })
        {
            foreach (string[] r in Xlsx.Rows(P(dir, file), "Table_6"))
            {
                if (r.Length >= 3 && int.TryParse(r[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    a.Add(r[1], sex, Num(r[2]));
                }
            }
        }

        return a.Done("gb-given", min);
    }

    /// <summary>National Records of Scotland, all surnames in the registers of births, marriages and deaths, 2025 (OGL v3): year, rank, surname, number.</summary>
    internal static NameSet GbFamily(string dir, double min)
    {
        Acc a = new(true);
        foreach (string[] r in Xlsx.Rows(P(dir, "gb/nrs-most-common-surnames-2025.xlsx"), "All Surnames for 2025"))
        {
            if (r.Length >= 4 && int.TryParse(r[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                a.Add(r[2], '-', Num(r[3]));
            }
        }

        return a.Done("gb-family", min);
    }

    // ---- Australia: NSW Registry of Births, Deaths and Marriages, popular baby names 1952–2025 (CC BY 4.0): rank, name, number, gender, year. --

    internal static NameSet AuGiven(string dir, int fromYear, double min)
    {
        Acc a = new(false);
        foreach (string[] r in Csv(P(dir, "au/nsw-popular-baby-names-1952-2025.csv")))
        {
            if (r.Length >= 5 && int.Parse(r[4], CultureInfo.InvariantCulture) >= fromYear)
            {
                a.Add(r[1], r[3] == "Male" ? 'M' : 'F', Num(r[2]));
            }
        }

        return a.Done("au-given", min);
    }

    // ---- JSON-stat 2 tables from the statistics offices' query APIs (Norway, Sweden, Slovenia). ------------------------------------------

    private static void JsonStat(string path, Action<string, string, double> row)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
        JsonElement root = doc.RootElement;
        string firstDim = root.GetProperty("id")[0].GetString()!;
        JsonElement cat = root.GetProperty("dimension").GetProperty(firstDim).GetProperty("category");
        JsonElement index = cat.GetProperty("index");
        bool hasLabel = cat.TryGetProperty("label", out JsonElement labels);
        JsonElement values = root.GetProperty("value");
        List<string> codes = new(values.GetArrayLength());
        if (index.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement c in index.EnumerateArray())
            {
                codes.Add(c.GetString()!);
            }
        }
        else
        {
            SortedDictionary<int, string> byPos = [];
            foreach (JsonProperty p in index.EnumerateObject())
            {
                byPos[p.Value.GetInt32()] = p.Name;
            }

            codes.AddRange(byPos.Values);
        }

        int i = 0;
        foreach (JsonElement v in values.EnumerateArray())
        {
            if (v.ValueKind == JsonValueKind.Number && i < codes.Count)
            {
                string code = codes[i];
                string label = hasLabel && labels.TryGetProperty(code, out JsonElement l) ? l.GetString()! : code;
                row(code, label, v.GetDouble());
            }

            i++;
        }
    }

    /// <summary>Statistics Norway table 10501 (first names borne by 200 or more people; code prefix 1 girls, 2 boys) and 12891 (last names of 200 or more); CC BY 4.0.</summary>
    internal static NameSet NoGiven(string dir, double min)
    {
        Acc a = new(false);
        JsonStat(P(dir, "no/ssb-10501-2025.json"), (code, label, v) => a.Add(label, code[0] == '1' ? 'F' : 'M', v));
        return a.Done("no-given", min);
    }

    internal static NameSet NoFamily(string dir, double min)
    {
        Acc a = new(true);
        JsonStat(P(dir, "no/ssb-12891-2025.json"), (code, label, v) => a.Add(label, '-', v));
        return a.Done("no-family", min);
    }

    /// <summary>Statistics Sweden: first names (code suffix K women, M men) and last names with at least 10 bearers, 31 December 2020; CC0.</summary>
    internal static NameSet SeGiven(string dir, double min)
    {
        Acc a = new(false);
        JsonStat(P(dir, "se/scb-BE0001FNamn10-2020.json"), (code, label, v) => a.Add(label, code[^1] == 'K' ? 'F' : 'M', v));
        return a.Done("se-given", min);
    }

    internal static NameSet SeFamily(string dir, double min)
    {
        Acc a = new(true);
        JsonStat(P(dir, "se/scb-BE0001ENamn10-2020.json"), (code, label, v) => a.Add(label, '-', v));
        return a.Done("se-family", min);
    }

    /// <summary>SURS (Slovenia): first names (male, female tables) and family names (four alphabetical tables), 1 January 2026; free use with the source named.</summary>
    internal static NameSet SiGiven(string dir, double min)
    {
        Acc a = new(false);
        JsonStat(P(dir, "si/surs-05X1005S-2026.json"), (code, label, v) => a.Add(label, 'M', v));
        JsonStat(P(dir, "si/surs-05X1010S-2026.json"), (code, label, v) => a.Add(label, 'F', v));
        return a.Done("si-given", min);
    }

    internal static NameSet SiFamily(string dir, double min)
    {
        Acc a = new(true);
        foreach (string t in new[] { "05X1015S", "05X1016S", "05X1017S", "05X1018S" })
        {
            JsonStat(P(dir, $"si/surs-{t}-2026.json"), (code, label, v) => a.Add(label, '-', v));
        }

        return a.Done("si-family", min);
    }

    // ---- Denmark: Statistics Denmark's name lists as published by the Agency for Digitisation (CC BY 4.0). Names in order of frequency, no counts. --

    /// <summary>Reads a list ordered by frequency and gives the name at rank r the weight 1000000/r (a Zipf shape; the source publishes no counts).</summary>
    internal static NameSet DkRanked(string dir, string entry, char sex, bool family, string id, int max)
    {
        Acc a = new(family);
        using ZipArchive zip = ZipFile.OpenRead(P(dir, "dk/navne_registreret_i_danmark_2020.zip"));
        // The archive does not flag its entry names as UTF-8, so the names are matched on their ASCII parts: "prefix|token".
        string[] want = entry.Split('|');
        ZipArchiveEntry e = zip.Entries.First(x => x.Name.StartsWith(want[0], StringComparison.Ordinal) && x.Name.Contains(want[1], StringComparison.Ordinal));
        using StreamReader sr = new(e.Open(), Encoding.UTF8, true);
        string? line;
        int rank = 0;
        while ((line = sr.ReadLine()) != null && rank < max)
        {
            string name = line.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            rank++;
            a.Add(name, sex, 1000000.0 / rank);
        }

        return a.Done(id, 0);
    }
}
