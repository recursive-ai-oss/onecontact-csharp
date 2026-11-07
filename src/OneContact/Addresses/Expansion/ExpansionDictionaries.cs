using System.Text;
using RecursiveAI.OneContact.Data;

namespace RecursiveAI.OneContact.Addresses.Expansion;

/// <summary>The languages whose dictionaries expansion uses. The underlying type is 64-bit so that more languages can be added.</summary>
[Flags]
public enum ExpansionLanguages : long
{
    /// <summary>No language: expansion only normalizes.</summary>
    None = 0,

    /// <summary>English (street types, directionals, unit and level words, post office words).</summary>
    English = 1,

    /// <summary>French (as written in Canada and France).</summary>
    French = 2,

    /// <summary>German (street types that end a name: <c>str</c> for <c>strasse</c>, directionals, unit words).</summary>
    German = 4,

    /// <summary>Dutch (street types that end a name: <c>straat</c>, <c>laan</c>, directionals, unit words).</summary>
    Dutch = 8,

    /// <summary>Italian (street types, directionals, unit words).</summary>
    Italian = 16,

    /// <summary>Luxembourgish (street types).</summary>
    Luxembourgish = 32,

    /// <summary>Spanish (street types, directionals, unit and level words, post office words).</summary>
    Spanish = 64,

    /// <summary>Portuguese (street types, directionals, unit and level words, post office words).</summary>
    Portuguese = 128,

    /// <summary>Catalan (as written in Andorra and in Spanish addresses).</summary>
    Catalan = 256,

    /// <summary>Basque (as written in Spanish addresses).</summary>
    Basque = 512,

    /// <summary>Galician (as written in Spanish addresses).</summary>
    Galician = 1024,

    /// <summary>Maltese (street types, place names).</summary>
    Maltese = 2048,

    /// <summary>Irish (street types, building and unit words).</summary>
    Irish = 4096,

    /// <summary>Danish (street types, directionals, unit and level words, post office words).</summary>
    Danish = 1 << 16,

    /// <summary>Swedish (street types, directionals, unit and level words, post office words).</summary>
    Swedish = 1 << 17,

    /// <summary>Finnish (street types, directionals, unit and level words, post office words).</summary>
    Finnish = 1 << 18,

    /// <summary>Norwegian, Bokmål (street types, directionals, unit and level words, post office words); the libpostal dictionary for it is named <c>nb</c>.</summary>
    Norwegian = 1 << 19,

    /// <summary>Icelandic (street types, directionals, unit and level words, post office words).</summary>
    Icelandic = 1 << 20,

    /// <summary>Estonian (street types, directionals, unit and level words, post office words).</summary>
    Estonian = 1 << 21,

    /// <summary>Latvian (street types, directionals, unit and level words).</summary>
    Latvian = 1 << 22,

    /// <summary>Lithuanian (street types, directionals, unit and level words, post office words).</summary>
    Lithuanian = 1 << 23,

    /// <summary>Polish (street types, unit and level words, post office words).</summary>
    Polish = 1 << 24,

    /// <summary>Czech (street types, unit and level words, post office words).</summary>
    Czech = 1 << 25,

    /// <summary>Slovak (street types, unit and level words, post office words).</summary>
    Slovak = 1 << 26,

    /// <summary>Slovenian (street types, unit and level words, post office words).</summary>
    Slovenian = 1 << 27,

    /// <summary>Croatian (street types, unit and level words, post office words).</summary>
    Croatian = 1 << 28,

    /// <summary>Hungarian (street types that end a name, unit and level words, post office words).</summary>
    Hungarian = 1 << 29,

    /// <summary>Romanian (street types, unit and level words, post office words).</summary>
    Romanian = 1 << 30,
}

/// <summary>What kind of word a dictionary phrase is; it decides where the phrase may be expanded.</summary>
internal enum PhraseKind
{
    StreetType,
    Directional,
    NumberedUnit,
    Standalone,
    PostOffice,
    Synonym,
    StreetName,
    NumberWord,
}

/// <summary>One dictionary entry: the canonical phrase first, then its variants.</summary>
internal sealed record PhraseEntry(string[] Phrases, PhraseKind Kind, string Language);

/// <summary>
/// The libpostal dictionaries for English and French as compiled by the ingest tool (table layout: docs/address-dictionaries.md), indexed by phrase.
/// Built once on first use from the embedded tables.
/// </summary>
internal static class ExpansionDictionaries
{
    private static readonly Lazy<Index> Loaded = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly (string File, PhraseKind Kind)[] Files =
    [
        ("street_types", PhraseKind.StreetType),
        ("directionals", PhraseKind.Directional),
        ("unit_types_numbered", PhraseKind.NumberedUnit),
        ("level_types_numbered", PhraseKind.NumberedUnit),
        ("number", PhraseKind.NumberWord),
        ("unit_types_standalone", PhraseKind.Standalone),
        ("level_types_standalone", PhraseKind.Standalone),
        ("level_types_basement", PhraseKind.Standalone),
        ("level_types_mezzanine", PhraseKind.Standalone),
        ("level_types_sub_basement", PhraseKind.Standalone),
        ("building_types", PhraseKind.Standalone),
        ("staircases", PhraseKind.Standalone),
        ("entrances", PhraseKind.Standalone),
        ("qualifiers", PhraseKind.Standalone),
        ("no_number", PhraseKind.Standalone),
        ("post_office", PhraseKind.PostOffice),
        ("synonyms", PhraseKind.Synonym),
        ("street_names", PhraseKind.StreetName),
    ];

    /// <summary>Gets the longest phrase in the dictionaries, in words.</summary>
    internal static int MaxWords => Loaded.Value.MaxWords;

    private static readonly string[] Legacy = ["en", "fr", "de", "nl", "it", "lb"];

    /// <summary>Gets whether a language lists the phrase as ambiguous (a later language's own list; the first six languages share <see cref="Ambiguous"/>).</summary>
    internal static bool AmbiguousIn(string language, string phrase) =>
        Loaded.Value.AmbiguousByLanguage.TryGetValue(language, out HashSet<string>? set) && set.Contains(phrase);

    /// <summary>Gets the phrases that are ambiguous (a single letter that may be an initial, a word that may be a name) as listed by the dictionaries.</summary>
    internal static IReadOnlySet<string> Ambiguous => Loaded.Value.Ambiguous;

    /// <summary>Finds the entries of a language that contain a phrase.</summary>
    /// <param name="language">The language code: <c>en</c>, <c>fr</c>, <c>de</c>, <c>nl</c>, <c>it</c> or <c>lb</c>.</param>
    /// <param name="phrase">The phrase, lower case with single spaces.</param>
    /// <returns>The entries; empty when none.</returns>
    internal static IReadOnlyList<PhraseEntry> Find(string language, string phrase) =>
        Loaded.Value.ByPhrase.TryGetValue((language, phrase), out List<PhraseEntry>? entries) ? entries : [];

    /// <summary>Gets a French elision prefix test (<c>l'</c>, <c>d'</c>): such a prefix is split from the word that follows it.</summary>
    internal static IReadOnlySet<string> Elisions => Loaded.Value.Elisions;

    /// <summary>
    /// The endings that German and Dutch street names are written with: each entry is the full ending first and its abbreviations after it (<c>strasse</c>, <c>str</c>).
    /// Periods are dropped and only abbreviations of three letters or more are kept, because a shorter ending (<c>w</c> for <c>weg</c>) is also a plain letter.
    /// </summary>
    /// <param name="language">The language code, <c>de</c> or <c>nl</c>.</param>
    /// <returns>The entries as (abbreviation, full ending) pairs, longest abbreviation first.</returns>
    internal static IReadOnlyList<(string Abbreviation, string Full)> Suffixes(string language) =>
        Loaded.Value.Suffixes.TryGetValue(language, out List<(string, string)>? list) ? list : [];

    private static Index Load()
    {
        EmbeddedTables tables = new(typeof(ExpansionDictionaries).Assembly, "OneContact.Data.libpostal-dictionaries.ocd");
        Index index = new();
        foreach (string language in new[] { "en", "fr", "de", "nl", "it", "lb", "es", "pt", "ca", "eu", "gl", "mt", "ga", "da", "sv", "fi", "nb", "is", "et", "lv", "lt", "pl", "cs", "sk", "sl", "hr", "hu", "ro" })
        {
            foreach (string file in new[] { "concatenated_suffixes_separable", "concatenated_suffixes_inseparable" })
            {
                try
                {
                    foreach (string line in Encoding.UTF8.GetString(tables.GetTable(language + "/" + file).Span).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] phrases = line.Split('\t');
                        foreach (string abbreviation in phrases.Skip(1))
                        {
                            string a = abbreviation.Replace(".", string.Empty, StringComparison.Ordinal);
                            if (a.Length >= 3 && a != phrases[0])
                            {
                                if (!index.Suffixes.TryGetValue(language, out List<(string, string)>? list))
                                {
                                    list = [];
                                    index.Suffixes[language] = list;
                                }

                                list.Add((a, phrases[0]));
                            }
                        }
                    }
                }
                catch (InvalidDataException)
                {
                    // The language has no such dictionary.
                }
            }

            foreach ((string file, PhraseKind kind) in Files)
            {
                string name = language + "/" + file;
                string[] lines;
                try
                {
                    lines = Encoding.UTF8.GetString(tables.GetTable(name).Span).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                }
                catch (InvalidDataException)
                {
                    continue;
                }

                foreach (string line in lines)
                {
                    string[] phrases = line.Split('\t');
                    PhraseEntry entry = new(phrases, kind, language);
                    foreach (string phrase in phrases)
                    {
                        if (!index.ByPhrase.TryGetValue((language, phrase), out List<PhraseEntry>? list))
                        {
                            list = [];
                            index.ByPhrase[(language, phrase)] = list;
                        }

                        list.Add(entry);
                        index.MaxWords = Math.Max(index.MaxWords, phrase.Count(c => c == ' ') + 1);
                    }
                }
            }

            foreach (string file in new[] { "ambiguous_expansions" })
            {
                string[] lines;
                try
                {
                    lines = Encoding.UTF8.GetString(tables.GetTable(language + "/" + file).Span).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                }
                catch (InvalidDataException)
                {
                    continue;
                }

                // The first six languages share one set (it is what the keys of the United States, Canada and the first Tier 2 group were built with);
                // each later language keeps its own, used only when that language is asked for.
                HashSet<string> target = Array.IndexOf(Legacy, language) >= 0 ? index.Ambiguous : index.AmbiguousByLanguage.GetValueOrDefault(language) ?? (index.AmbiguousByLanguage[language] = new HashSet<string>(StringComparer.Ordinal));
                foreach (string line in lines)
                {
                    target.Add(line.Split('\t')[0]);
                }
            }
        }

        foreach (string line in Encoding.UTF8.GetString(tables.GetTable("fr/elisions").Span).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            index.Elisions.Add(line);
        }

        return index;
    }

    private sealed class Index
    {
        internal Dictionary<(string, string), List<PhraseEntry>> ByPhrase { get; } = [];

        internal Dictionary<string, List<(string, string)>> Suffixes { get; } = [];

        internal HashSet<string> Ambiguous { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, HashSet<string>> AmbiguousByLanguage { get; } = [];

        internal HashSet<string> Elisions { get; } = new(StringComparer.Ordinal);

        internal int MaxWords { get; set; } = 1;
    }
}
