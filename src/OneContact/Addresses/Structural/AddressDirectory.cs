using System.Collections.Concurrent;

namespace RecursiveAI.OneContact.Addresses.Structural;

/// <summary>
/// The structural description of one country as the address data gives it, in one language: the format, the required fields,
/// the postal code pattern and the subdivisions. Values a country does not give come from the data's default node (<c>data/ZZ</c>).
/// </summary>
internal sealed class CountryStructure
{
    private readonly AddressDataSet _data;
    private readonly AddressNode _node;
    private readonly AddressNode _default;
    private readonly Lazy<AddressFormat> _format;
    private readonly Lazy<AddressFormat?> _latinFormat;

    internal CountryStructure(AddressDataSet data, AddressNode node, AddressNode defaults, Region region, string? language)
    {
        _data = data;
        _node = node;
        _default = defaults;
        Region = region;
        Language = language;
        _format = new Lazy<AddressFormat>(() => AddressFormat.Parse(node[NodeProperty.Fmt] ?? defaults[NodeProperty.Fmt] ?? "%N%n%O%n%A%n%C"));
        _latinFormat = new Lazy<AddressFormat?>(() => node[NodeProperty.Lfmt] is { } text ? AddressFormat.Parse(text) : null);
    }

    /// <summary>Gets the region.</summary>
    internal Region Region { get; }

    /// <summary>Gets the language variant of the data that was selected, or <see langword="null"/> for the default language.</summary>
    internal string? Language { get; }

    /// <summary>Gets the country's data node.</summary>
    internal AddressNode Node => _node;

    /// <summary>Gets the format in the country's own script.</summary>
    internal AddressFormat Format => _format.Value;

    /// <summary>Gets the format in Latin script, or <see langword="null"/> when the country publishes none.</summary>
    internal AddressFormat? LatinFormat => _latinFormat.Value;

    /// <summary>Gets the letters of the required fields (<c>ACSZ</c>).</summary>
    internal string Require => _node[NodeProperty.Require] ?? _default[NodeProperty.Require] ?? string.Empty;

    /// <summary>Gets the letters of the fields shown in upper case on mail (<c>CS</c>).</summary>
    internal string Upper => _node[NodeProperty.Upper] ?? _default[NodeProperty.Upper] ?? string.Empty;

    /// <summary>Gets a compiled pattern of this country's data, caching it.</summary>
    /// <param name="pattern">The pattern text.</param>
    /// <returns>The compiled pattern, or <see langword="null"/> when it is outside the supported dialect.</returns>
    internal static PostalPattern? Pattern(string pattern) => AddressDirectory.GetPattern(pattern);

    /// <summary>Finds the subdivision of a node that a text names: by key, by name or by Latin name, ignoring case.</summary>
    /// <param name="parent">The node whose subdivisions are searched.</param>
    /// <param name="text">The text.</param>
    /// <returns>The subdivision's node, or <see langword="null"/> when the parent has none by that name.</returns>
    internal AddressNode? FindChild(AddressNode parent, string text)
    {
        string[] keys = parent.List(NodeProperty.SubKeys);
        string[] names = parent.List(NodeProperty.SubNames);
        string[] latinNames = parent.List(NodeProperty.SubLnames);
        for (int i = 0; i < keys.Length; i++)
        {
            if (string.Equals(keys[i], text, StringComparison.OrdinalIgnoreCase)
                || (i < names.Length && string.Equals(names[i], text, StringComparison.OrdinalIgnoreCase))
                || (i < latinNames.Length && string.Equals(latinNames[i], text, StringComparison.OrdinalIgnoreCase)))
            {
                return _data.Find(ChildId(parent, keys[i])) ?? _data.Find(ChildId(parent, keys[i], withLanguage: false));
            }
        }

        return null;
    }

    /// <summary>Tells whether a node lists subdivisions at all.</summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> when it has <c>sub_keys</c>.</returns>
    internal static bool HasChildren(AddressNode node) => node[NodeProperty.SubKeys] is { Length: > 0 };

    private static string ChildId(AddressNode parent, string key, bool withLanguage = true)
    {
        string id = parent.Id;
        int variant = id.IndexOf("--", StringComparison.Ordinal);
        string baseId = variant >= 0 ? id[..variant] : id;
        string suffix = withLanguage && variant >= 0 ? id[variant..] : string.Empty;
        return string.Concat(baseId, "/", key, suffix);
    }
}

/// <summary>Looks up countries in the compiled address data.</summary>
internal static class AddressDirectory
{
    private static readonly ConcurrentDictionary<string, PostalPattern?> Shared = new(StringComparer.Ordinal);

    /// <summary>Finds a country's structure.</summary>
    /// <param name="data">The data.</param>
    /// <param name="region">The country.</param>
    /// <param name="language">A BCP 47 language tag selecting a language variant of the data when one exists; otherwise ignored.</param>
    /// <returns>The structure, or <see langword="null"/> when the data does not describe the country.</returns>
    internal static CountryStructure? Find(AddressDataSet data, Region region, string? language)
    {
        if (region.IsUnknown || !data.Countries.Contains(region.Code))
        {
            return null;
        }

        string key = "data/" + region.Code;
        AddressNode? node = null;
        string? used = null;
        if (!string.IsNullOrEmpty(language))
        {
            string tag = LanguageSubtag(language);
            node = data.Find(key + "--" + tag);
            used = node is null ? null : tag;
        }

        node ??= data.Find(key);
        AddressNode? defaults = data.Find("data/ZZ");
        if (node is null || defaults is null)
        {
            return null;
        }

        return new CountryStructure(data, node, defaults, region, used);
    }

    /// <summary>Gets the structure to use for a country the data does not describe: only the default node's rules apply.</summary>
    /// <param name="data">The data.</param>
    /// <returns>The default node, or <see langword="null"/> when the data has none.</returns>
    internal static AddressNode? Default(AddressDataSet data) => data.Find("data/ZZ");

    /// <summary>Compiles a pattern once and keeps it.</summary>
    /// <param name="pattern">The pattern text.</param>
    /// <returns>The compiled pattern, or <see langword="null"/> when it is outside the supported dialect.</returns>
    internal static PostalPattern? GetPattern(string pattern)
    {
        return Shared.GetOrAdd(pattern, static p =>
        {
            try
            {
                return PostalPattern.Compile(p);
            }
            catch (FormatException)
            {
                return null;
            }
        });
    }

    /// <summary>Takes the language part of a tag: <c>fr-CA</c> gives <c>fr</c>, <c>zh-Hant</c> gives <c>zh-hant</c> (the data writes script variants in lower case).</summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The key suffix to try.</returns>
    internal static string LanguageSubtag(string tag) => tag.Trim().ToLowerInvariant().Replace('_', '-');
}
