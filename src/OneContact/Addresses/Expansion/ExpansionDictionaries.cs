using System.Text;
using RecursiveAI.OneContact.Data;

namespace RecursiveAI.OneContact.Addresses.Expansion;

/// <summary>The languages whose dictionaries expansion uses.</summary>
[Flags]
public enum ExpansionLanguages
{
    /// <summary>No language: expansion only normalizes.</summary>
    None = 0,

    /// <summary>English (street types, directionals, unit and level words, post office words).</summary>
    English = 1,

    /// <summary>French (as written in Canada and France).</summary>
    French = 2,
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

    /// <summary>Gets the phrases that are ambiguous (a single letter that may be an initial, a word that may be a name) as listed by the dictionaries.</summary>
    internal static IReadOnlySet<string> Ambiguous => Loaded.Value.Ambiguous;

    /// <summary>Finds the entries of a language that contain a phrase.</summary>
    /// <param name="language">The language code, <c>en</c> or <c>fr</c>.</param>
    /// <param name="phrase">The phrase, lower case with single spaces.</param>
    /// <returns>The entries; empty when none.</returns>
    internal static IReadOnlyList<PhraseEntry> Find(string language, string phrase) =>
        Loaded.Value.ByPhrase.TryGetValue((language, phrase), out List<PhraseEntry>? entries) ? entries : [];

    /// <summary>Gets a French elision prefix test (<c>l'</c>, <c>d'</c>): such a prefix is split from the word that follows it.</summary>
    internal static IReadOnlySet<string> Elisions => Loaded.Value.Elisions;

    private static Index Load()
    {
        EmbeddedTables tables = new(typeof(ExpansionDictionaries).Assembly, "OneContact.Data.libpostal-dictionaries.ocd");
        Index index = new();
        foreach (string language in new[] { "en", "fr" })
        {
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
                foreach (string line in Encoding.UTF8.GetString(tables.GetTable(language + "/" + file).Span).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    index.Ambiguous.Add(line.Split('\t')[0]);
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

        internal HashSet<string> Ambiguous { get; } = new(StringComparer.Ordinal);

        internal HashSet<string> Elisions { get; } = new(StringComparer.Ordinal);

        internal int MaxWords { get; set; } = 1;
    }
}
