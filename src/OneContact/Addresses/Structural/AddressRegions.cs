using RecursiveAI.OneContact.Addresses.Structural;

namespace RecursiveAI.OneContact.Addresses;

/// <summary>
/// One subdivision of a country as the address data lists it, at the level of the administrative area: a state, province,
/// prefecture, island or similar.
/// </summary>
/// <param name="Key">The key the data uses; usually the abbreviation or the local name (<c>CA</c>, <c>Bayern</c>).</param>
/// <param name="Name">The full local name, or <see langword="null"/> when the data gives none beyond the key.</param>
/// <param name="LatinName">The name in Latin script, or <see langword="null"/> when the data gives none.</param>
/// <param name="PostalCodePrefix">A pattern that the start of the postal codes of this subdivision matches, or <see langword="null"/> when the data gives none. The pattern uses the regular expression dialect described in docs/address-structural.md.</param>
public sealed record AddressSubdivision(string Key, string? Name, string? LatinName, string? PostalCodePrefix);

/// <summary>What the address data says about the structure of one country's addresses. Immutable.</summary>
public sealed class AddressRegionInfo
{
    private readonly CountryStructure _structure;
    private readonly Lazy<List<AddressSubdivision>> _subdivisions;

    internal AddressRegionInfo(CountryStructure structure)
    {
        _structure = structure;
        _subdivisions = new Lazy<List<AddressSubdivision>>(Build, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Gets the region.</summary>
    public Region Region => _structure.Region;

    /// <summary>Gets the English name of the country in capitals, as the data writes it (<c>UNITED STATES</c>); empty when the data gives none.</summary>
    public string Name => _structure.Node[NodeProperty.Name] ?? string.Empty;

    /// <summary>Gets how much the library knows about the country's addresses.</summary>
    public AddressCoverage Coverage => AddressCoverageTable.For(Region);

    /// <summary>Gets the BCP 47 tag of the country's main language, or <see langword="null"/> when the data gives none.</summary>
    public string? DefaultLanguage => _structure.Node[NodeProperty.Lang];

    /// <summary>Gets the tags of the country's official languages (the data's list); empty when the data gives none.</summary>
    public IReadOnlyList<string> Languages => _structure.Node.List(NodeProperty.Languages);

    /// <summary>Gets the fields in the order the country writes them, first line first, in its own script.</summary>
    public IReadOnlyList<AddressField> FieldOrder => _structure.Format.FieldOrder;

    /// <summary>Gets the fields in the order of the country's Latin-script layout; empty when the country has none.</summary>
    public IReadOnlyList<AddressField> LatinFieldOrder => _structure.LatinFormat?.FieldOrder ?? [];

    /// <summary>Gets the fields an address of this country must have, besides the country itself.</summary>
    public IReadOnlyList<AddressField> RequiredFields => FieldsOf(_structure.Require);

    /// <summary>Gets the fields the country's postal service writes in capitals.</summary>
    public IReadOnlyList<AddressField> UpperCaseFields => FieldsOf(_structure.Upper);

    /// <summary>Gets the pattern every postal code of the country matches, or <see langword="null"/> when the country has no postal codes or the data gives no pattern. The pattern uses the regular expression dialect described in docs/address-structural.md and matches a whole code.</summary>
    public string? PostalCodePattern => _structure.Node[NodeProperty.Zip];

    /// <summary>Gets example postal codes.</summary>
    public IReadOnlyList<string> PostalCodeExamples =>
        _structure.Node[NodeProperty.Zipex] is { Length: > 0 } text ? text.Split(',') : [];

    /// <summary>Gets what the country calls its administrative area: <c>state</c>, <c>province</c>, <c>prefecture</c> and so on (<c>province</c> when the data does not say).</summary>
    public string AdminAreaType => _structure.Node[NodeProperty.StateNameType] ?? "province";

    /// <summary>Gets what the country calls its locality: <c>city</c>, <c>post_town</c>, <c>district</c> and so on (<c>city</c> when the data does not say).</summary>
    public string LocalityType => _structure.Node[NodeProperty.LocalityNameType] ?? "city";

    /// <summary>Gets what the country calls its dependent locality: <c>suburb</c>, <c>district</c>, <c>neighborhood</c> and so on (<c>suburb</c> when the data does not say).</summary>
    public string DependentLocalityType => _structure.Node[NodeProperty.SublocalityNameType] ?? "suburb";

    /// <summary>Gets what the country calls its postal code: <c>postal</c>, <c>zip</c> or <c>pin</c> (<c>postal</c> when the data does not say).</summary>
    public string PostalCodeType => _structure.Node[NodeProperty.ZipNameType] ?? "postal";

    /// <summary>Gets the subdivisions at the administrative area level, in the data's order; empty when the data lists none (any value is accepted for the field then).</summary>
    public IReadOnlyList<AddressSubdivision> Subdivisions => _subdivisions.Value;

    /// <summary>
    /// Finds the subdivision a text names: its key, its name or its Latin name, ignoring case and surrounding white space (the
    /// characters up to U+0020).
    /// </summary>
    /// <param name="text">The text, such as <c>ca</c>, <c>California</c> or <c>Bayern</c>.</param>
    /// <param name="subdivision">The subdivision when found; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the text names a subdivision.</returns>
    public bool TryMatchSubdivision(string? text, out AddressSubdivision? subdivision)
    {
        subdivision = null;
        if (text is null)
        {
            return false;
        }

        string wanted = AddressText.Trim(text);
        if (wanted.Length == 0)
        {
            return false;
        }

        foreach (AddressSubdivision candidate in Subdivisions)
        {
            if (string.Equals(candidate.Key, wanted, StringComparison.OrdinalIgnoreCase)
                || (candidate.Name is not null && string.Equals(candidate.Name, wanted, StringComparison.OrdinalIgnoreCase))
                || (candidate.LatinName is not null && string.Equals(candidate.LatinName, wanted, StringComparison.OrdinalIgnoreCase)))
            {
                subdivision = candidate;
                return true;
            }
        }

        return false;
    }

    private static List<AddressField> FieldsOf(string letters)
    {
        List<AddressField> fields = [];
        foreach (char letter in letters)
        {
            if (AddressFormat.TryField(letter, out AddressField field) && !fields.Contains(field))
            {
                fields.Add(field);
            }
        }

        return fields;
    }

    private List<AddressSubdivision> Build()
    {
        AddressNode node = _structure.Node;
        string[] keys = node.List(NodeProperty.SubKeys);
        string[] names = node.List(NodeProperty.SubNames);
        string[] latinNames = node.List(NodeProperty.SubLnames);
        List<AddressSubdivision> result = new(keys.Length);
        for (int i = 0; i < keys.Length; i++)
        {
            AddressNode? child = _structure.FindChildByKey(node, keys[i]);
            result.Add(new AddressSubdivision(
                keys[i],
                i < names.Length ? names[i] : null,
                i < latinNames.Length ? latinNames[i] : null,
                child?[NodeProperty.Zip]));
        }

        return result;
    }
}

/// <summary>The countries the address data describes and how to find them by name.</summary>
public static class AddressRegions
{
    private static readonly Lazy<Region[]> AllRegions = new(
        () => [.. AddressDataSet.Embedded.Countries.OrderBy(c => c, StringComparer.Ordinal).Select(Region.Parse)],
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Gets every country the address data describes, in code order. Each is at least structurally covered.</summary>
    public static IReadOnlyList<Region> All => AllRegions.Value;

    /// <summary>Gets what the address data says about a country.</summary>
    /// <param name="region">The country.</param>
    /// <param name="info">The information when the data describes the country; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the data describes the country.</returns>
    public static bool TryGet(Region region, out AddressRegionInfo? info)
    {
        CountryStructure? structure = AddressDirectory.Find(AddressDataSet.Embedded, region, language: null);
        info = structure is null ? null : new AddressRegionInfo(structure);
        return info is not null;
    }

    /// <summary>
    /// Finds a country by its two-letter code or its English name as the data writes it, ignoring case and surrounding white space
    /// (the characters up to U+0020). Names are matched whole: <c>united states</c> finds the United States and <c>united</c> finds nothing.
    /// </summary>
    /// <param name="text">The code or name.</param>
    /// <param name="region">The country when found; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the text names a country the data describes.</returns>
    public static bool TryFindByName(string? text, out Region region)
    {
        region = default;
        if (text is null)
        {
            return false;
        }

        string wanted = AddressText.Trim(text);
        if (wanted.Length == 0)
        {
            return false;
        }

        AddressDataSet data = AddressDataSet.Embedded;
        if (Region.TryParse(wanted, out Region byCode) && data.Countries.Contains(byCode.Code))
        {
            region = byCode;
            return true;
        }

        foreach (Region candidate in All)
        {
            AddressNode? node = data.Find("data/" + candidate.Code);
            if (node?[NodeProperty.Name] is { } name && string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                region = candidate;
                return true;
            }
        }

        return false;
    }
}
