using System.Text;

namespace OneContact.Data.AddressUsCa;

/// <summary>One item of a compiled layout line: a literal, or a slot with the text written before and after it when it has content.</summary>
internal readonly record struct LayoutItem(string Before, string Slot, string After);

internal sealed class Layout
{
    internal Layout(string id, string country, string language, bool label, double weight, List<List<LayoutItem>> lines)
    {
        Id = id;
        Country = country;
        Language = language;
        IsLabel = label;
        Weight = weight;
        Lines = lines;
    }

    internal string Id { get; }

    internal string Country { get; }

    internal string Language { get; }

    internal bool IsLabel { get; }

    internal double Weight { get; }

    internal List<List<LayoutItem>> Lines { get; }
}

/// <summary>The committed templates: layouts and the invented name parts, plus a hash of each file for the manifest.</summary>
internal sealed class Templates
{
    private readonly List<Layout> _layouts = [];
    private readonly Dictionary<string, List<string>> _names = new(StringComparer.Ordinal);

    internal Templates(string directory)
    {
        string layoutsPath = Path.Combine(directory, "layouts.tsv");
        string namesPath = Path.Combine(directory, "names.tsv");
        // layouts-pr.tsv (Puerto Rico, with the urbanization line) is read after the base layouts and versioned on its own, so the
        // base template version of a United States / Canada run does not change.
        string prPath = Path.Combine(directory, "layouts-pr.tsv");
        foreach (string path in File.Exists(prPath) ? new[] { layoutsPath, prPath } : new[] { layoutsPath })
        {
            foreach (string line in File.ReadLines(path, Encoding.UTF8))
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] c = line.Split('\t');
                List<List<LayoutItem>> lines = [];
                foreach (string raw in c[5].Split("\\n", StringSplitOptions.None))
                {
                    lines.Add(ParseLine(raw));
                }

                _layouts.Add(new Layout(c[0], c[1], c[2], c[3] == "label", double.Parse(c[4], System.Globalization.CultureInfo.InvariantCulture), lines));
            }
        }

        PrVersion = File.Exists(prPath) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(prPath))).ToLowerInvariant()[..16] : string.Empty;

        foreach (string line in File.ReadLines(namesPath, Encoding.UTF8))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            int tab = line.IndexOf('\t', StringComparison.Ordinal);
            string key = line[..tab];
            if (!_names.TryGetValue(key, out List<string>? list))
            {
                _names[key] = list = [];
            }

            list.Add(line[(tab + 1)..]);
        }

        Version = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(File.ReadAllText(layoutsPath, Encoding.UTF8) + "\n--\n" + File.ReadAllText(namesPath, Encoding.UTF8)))).ToLowerInvariant()[..16];
    }

    /// <summary>The template version: the first 16 hex digits of the SHA-256 over the layout and name files.</summary>
    internal string Version { get; }

    /// <summary>The first 16 hex digits of the SHA-256 of <c>layouts-pr.tsv</c>, or empty when there is none.</summary>
    internal string PrVersion { get; }

    internal IReadOnlyList<string> Names(string kind) => _names[kind];

    /// <summary>Picks a layout for a country and language by weight.</summary>
    internal Layout Pick(string country, string language, Prng r)
    {
        List<Layout> candidates = _layouts.Where(l => l.Country == country && l.Language == language).ToList();
        int i = r.Weighted(candidates.Select(l => l.Weight).ToList());
        return candidates[i];
    }

    internal IReadOnlyList<Layout> All => _layouts;

    private static List<LayoutItem> ParseLine(string raw)
    {
        List<LayoutItem> items = [];
        int i = 0;
        while (i < raw.Length)
        {
            if (raw[i] != '{')
            {
                throw new InvalidDataException("layout text outside a slot: " + raw);
            }

            int close = raw.IndexOf('}', i);
            string[] parts = raw[(i + 1)..close].Split('|');
            items.Add(parts.Length switch
            {
                1 => new LayoutItem(string.Empty, parts[0], string.Empty),
                2 => new LayoutItem(parts[0], parts[1], string.Empty),
                _ => new LayoutItem(parts[0], parts[1], parts[2]),
            });
            i = close + 1;
        }

        return items;
    }
}
