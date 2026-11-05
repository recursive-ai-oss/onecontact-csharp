using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace OneContact.Data.Names;

/// <summary>A list of items drawn with probability proportional to a weight.</summary>
internal sealed class WeightedList<T>
{
    private readonly T[] _items;
    private readonly double[] _cumulative;

    /// <param name="entries">Items with a weight (a count, for the name lists).</param>
    /// <param name="mixCountShapes">Draw half of the time in proportion to the count and half in proportion to its square root, so that common
    /// names dominate (as in real data) while rare names still make up a good share of what the model sees.</param>
    internal WeightedList(List<(T Item, double Weight)> entries, bool mixCountShapes = false)
    {
        _items = new T[entries.Count];
        _cumulative = new double[entries.Count];
        double total = 0;
        double totalRoot = 0;
        foreach ((T _, double w) in entries)
        {
            total += w;
            totalRoot += Math.Sqrt(w);
        }

        double sum = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            _items[i] = entries[i].Item;
            double w = entries[i].Weight;
            sum += mixCountShapes ? (0.5 * w / total) + (0.5 * Math.Sqrt(w) / totalRoot) : w;
            _cumulative[i] = sum;
        }
    }

    internal int Count => _items.Length;

    internal IReadOnlyList<T> Items => _items;

    internal T Pick(Prng r)
    {
        double x = r.Unit() * _cumulative[^1];
        int lo = 0;
        int hi = _cumulative.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_cumulative[mid] > x)
            {
                hi = mid;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return _items[lo];
    }
}

internal readonly record struct GivenName(string Name, char Sex);

/// <summary>
/// The name pools the person renderer draws from. Given names and family names are drawn independently of each other; half of the draws in proportion to the count and half in proportion to its square root, so that common names dominate while the long tail still appears. No record links
/// a given name to a family name, so no string is a real person's name by construction.
/// </summary>
internal sealed class NamePools
{
    /// <summary>Counts are summed over birth years from this year on: names of people who are adults today.</summary>
    internal const int FirstBirthYear = 1940;

    internal WeightedList<GivenName> EnGiven = null!;
    internal WeightedList<string> EnFamily = null!;
    internal WeightedList<string> EsFamily = null!;
    internal WeightedList<GivenName> EsGiven = null!;
    internal WeightedList<GivenName> FrGivenQuebec = null!;
    internal WeightedList<GivenName> FrGivenInsee = null!;
    internal WeightedList<string> FrFamilyInsee = null!;
    internal WeightedList<string> FrFamilyQuebec = null!;
    internal Dictionary<string, string[]> Nicknames = new(StringComparer.Ordinal);

    /// <summary>Every word of every given and family name in the pools, folded: used to keep person-like strings out of the company examples.</summary>
    internal HashSet<string> PersonTokens { get; } = new(StringComparer.Ordinal);

    /// <summary>Entries per pool, for the manifest.</summary>
    internal SortedDictionary<string, int> Sizes { get; } = new(StringComparer.Ordinal);

    private static double Damp(long count) => count;

    internal static NamePools Load(string dataDir, string templateDir)
    {
        NamePools p = new();

        // SSA national baby names: one file per birth year, "Name,Sex,Count".
        Dictionary<string, (long F, long M)> ssa = new(StringComparer.Ordinal);
        using (ZipArchive zip = ZipFile.OpenRead(Path.Combine(dataDir, "ssa", "names.zip")))
        {
            foreach (ZipArchiveEntry e in zip.Entries.OrderBy(x => x.FullName, StringComparer.Ordinal))
            {
                if (!e.Name.StartsWith("yob", StringComparison.Ordinal) || !e.Name.EndsWith(".txt", StringComparison.Ordinal))
                {
                    continue;
                }

                int year = int.Parse(e.Name.AsSpan(3, 4), CultureInfo.InvariantCulture);
                if (year < FirstBirthYear)
                {
                    continue;
                }

                using StreamReader sr = new(e.Open(), Encoding.UTF8);
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    string[] f = line.Split(',');
                    if (f.Length < 3)
                    {
                        continue;
                    }

                    long n = long.Parse(f[2], CultureInfo.InvariantCulture);
                    ssa.TryGetValue(f[0], out (long F, long M) cur);
                    ssa[f[0]] = f[1] == "F" ? (cur.F + n, cur.M) : (cur.F, cur.M + n);
                }
            }
        }

        List<(GivenName, double)> given = [];
        foreach (KeyValuePair<string, (long F, long M)> kv in ssa.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            long total = kv.Value.F + kv.Value.M;
            if (total >= 100 && kv.Key.Length >= 2 && Txt.IsLatinText(kv.Key))
            {
                given.Add((new GivenName(kv.Key, kv.Value.F >= kv.Value.M ? 'F' : 'M'), Damp(total)));
            }
        }

        p.EnGiven = new WeightedList<GivenName>(given, true);
        p.Sizes["ssa-given"] = given.Count;

        // Census 2010 surnames: "name,rank,count,prop100k,cum_prop100k,pctwhite,pctblack,pctapi,pctaian,pct2prace,pcthispanic".
        List<(string, double)> en = [];
        List<(string, double)> es = [];
        using (ZipArchive zip = ZipFile.OpenRead(Path.Combine(dataDir, "census", "names.zip")))
        {
            ZipArchiveEntry entry = zip.GetEntry("Names_2010Census.csv") ?? throw new InvalidDataException("Names_2010Census.csv is missing from the Census archive");
            using StreamReader sr = new(entry.Open(), Encoding.UTF8);
            _ = sr.ReadLine();
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] f = line.Split(',');
                if (f.Length < 11 || f[0] == "ALL OTHER NAMES" || !long.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long count))
                {
                    continue;
                }

                string name = Txt.NameCase(f[0]);
                en.Add((name, Damp(count)));
                if (double.TryParse(f[10], NumberStyles.Float, CultureInfo.InvariantCulture, out double hispanic) && hispanic >= 75)
                {
                    es.Add((name, Damp(count)));
                }
            }
        }

        p.EnFamily = new WeightedList<string>(en, true);
        p.EsFamily = new WeightedList<string>(es, true);
        p.Sizes["census-surname"] = en.Count;
        p.Sizes["census-surname-hispanic>=75"] = es.Count;

        // Spanish-language given names: our own short list.
        List<(GivenName, double)> esGiven = [];
        foreach (string line in File.ReadAllLines(Path.Combine(templateDir, "es-given.tsv")))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] f = line.Split('\t');
            esGiven.Add((new GivenName(f[0], f[1][0]), 1));
        }

        p.EsGiven = new WeightedList<GivenName>(esGiven);
        p.Sizes["es-given-template"] = esGiven.Count;

        // Québec given names (CC BY 4.0): one file per sex, "PRENOMS,A_1980,...", cells are counts, "<5" or ".".
        List<(GivenName, double)> qc = [];
        Dictionary<string, (long F, long M)> qcSum = new(StringComparer.Ordinal);
        foreach ((string file, char sex) in new[] { ("grande_listef_csv.csv", 'F'), ("grande_listeg_csv.csv", 'M') })
        {
            using StreamReader sr = new(Path.Combine(dataDir, "qc", file), Encoding.UTF8);
            _ = sr.ReadLine();
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] f = line.Split(',');
                long total = 0;
                for (int i = 1; i < f.Length; i++)
                {
                    if (long.TryParse(f[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long n))
                    {
                        total += n;
                    }
                }

                if (total < 100 || !IsNameWord(f[0].TrimStart('\uFEFF')))
                {
                    continue;
                }

                string name = Txt.NameCase(f[0]);
                qcSum.TryGetValue(name, out (long F, long M) cur);
                qcSum[name] = sex == 'F' ? (cur.F + total, cur.M) : (cur.F, cur.M + total);
            }
        }

        foreach (KeyValuePair<string, (long F, long M)> kv in qcSum.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            qc.Add((new GivenName(kv.Key, kv.Value.F >= kv.Value.M ? 'F' : 'M'), Damp(kv.Value.F + kv.Value.M)));
        }

        p.FrGivenQuebec = new WeightedList<GivenName>(qc, true);
        p.Sizes["quebec-given"] = qc.Count;

        // INSEE first names: "sexe;preusuel;annais;nombre" (1 = male, 2 = female); rare names are pooled in "_PRENOMS_RARES".
        Dictionary<string, (long Count, char Sex)> inseeSum = new(StringComparer.Ordinal);
        using (ZipArchive zip = ZipFile.OpenRead(Path.Combine(dataDir, "insee", "nat2022_csv.zip")))
        {
            ZipArchiveEntry entry = zip.GetEntry("nat2022.csv") ?? throw new InvalidDataException("nat2022.csv is missing from the INSEE archive");
            using StreamReader sr = new(entry.Open(), Encoding.UTF8);
            _ = sr.ReadLine();
            string? line;
            Dictionary<string, (long M, long F)> bySex = new(StringComparer.Ordinal);
            while ((line = sr.ReadLine()) != null)
            {
                string[] f = line.Split(';');
                if (f.Length < 4 || f[1].StartsWith('_') || !int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int year) || year < FirstBirthYear)
                {
                    continue;
                }

                if (!IsNameWord(f[1]) || !long.TryParse(f[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long n))
                {
                    continue;
                }

                string name = Txt.NameCase(f[1]);
                bySex.TryGetValue(name, out (long M, long F) cur);
                bySex[name] = f[0] == "1" ? (cur.M + n, cur.F) : (cur.M, cur.F + n);
            }

            foreach (KeyValuePair<string, (long M, long F)> kv in bySex)
            {
                inseeSum[kv.Key] = (kv.Value.M + kv.Value.F, kv.Value.F > kv.Value.M ? 'F' : 'M');
            }
        }

        List<(GivenName, double)> insee = [];
        foreach (KeyValuePair<string, (long Count, char Sex)> kv in inseeSum.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (kv.Value.Count >= 100)
            {
                insee.Add((new GivenName(kv.Key, kv.Value.Sex), Damp(kv.Value.Count)));
            }
        }

        p.FrGivenInsee = new WeightedList<GivenName>(insee, true);
        p.Sizes["insee-given"] = insee.Count;

        // INSEE family names (born 1891-1990), tab separated, one count column per 25-year period.
        List<(string, double)> fr = [];
        using (ZipArchive zip = ZipFile.OpenRead(Path.Combine(dataDir, "insee", "noms2008nat_txt.zip")))
        {
            ZipArchiveEntry entry = zip.Entries[0];
            using StreamReader sr = new(entry.Open(), Encoding.UTF8);
            _ = sr.ReadLine();
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] f = line.TrimEnd('\r').Split('\t');
                if (f.Length < 2 || !IsNameWord(f[0], allowSpace: true) || f[0].Contains("XXX", StringComparison.Ordinal) || f[0].StartsWith("AUTRES", StringComparison.Ordinal))
                {
                    continue;
                }

                long total = 0;
                for (int i = 1; i < f.Length; i++)
                {
                    if (long.TryParse(f[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out long n))
                    {
                        total += n;
                    }
                }

                if (total >= 100)
                {
                    fr.Add((Txt.NameCase(f[0]), Damp(total)));
                }
            }
        }

        p.FrFamilyInsee = new WeightedList<string>(fr, true);
        p.Sizes["insee-surname"] = fr.Count;

        List<(string, double)> frQc = [];
        foreach (string line in File.ReadAllLines(Path.Combine(templateDir, "fr-ca-surnames.tsv")))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] f = line.Split('\t');
            frQc.Add((f[0], double.Parse(f[1], CultureInfo.InvariantCulture)));
        }

        p.FrFamilyQuebec = new WeightedList<string>(frQc);
        p.Sizes["quebec-surname-template"] = frQc.Count;

        foreach (string line in File.ReadAllLines(Path.Combine(templateDir, "nicknames.tsv")))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] f = line.Split('\t');
            p.Nicknames[f[0]] = f[1].Split(',');
        }

        void Tokens<T>(WeightedList<T> list, Func<T, string> text)
        {
            foreach (T item in list.Items)
            {
                foreach (string t in text(item).Split(' ', '-', '\''))
                {
                    p.PersonTokens.Add(Txt.Fold(t));
                }
            }
        }

        Tokens(p.EnGiven, g => g.Name);
        Tokens(p.EnFamily, s => s);
        Tokens(p.EsFamily, s => s);
        Tokens(p.EsGiven, g => g.Name);
        Tokens(p.FrGivenQuebec, g => g.Name);
        Tokens(p.FrGivenInsee, g => g.Name);
        Tokens(p.FrFamilyInsee, s => s);
        Tokens(p.FrFamilyQuebec, s => s);
        p.PersonTokens.Remove("");
        p.Sizes["person-tokens"] = p.PersonTokens.Count;

        return p;
    }

    /// <summary>A name made of letters, with single hyphens, apostrophes or spaces inside (no digits, no stray symbols).</summary>
    private static bool IsNameWord(string s, bool allowSpace = false)
    {
        if (s.Length < 2 || s.Length > 30 || !char.IsLetter(s[0]) || !char.IsLetter(s[^1]) || !Txt.IsLatinText(s))
        {
            return false;
        }

        foreach (char c in s)
        {
            if (!char.IsLetter(c) && c is not ('-' or '\'') && !(allowSpace && c == ' '))
            {
                return false;
            }
        }

        return true;
    }
}
