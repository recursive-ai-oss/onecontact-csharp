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
        _format = new Lazy<AddressFormat>(() => ParseFormat(node[NodeProperty.Fmt] ?? defaults[NodeProperty.Fmt] ?? "%N%n%O%n%A%n%C"));
        _latinFormat = new Lazy<AddressFormat?>(() => node[NodeProperty.Lfmt] is { } text ? ParseFormat(text) : null);
    }

    // A format string comes from the loaded tables, so a malformed one is bad table data, not a caller error.
    private static AddressFormat ParseFormat(string text)
    {
        try
        {
            return AddressFormat.Parse(text);
        }
        catch (FormatException e)
        {
            throw new InvalidDataException("The address tables hold a malformed address format.", e);
        }
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

    /// <summary>Tells whether a text names one of the subdivisions a node lists: by key, by name or by Latin name, ignoring case.</summary>
    /// <param name="parent">The node whose subdivisions are searched.</param>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when the text names a subdivision.</returns>
    internal static bool IsKnown(AddressNode parent, string text)
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
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Finds the data node of the subdivision a text leads on to: the one whose key the text equals exactly (case-sensitively), or
    /// else the one whose Latin name it equals ignoring case. Local names (<c>sub_names</c>) do not lead on to a node.
    /// </summary>
    /// <param name="parent">The node whose subdivisions are searched.</param>
    /// <param name="text">The text.</param>
    /// <returns>The subdivision's node, or <see langword="null"/> when the text does not select one or the data has no node for it.</returns>
    internal AddressNode? FindChildByKey(AddressNode parent, string text)
    {
        string[] keys = parent.List(NodeProperty.SubKeys);
        int index = Array.FindIndex(keys, key => string.Equals(key, text, StringComparison.Ordinal));
        if (index < 0)
        {
            string[] latinNames = parent.List(NodeProperty.SubLnames);
            index = Array.FindIndex(latinNames, name => string.Equals(name, text, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || index >= keys.Length)
            {
                return null;
            }
        }

        string match = keys[index];
        return _data.Find(ChildId(parent, match)) ?? _data.Find(ChildId(parent, match, withLanguage: false));
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
            // The variant is named exactly as the data names it: a tag is matched as written (the public API lower-cases tags first).
            node = data.Find(key + "--" + language);
            used = node is null ? null : language;
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
}
