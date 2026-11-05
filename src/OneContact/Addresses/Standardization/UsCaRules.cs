namespace RecursiveAI.OneContact.Addresses.Standardization;

/// <summary>
/// The United States and Canadian rule tables (USPS Publication 28; Canada Post Addressing Guidelines, English and French editions),
/// loaded once from the embedded data files. Every lookup key is upper case with single spaces.
/// </summary>
internal static class UsCaRules
{
    /// <summary>A street type of Canada Post's list.</summary>
    /// <param name="Standard">The abbreviation to print.</param>
    /// <param name="French">True for a type of the French edition (printed before the street name).</param>
    internal readonly record struct CaStreetType(string Standard, bool French);

    private static readonly Lazy<Tables> Loaded = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>USPS Publication 28 Appendix C1: street suffix variant to standard abbreviation and primary name.</summary>
    internal static IReadOnlyDictionary<string, (string Standard, string Primary)> UsSuffixes => Loaded.Value.UsSuffixes;

    /// <summary>USPS Publication 28 Appendix B directionals: variant to abbreviation and full word.</summary>
    internal static IReadOnlyDictionary<string, (string Abbreviation, string Full)> UsDirectionals => Loaded.Value.UsDirectionals;

    /// <summary>USPS Publication 28 Appendix C2 secondary unit designators: variant to abbreviation and whether a range must follow.</summary>
    internal static IReadOnlyDictionary<string, (string Abbreviation, bool NeedsRange)> UsUnits => Loaded.Value.UsUnits;

    /// <summary>USPS state, district, territory and military codes by name (upper case).</summary>
    internal static IReadOnlyDictionary<string, string> UsStateNames => Loaded.Value.UsStateNames;

    /// <summary>The kind of each USPS code: state, district, territory or military.</summary>
    internal static IReadOnlyDictionary<string, string> UsStateKinds => Loaded.Value.UsStateKinds;

    /// <summary>Puerto Rico words (variant to standard form and role).</summary>
    internal static IReadOnlyDictionary<string, (string Standard, string Role)> PuertoRicoWords => Loaded.Value.PuertoRicoWords;

    /// <summary>Canada Post street types of the English edition by variant (printed after the street name).</summary>
    internal static IReadOnlyDictionary<string, CaStreetType> CaStreetTypesEnglish => Loaded.Value.CaStreetTypesEnglish;

    /// <summary>Canada Post street types of the French edition by variant (printed before the street name, after an ordinal name).</summary>
    internal static IReadOnlyDictionary<string, CaStreetType> CaStreetTypesFrench => Loaded.Value.CaStreetTypesFrench;

    /// <summary>Canada Post street directions by variant (English and French) to abbreviation.</summary>
    internal static IReadOnlyDictionary<string, string> CaDirections => Loaded.Value.CaDirections;

    /// <summary>Canada Post unit designators by variant to abbreviation.</summary>
    internal static IReadOnlyDictionary<string, string> CaUnits => Loaded.Value.CaUnits;

    /// <summary>Province and territory names (English and French, upper case) to code.</summary>
    internal static IReadOnlyDictionary<string, string> CaProvinceNames => Loaded.Value.CaProvinceNames;

    /// <summary>Canada Post delivery words (box, rural route, general delivery, station, outlet) by variant to standard form and role.</summary>
    internal static IReadOnlyDictionary<string, (string Standard, string Role)> CaDeliveryWords => Loaded.Value.CaDeliveryWords;

    /// <summary>The longest variant, in words, of each table that holds multi-word variants.</summary>
    internal static int MaxWords => Loaded.Value.MaxWords;

    private static Tables Load()
    {
        Tables t = new();
        foreach (string[] r in RuleFile.Read("us-street-suffixes.tsv"))
        {
            t.UsSuffixes[r[0]] = (r[1], r[2]);
        }

        foreach (string[] r in RuleFile.Read("us-directionals.tsv"))
        {
            t.UsDirectionals[r[0]] = (r[1], r[2]);
        }

        foreach (string[] r in RuleFile.Read("us-unit-designators.tsv"))
        {
            t.UsUnits[r[0]] = (r[1], r[2] == "1");
        }

        foreach (string[] r in RuleFile.Read("us-states.tsv"))
        {
            t.UsStateNames[r[0].ToUpperInvariant()] = r[1];
            t.UsStateNames[r[1]] = r[1];
            t.UsStateKinds[r[1]] = r[2];
        }

        foreach (string[] r in RuleFile.Read("us-puerto-rico.tsv"))
        {
            t.PuertoRicoWords[r[0]] = (r[1], r[2]);
        }

        foreach (string[] r in RuleFile.Read("ca-street-types.tsv"))
        {
            (r[2] == "fr" ? t.CaStreetTypesFrench : t.CaStreetTypesEnglish)[r[0]] = new CaStreetType(r[1], r[2] == "fr");
        }

        foreach (string[] r in RuleFile.Read("ca-directions.tsv"))
        {
            t.CaDirections[r[0]] = r[1];
        }

        foreach (string[] r in RuleFile.Read("ca-unit-designators.tsv"))
        {
            t.CaUnits[r[0]] = r[1];
        }

        foreach (string[] r in RuleFile.Read("ca-provinces.tsv"))
        {
            t.CaProvinceNames[r[0]] = r[1];
            t.CaProvinceNames[r[1]] = r[1];
        }

        foreach (string[] r in RuleFile.Read("ca-delivery-words.tsv"))
        {
            t.CaDeliveryWords[r[0]] = (r[1], r[2]);
        }

        t.MaxWords = 4;
        return t;
    }

    private sealed class Tables
    {
        internal Dictionary<string, (string, string)> UsSuffixes { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, (string, string)> UsDirectionals { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, (string, bool)> UsUnits { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, string> UsStateNames { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, string> UsStateKinds { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, (string, string)> PuertoRicoWords { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, CaStreetType> CaStreetTypesEnglish { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, CaStreetType> CaStreetTypesFrench { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, string> CaDirections { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, string> CaUnits { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, string> CaProvinceNames { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, (string, string)> CaDeliveryWords { get; } = new(StringComparer.Ordinal);

        internal int MaxWords { get; set; }
    }
}
