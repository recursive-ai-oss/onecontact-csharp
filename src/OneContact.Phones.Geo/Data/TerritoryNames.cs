using System.Collections.Concurrent;
using System.Text;

namespace RecursiveAI.OneContact.Phones.Geo.Data;

/// <summary>
/// Country names in a language, from the CLDR territory names, resolved the way CLDR inheritance does: the locale's own table, then
/// its parent (an explicit parent where CLDR names one, otherwise the tag with its last part removed), up to the language, and last
/// the root names (English names for the ISO 3166-1 regions), which is where a language without a name for a region ends up.
/// </summary>
internal sealed class TerritoryNames
{
    private readonly GeoTables _tables;
    private readonly ConcurrentDictionary<string, Dictionary<string, string>?> _locales = new(StringComparer.Ordinal);
    private readonly Lazy<Dictionary<string, string>> _parents;

    internal TerritoryNames(GeoTables tables)
    {
        _tables = tables;
        _parents = new Lazy<Dictionary<string, string>>(LoadParents, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Finds the name of a region in a language.</summary>
    /// <param name="tag">The language.</param>
    /// <param name="region">A two-letter region code.</param>
    /// <returns>The name, or the code itself when no locale in the chain names the region.</returns>
    internal string Get(LanguageTag tag, string region)
    {
        foreach (string locale in Chain(tag))
        {
            if (Names(locale) is { } names && names.TryGetValue(region, out string? name))
            {
                return name;
            }
        }

        return region;
    }

    private List<string> Chain(LanguageTag tag)
    {
        List<string> chain = [];
        if (tag.Language.Length != 0)
        {
            string script = tag.Script.Length != 0 ? tag.Script : ImpliedScript(tag);
            // Names follow the current codes of CLDR: Moldovan is Romanian and Tagalog is Filipino (the tables of area descriptions and
            // carriers are asked for by the code as given).
            string language = tag.Language switch { "mo" => "ro", "tl" => "fil", _ => tag.Language };
            string current = language
                + (script.Length != 0 ? "-" + script : string.Empty)
                + (tag.Region.Length != 0 ? "-" + tag.Region : string.Empty);
            Dictionary<string, string> parents = _parents.Value;
            while (current.Length != 0 && chain.Count < 8)
            {
                chain.Add(current);
                if (parents.TryGetValue(current, out string? parent))
                {
                    current = parent == "root" ? string.Empty : parent;
                }
                else
                {
                    int cut = current.LastIndexOf('-');
                    current = cut < 0 ? string.Empty : current[..cut];
                }
            }
        }

        chain.Add("root");

        return chain;
    }

    // Where the writing system follows from the region (the data's own locales are named with the script for these languages).
    private static string ImpliedScript(LanguageTag tag) => (tag.Language, tag.Region) switch
    {
        ("zh", "TW" or "HK" or "MO") => "Hant",
        ("zh", "CN" or "SG") => "Hans",
        _ => string.Empty,
    };

    private Dictionary<string, string>? Names(string locale) =>
        _locales.GetOrAdd(locale, name => _tables.TryGetTable("n/" + name, out ReadOnlyMemory<byte> bytes) ? Decode(bytes.Span) : null);

    private static Dictionary<string, string> Decode(ReadOnlySpan<byte> span)
    {
        if (span.Length < 2)
        {
            throw new InvalidDataException("A territory table is truncated.");
        }

        int count = span[0] | (span[1] << 8);
        Dictionary<string, string> names = new(count, StringComparer.Ordinal);
        int pos = 2;
        for (int i = 0; i < count; i++)
        {
            if (pos + 3 > span.Length)
            {
                throw new InvalidDataException("A territory table is truncated.");
            }

            string code = string.Create(2, (span[pos], span[pos + 1]), static (s, c) =>
            {
                s[0] = (char)c.Item1;
                s[1] = (char)c.Item2;
            });
            pos += 2;
            int length = 0;
            for (int shift = 0; ; shift += 7)
            {
                if (pos >= span.Length || shift > 21)
                {
                    throw new InvalidDataException("A territory table is damaged.");
                }

                byte b = span[pos++];
                length |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    break;
                }
            }

            if (length > span.Length - pos)
            {
                throw new InvalidDataException("A territory table is truncated.");
            }

            names[code] = Encoding.UTF8.GetString(span.Slice(pos, length));
            pos += length;
        }

        return names;
    }

    private Dictionary<string, string> LoadParents()
    {
        Dictionary<string, string> parents = new(StringComparer.Ordinal);
        if (_tables.TryGetTable("parents", out ReadOnlyMemory<byte> bytes))
        {
            foreach (string line in Encoding.UTF8.GetString(bytes.Span).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = line.IndexOf('=', StringComparison.Ordinal);
                if (eq <= 0 || eq == line.Length - 1)
                {
                    throw new InvalidDataException("The parent-locale table is damaged.");
                }

                parents[line[..eq]] = line[(eq + 1)..];
            }
        }

        return parents;
    }
}
