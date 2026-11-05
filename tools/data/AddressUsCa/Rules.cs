using System.Text;

namespace OneContact.Data.AddressUsCa;

/// <summary>
/// The authority tables the templates spell words from: USPS Publication 28 (street suffixes, directionals, secondary unit
/// designators, states and territories) and the Canada Post Addressing Guidelines (street types, directions, unit designators,
/// delivery words), read from the library's own rule files so the training spellings and the rule parser agree.
/// </summary>
internal sealed class Rules
{
    private readonly Dictionary<string, (string Std, string Primary)> _usSuffixVariants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _usSuffixSpellings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Abbr, string Full)> _usDirections = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _usUnitStd = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _usUnitSpellings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _usStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _usStateNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Std, string Lang, bool Before)> _caTypeVariants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<string>> _caTypeSpellings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Abbr, string Lang)> _caDirections = new(StringComparer.Ordinal);

    internal Rules(string rulesDirectory)
    {
        foreach (string[] r in Read(rulesDirectory, "us-street-suffixes.tsv"))
        {
            _usSuffixVariants[r[0]] = (r[1], r[2]);
            if (!_usSuffixSpellings.TryGetValue(r[1], out List<string>? list))
            {
                _usSuffixSpellings[r[1]] = list = [];
            }

            list.Add(r[0]);
        }

        foreach (string[] r in Read(rulesDirectory, "us-directionals.tsv"))
        {
            _usDirections[r[0]] = (r[1], r[2]);
        }

        foreach (string[] r in Read(rulesDirectory, "us-unit-designators.tsv"))
        {
            _usUnitStd[r[0]] = r[1];
            if (!_usUnitSpellings.TryGetValue(r[1], out List<string>? spellings))
            {
                _usUnitSpellings[r[1]] = spellings = [];
            }

            spellings.Add(r[0]);
        }

        foreach (string[] r in Read(rulesDirectory, "us-states.tsv"))
        {
            _usStates[r[1]] = r[2];
            _usStateNames[r[1]] = r[0];
        }

        foreach (string[] r in Read(rulesDirectory, "ca-street-types.tsv"))
        {
            _caTypeVariants[r[0]] = (r[1], r[2], r[3] == "before");
            string key = r[2] + ":" + r[1];
            if (!_caTypeSpellings.TryGetValue(key, out List<string>? list))
            {
                _caTypeSpellings[key] = list = [];
            }

            list.Add(r[0]);
        }

        foreach (string[] r in Read(rulesDirectory, "ca-directions.tsv"))
        {
            _caDirections[r[0]] = (r[1], r[2]);
        }
    }

    internal bool IsUsState(string code) => _usStates.ContainsKey(code);

    internal string UsStateKind(string code) => _usStates.TryGetValue(code, out string? k) ? k : string.Empty;

    internal string UsStateName(string code) => _usStateNames.TryGetValue(code, out string? n) ? n : code;

    /// <summary>Looks up a written street suffix; false when Publication 28 does not list it.</summary>
    internal bool TryUsSuffix(string written, out string std, out string primary)
    {
        if (_usSuffixVariants.TryGetValue(written.ToUpperInvariant(), out (string Std, string Primary) v))
        {
            std = v.Std;
            primary = v.Primary;
            return true;
        }

        std = primary = string.Empty;
        return false;
    }

    /// <summary>Every spelling Publication 28 lists for a suffix, the standard abbreviation and the primary name included.</summary>
    internal IReadOnlyList<string> UsSuffixSpellings(string std) => _usSuffixSpellings[std];

    internal bool TryUsDirection(string written, out string abbr, out string full)
    {
        if (_usDirections.TryGetValue(written.ToUpperInvariant(), out (string Abbr, string Full) v))
        {
            abbr = v.Abbr;
            full = v.Full;
            return true;
        }

        abbr = full = string.Empty;
        return false;
    }

    /// <summary>Every spelling Publication 28 appendix C2 lists for a secondary unit designator, the approved abbreviation included.</summary>
    internal IReadOnlyList<string> UsUnitSpellings(string std) => _usUnitSpellings.TryGetValue(std, out List<string>? l) ? l : [std];

    internal bool TryUsUnit(string written, out string std) => _usUnitStd.TryGetValue(written.ToUpperInvariant(), out std!);

    internal bool TryCaType(string written, out string std, out string lang, out bool before)
    {
        if (_caTypeVariants.TryGetValue(written.ToUpperInvariant(), out (string Std, string Lang, bool Before) v))
        {
            std = v.Std;
            lang = v.Lang;
            before = v.Before;
            return true;
        }

        std = lang = string.Empty;
        before = false;
        return false;
    }

    internal IReadOnlyList<string> CaTypeSpellings(string lang, string std) => _caTypeSpellings[lang + ":" + std];

    internal bool TryCaDirection(string written, out string abbr, out string lang)
    {
        if (_caDirections.TryGetValue(written.ToUpperInvariant(), out (string Abbr, string Lang) v))
        {
            abbr = v.Abbr;
            lang = v.Lang;
            return true;
        }

        abbr = lang = string.Empty;
        return false;
    }

    /// <summary>The written forms of a Canadian direction in one language: abbreviation first, then the spelled-out names.</summary>
    internal IReadOnlyList<string> CaDirectionSpellings(string lang, string abbr)
    {
        List<string> result = [abbr];
        foreach (KeyValuePair<string, (string Abbr, string Lang)> kv in _caDirections.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (kv.Value.Abbr == abbr && kv.Value.Lang == lang && kv.Key.Length > abbr.Length && !kv.Key.Contains(' '))
            {
                result.Add(kv.Key);
            }
        }

        return result;
    }

    private static IEnumerable<string[]> Read(string directory, string file)
    {
        foreach (string line in File.ReadLines(Path.Combine(directory, file), Encoding.UTF8))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            yield return line.Split('\t');
        }
    }

    /// <summary>Province and territory names, English then French (Canada Post Addressing Guidelines, section 5).</summary>
    internal static readonly IReadOnlyDictionary<string, (string English, string French)> CaProvinceNames = new Dictionary<string, (string, string)>(StringComparer.Ordinal)
    {
        ["AB"] = ("Alberta", "Alberta"),
        ["BC"] = ("British Columbia", "Colombie-Britannique"),
        ["MB"] = ("Manitoba", "Manitoba"),
        ["NB"] = ("New Brunswick", "Nouveau-Brunswick"),
        ["NL"] = ("Newfoundland and Labrador", "Terre-Neuve-et-Labrador"),
        ["NS"] = ("Nova Scotia", "Nouvelle-Écosse"),
        ["NT"] = ("Northwest Territories", "Territoires du Nord-Ouest"),
        ["NU"] = ("Nunavut", "Nunavut"),
        ["ON"] = ("Ontario", "Ontario"),
        ["PE"] = ("Prince Edward Island", "Île-du-Prince-Édouard"),
        ["QC"] = ("Quebec", "Québec"),
        ["SK"] = ("Saskatchewan", "Saskatchewan"),
        ["YT"] = ("Yukon", "Yukon"),
    };

    /// <summary>Statistics Canada province and territory codes (the ODA <c>pruid</c> column) to postal abbreviations.</summary>
    internal static readonly IReadOnlyDictionary<string, string> CaProvinceByPruid = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["10"] = "NL", ["11"] = "PE", ["12"] = "NS", ["13"] = "NB", ["24"] = "QC", ["35"] = "ON", ["46"] = "MB",
        ["47"] = "SK", ["48"] = "AB", ["59"] = "BC", ["60"] = "YT", ["61"] = "NT", ["62"] = "NU",
    };

    private const string Accented = "ÀÁÂÃÄÅÇÈÉÊËÌÍÎÏÑÒÓÔÕÖÙÚÛÜÝŸ";
    private const string Plain = "AAAAAACEEEEIIIINOOOOOUUUUYY";

    /// <summary>Upper-cases and strips the accents of Latin-1 letters: the comparison form for matching register fields. Done by table because normalization data is not available with invariant globalization.</summary>
    internal static string Fold(string s)
    {
        StringBuilder sb = new(s.Length);
        foreach (char c in s)
        {
            char u = char.ToUpperInvariant(c);
            int i = Accented.IndexOf(u, StringComparison.Ordinal);
            sb.Append(i >= 0 ? Plain[i] : u);
        }

        return sb.ToString();
    }
}
