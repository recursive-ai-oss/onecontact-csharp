using System.Text;
using System.Text.Json;

namespace RecursiveAI.OneContact.Ingest.LibPhoneNumberGeo;

/// <summary>
/// Compiles the CLDR territory display names. One table <c>n/&lt;locale&gt;</c> per locale file holds that locale's own two-letter region
/// names (a count, then per region its two letters, a varint length and the UTF-8 name, sorted by code); alternate spellings and
/// non-region codes are not kept. The table <c>parents</c> lists the explicit parent locales as <c>child=parent</c> lines.
/// </summary>
internal sealed class TerritorySource : IIngestSource
{
    private const string Prefix = "cldr-localenames-full/main/";
    private const string Suffix = "/territories.json";

    // The ISO 3166-1 alpha-2 codes (the region codes the reference's runtime names in its root locale), and the locales that runtime has
    // no names for (it answers in English there); both were read from it, see docs/phones-geo.md.
    private static readonly HashSet<string> IsoRegions = ReadList("iso3166-1-alpha2.txt");
    private static readonly HashSet<string> Unnamed = ReadList("unavailable-locales.txt");

    private static HashSet<string> ReadList(string name)
    {
        using Stream stream = typeof(TerritorySource).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing resource {name}.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return [.. reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    public string Name => GeoSources.TerritoriesName;

    public string ToolVersion => "1";

    public string NoticeText =>
        "Unicode CLDR territory names\n" +
        "Copyright (c) 1991-2023 Unicode, Inc.\n\n" +
        "The territory display names compiled into this package (cldr-localenames-full and cldr-core/supplemental/parentLocales.json of the\n" +
        "CLDR JSON distribution, release 43, https://github.com/unicode-org/cldr-json) are Unicode Data Files, distributed under the\n" +
        "Unicode License v3 (https://www.unicode.org/license.txt).\n";

    public void Convert(IngestInput input, TableSetWriter tables)
    {
        Dictionary<string, Dictionary<string, string>> full = new(StringComparer.Ordinal);
        Dictionary<string, string> parents = new(StringComparer.Ordinal);
        foreach (PinnedFile file in input.Pin.Files)
        {
            if (file.Path.StartsWith(Prefix, StringComparison.Ordinal) && file.Path.EndsWith(Suffix, StringComparison.Ordinal))
            {
                string locale = file.Path[Prefix.Length..^Suffix.Length];
                if (!Unnamed.Contains(locale))
                {
                    full.Add(locale, Names(locale, input.ReadAllBytes(file.Path)));
                }
            }
            else if (file.Path == "cldr-core/supplemental/parentLocales.json")
            {
                parents = Parents(input.ReadAllBytes(file.Path));
            }
            else
            {
                throw new FormatException($"Unexpected pinned path '{file.Path}'.");
            }
        }

        // A locale keeps only the names that differ from what it would inherit; resolving a name at run time walks the same chain.
        foreach ((string locale, Dictionary<string, string> names) in full)
        {
            string? ancestor = ParentOf(locale, parents);
            while (ancestor is not null && !full.ContainsKey(ancestor))
            {
                ancestor = ParentOf(ancestor, parents);
            }

            SortedDictionary<string, string> kept = new(StringComparer.Ordinal);
            foreach ((string code, string name) in names)
            {
                bool inherited = ancestor is not null && full[ancestor].TryGetValue(code, out string? above) && above == name;
                if (!inherited)
                {
                    kept.Add(code, name);
                }
            }

            tables.Add("n/" + locale, Encode(kept));

            // The root locale of the reference's runtime has English names for the ISO 3166-1 regions only; it is where a language
            // with no name for a region, and a tag that is not a language, end up.
            if (locale == "en")
            {
                tables.Add("n/root", Encode(new SortedDictionary<string, string>(names.Where(kv => IsoRegions.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value), StringComparer.Ordinal)));
            }

        }

        tables.Add("parents", EncodeParents(parents));
    }

    private static string? ParentOf(string locale, Dictionary<string, string> parents)
    {
        if (parents.TryGetValue(locale, out string? explicitParent))
        {
            return explicitParent == "root" ? null : explicitParent;
        }

        int cut = locale.LastIndexOf('-');
        return cut < 0 ? null : locale[..cut];
    }

    private static Dictionary<string, string> Names(string locale, byte[] json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement territories = doc.RootElement.GetProperty("main").GetProperty(locale).GetProperty("localeDisplayNames").GetProperty("territories");
        Dictionary<string, string> names = new(StringComparer.Ordinal);
        foreach (JsonProperty p in territories.EnumerateObject())
        {
            if (p.Name.Length == 2 && p.Name.All(char.IsAsciiLetterUpper))
            {
                // CLDR puts a right-to-left override in front of one Adlam name; the reference's runtime shows the name without it.
                string name = p.Value.GetString() ?? throw new FormatException($"{locale}: {p.Name} is not a string.");
                names.Add(p.Name, name.Replace("\u202E", string.Empty, StringComparison.Ordinal));
            }
        }

        return names;
    }

    private static byte[] Encode(SortedDictionary<string, string> names)
    {
        using MemoryStream ms = new();
        ms.WriteByte((byte)(names.Count & 0xFF));
        ms.WriteByte((byte)(names.Count >> 8));
        foreach ((string code, string name) in names)
        {
            ms.WriteByte((byte)code[0]);
            ms.WriteByte((byte)code[1]);
            byte[] utf8 = Encoding.UTF8.GetBytes(name);
            int length = utf8.Length;
            while (length >= 0x80)
            {
                ms.WriteByte((byte)(length | 0x80));
                length >>= 7;
            }

            ms.WriteByte((byte)length);
            ms.Write(utf8);
        }

        return ms.ToArray();
    }

    private static Dictionary<string, string> Parents(byte[] json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement list = doc.RootElement.GetProperty("supplemental").GetProperty("parentLocales").GetProperty("parentLocale");
        Dictionary<string, string> parents = new(StringComparer.Ordinal);
        foreach (JsonProperty p in list.EnumerateObject())
        {
            parents.Add(p.Name, p.Value.GetString() ?? throw new FormatException($"parent of {p.Name} is not a string."));
        }

        return parents;
    }

    private static byte[] EncodeParents(Dictionary<string, string> parents)
    {
        StringBuilder sb = new();
        foreach ((string child, string parent) in parents.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append(child).Append('=').Append(parent).Append('\n');
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
