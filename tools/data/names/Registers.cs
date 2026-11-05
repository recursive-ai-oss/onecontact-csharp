using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace OneContact.Data.Names;

/// <summary>One written form of a legal form: its text, ELF code and country.</summary>
internal sealed record Form(string Text, string Code, string Country, bool IsAbbreviation);

/// <summary>A company name taken from a register, with what the register says about its legal form.</summary>
internal sealed record CompanyRecord(string Name, string Source, string Jurisdiction, string? Code, string Lang);

/// <summary>Where a legal form was found at the end of a name.</summary>
internal readonly record struct FormMatch(int Start, int End, string Text, List<string> Codes);

/// <summary>
/// The ISO 20275 entity legal form list (GLEIF code list, CC0), used to find the legal form inside a register name: the register gives the code, the
/// list gives the written forms of that code, and the span is where one of them stands at the end of the name. Our rule parser is not involved.
/// </summary>
internal sealed class ElfList
{
    private static readonly HashSet<string> GenericWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "company", "trust", "fund", "association", "foundation", "partnership", "society", "club", "union", "cooperative", "co-operative",
        "corporation sole", "unit trust", "institute", "church", "school", "council", "committee", "authority", "limited partnership",
    };

    private readonly Dictionary<string, List<Form>> _byCode = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Form>> _byCountry = new(StringComparer.Ordinal);

    internal static ElfList Load(string path)
    {
        ElfList list = new();
        using CsvReader csv = new(new StreamReader(path, Encoding.UTF8));
        string?[] row = new string?[16];
        bool[] keep = new bool[16];
        Array.Fill(keep, true);
        csv.Read(row, keep, out _);
        while (csv.Read(row, keep, out int n))
        {
            if (n < 13 || string.IsNullOrEmpty(row[2]))
            {
                continue;
            }

            string code = row[0]!;
            string country = row[2]!;
            bool active = row[12] == "ACTV";
            if (!active)
            {
                continue;
            }

            foreach (string? name in new[] { row[5], row[8] })
            {
                list.AddForm(code, country, name, false);
            }

            foreach (string? abbreviations in new[] { row[9], row[10] })
            {
                if (!string.IsNullOrEmpty(abbreviations))
                {
                    foreach (string a in abbreviations.Split(';'))
                    {
                        list.AddForm(code, country, a, true);
                    }
                }
            }
        }

        foreach (List<Form> forms in list._byCountry.Values)
        {
            forms.Sort(static (a, b) => b.Text.Length != a.Text.Length ? b.Text.Length.CompareTo(a.Text.Length) : string.CompareOrdinal(a.Text, b.Text));
        }

        return list;
    }

    private void AddForm(string code, string country, string? text, bool abbreviation)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length < 2)
        {
            return;
        }

        Form f = new(text, code, country, abbreviation);
        if (!_byCode.TryGetValue(code, out List<Form>? a))
        {
            _byCode[code] = a = [];
        }

        if (!a.Contains(f))
        {
            a.Add(f);
        }

        if (!_byCountry.TryGetValue(country, out List<Form>? b))
        {
            _byCountry[country] = b = [];
        }

        if (!b.Contains(f))
        {
            b.Add(f);
        }
    }

    internal bool HasCode(string code) => _byCode.ContainsKey(code);

    internal IReadOnlyList<Form> FormsOf(string code) => _byCode.TryGetValue(code, out List<Form>? f) ? f : [];

    internal IReadOnlyList<Form> FormsOfCountry(string country) => _byCountry.TryGetValue(country, out List<Form>? f) ? f : [];

    /// <summary>Finds a form at the end of <paramref name="name"/> among <paramref name="candidates"/> (longest first). A final full stop is part of the span.</summary>
    internal static FormMatch? FindTail(string name, IReadOnlyList<Form> candidates, bool skipGeneric)
    {
        int end = name.Length;
        while (end > 0 && name[end - 1] == ' ')
        {
            end--;
        }

        if (end == 0)
        {
            return null;
        }

        // Candidates are sorted by length, longest first, so the first fit is the longest form.
        foreach (Form f in candidates)
        {
            if (skipGeneric && GenericWords.Contains(f.Text))
            {
                continue;
            }

            int len = f.Text.Length;
            int e = end;
            if (!EndsAt(name, e, f.Text) && name[e - 1] == '.' && f.Text[^1] != '.' && EndsAt(name, e - 1, f.Text))
            {
                e = e - 1;
            }
            else if (!EndsAt(name, e, f.Text))
            {
                continue;
            }

            int start = e - len;
            if (start <= 0 || char.IsLetterOrDigit(name[start - 1]))
            {
                continue;
            }

            List<string> codes = [];
            foreach (Form g in candidates)
            {
                if (g.Text.Length == len && string.Equals(g.Text, f.Text, StringComparison.OrdinalIgnoreCase) && !codes.Contains(g.Code))
                {
                    codes.Add(g.Code);
                }
            }

            codes.Sort(StringComparer.Ordinal);
            return new FormMatch(start, end, name[start..end], codes);
        }

        return null;
    }

    private static bool EndsAt(string name, int end, string form) =>
        end >= form.Length && string.Compare(name, end - form.Length, form, 0, form.Length, StringComparison.OrdinalIgnoreCase) == 0;
}

/// <summary>Loaders for the company registers. Each returns records in a fixed order, de-duplicated by folded name.</summary>
internal static class Registers
{
    private static readonly HashSet<string> GleifCountries = new(StringComparer.Ordinal) { "US", "CA", "GB", "FR", "IE" };

    /// <summary>GLEIF Level 1 golden copy (CC0). Keeps legal entities of the countries in scope; sole proprietors (real individuals) are dropped.</summary>
    internal static List<CompanyRecord> LoadGleif(string path, HashSet<string> seen, out SortedDictionary<string, long> stats)
    {
        stats = new SortedDictionary<string, long>(StringComparer.Ordinal);
        List<CompanyRecord> result = [];
        using ZipArchive zip = ZipFile.OpenRead(path);
        using CsvReader csv = new(new StreamReader(zip.Entries[0].Open(), Encoding.UTF8, true, 1 << 20));
        string?[] header = new string?[400];
        bool[] all = new bool[400];
        Array.Fill(all, true);
        csv.Read(header, all, out int columns);
        int Col(string name) => Array.IndexOf(header, name, 0, columns) is int i and >= 0 ? i : throw new InvalidDataException($"GLEIF column {name} is missing");
        int cName = Col("Entity.LegalName");
        int cCountry = Col("Entity.LegalAddress.Country");
        int cJurisdiction = Col("Entity.LegalJurisdiction");
        int cCategory = Col("Entity.EntityCategory");
        int cForm = Col("Entity.LegalForm.EntityLegalFormCode");
        bool[] keep = new bool[columns];
        foreach (int c in new[] { cName, cCountry, cJurisdiction, cCategory, cForm })
        {
            keep[c] = true;
        }

        string?[] row = new string?[columns];
        while (csv.Read(row, keep, out int n))
        {
            stats["rows"] = stats.GetValueOrDefault("rows") + 1;
            if (n < columns)
            {
                continue;
            }

            string? country = row[cCountry];
            if (country == null || !GleifCountries.Contains(country))
            {
                continue;
            }

            if (row[cCategory] == "SOLE_PROPRIETOR")
            {
                stats["sole-proprietors-dropped"] = stats.GetValueOrDefault("sole-proprietors-dropped") + 1;
                continue;
            }

            string name = Clean(row[cName]);
            if (!NameOk(name) || !seen.Add(Txt.Fold(name)))
            {
                continue;
            }

            string jurisdiction = string.IsNullOrEmpty(row[cJurisdiction]) ? country : row[cJurisdiction]!;
            string lang = country == "FR" || jurisdiction == "CA-QC" ? "fr" : "en";
            result.Add(new CompanyRecord(name, "gleif", jurisdiction, string.IsNullOrEmpty(row[cForm]) ? null : row[cForm], lang));
            stats["kept"] = stats.GetValueOrDefault("kept") + 1;
        }

        return result;
    }

    /// <summary>Companies House basic company data (snapshot of the live register). A fixed share of companies is kept, chosen by a hash of the company number.</summary>
    internal static List<CompanyRecord> LoadCompaniesHouse(string path, HashSet<string> seen, int percent, out SortedDictionary<string, long> stats)
    {
        stats = new SortedDictionary<string, long>(StringComparer.Ordinal);
        List<CompanyRecord> result = [];
        using ZipArchive zip = ZipFile.OpenRead(path);
        using CsvReader csv = new(new StreamReader(zip.Entries[0].Open(), Encoding.UTF8, true, 1 << 20));
        string?[] header = new string?[100];
        bool[] all = new bool[100];
        Array.Fill(all, true);
        csv.Read(header, all, out int columns);
        int Col(string name)
        {
            for (int i = 0; i < columns; i++)
            {
                if (header[i]?.Trim() == name)
                {
                    return i;
                }
            }

            throw new InvalidDataException($"Companies House column {name} is missing");
        }

        int cName = Col("CompanyName");
        int cNumber = Col("CompanyNumber");
        int cStatus = Col("CompanyStatus");
        bool[] keep = new bool[columns];
        keep[cName] = keep[cNumber] = keep[cStatus] = true;
        string?[] row = new string?[columns];
        while (csv.Read(row, keep, out int n))
        {
            stats["rows"] = stats.GetValueOrDefault("rows") + 1;
            if (n < columns || row[cStatus] != "Active" || (int)(Prng.Hash(row[cNumber] ?? "", 0x4348UL) % 100UL) >= percent)
            {
                continue;
            }

            string name = Clean(row[cName]);
            if (!NameOk(name) || !seen.Add(Txt.Fold(name)))
            {
                continue;
            }

            result.Add(new CompanyRecord(name, "companies-house", "GB", null, "en"));
            stats["kept"] = stats.GetValueOrDefault("kept") + 1;
        }

        return result;
    }

    /// <summary>SEC EDGAR company names ("NAME:CIK:" lines). Filers that are people are common in this list, so only names that end in a legal form are kept.</summary>
    internal static List<CompanyRecord> LoadEdgar(string path, HashSet<string> seen, ElfList elf, out SortedDictionary<string, long> stats)
    {
        stats = new SortedDictionary<string, long>(StringComparer.Ordinal);
        List<CompanyRecord> result = [];
        IReadOnlyList<Form> us = elf.FormsOfCountry("US");
        foreach (string line in File.ReadLines(path, Encoding.UTF8))
        {
            stats["rows"] = stats.GetValueOrDefault("rows") + 1;
            string s = line.TrimEnd();
            if (s.EndsWith(':'))
            {
                s = s[..^1];
            }

            int colon = s.LastIndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            string name = StripStateSuffix(Clean(s[..colon]));
            if (!NameOk(name) || ElfList.FindTail(name, us, skipGeneric: true) == null)
            {
                stats["no-legal-form-dropped"] = stats.GetValueOrDefault("no-legal-form-dropped") + 1;
                continue;
            }

            if (!seen.Add(Txt.Fold(name)))
            {
                continue;
            }

            result.Add(new CompanyRecord(name, "edgar", "US", null, "en"));
            stats["kept"] = stats.GetValueOrDefault("kept") + 1;
        }

        return result;
    }

    /// <summary>EDGAR appends the state of incorporation to tell namesakes apart ("ACME INC /DE/"); it is not part of the name.</summary>
    private static string StripStateSuffix(string name)
    {
        int n = name.Length;
        if (n > 6 && ((name[n - 1] == '/' && name[n - 4] == '/') || (name[n - 1] == '\\' && name[n - 4] == '\\')) && name[n - 5] == ' ')
        {
            return name[..(n - 5)].TrimEnd();
        }

        return name;
    }

    private static string Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return "";
        }

        StringBuilder sb = new(s.Length);
        bool space = false;
        foreach (char c in s.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }

            if (space && sb.Length > 0)
            {
                sb.Append(' ');
            }

            space = false;
            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool NameOk(string name)
    {
        if (name.Length is < 3 or > 80 || !Txt.IsLatinText(name))
        {
            return false;
        }

        int letters = 0;
        foreach (char c in name)
        {
            if (char.IsLetter(c))
            {
                letters++;
            }
        }

        return letters >= 2 && name.IndexOf('@', StringComparison.Ordinal) < 0 && !name.Contains("http", StringComparison.OrdinalIgnoreCase);
    }
}
