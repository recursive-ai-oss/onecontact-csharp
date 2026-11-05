namespace RecursiveAI.OneContact.Addresses.Structural;

/// <summary>The properties a node of the address data can carry. The names are the ones the data uses.</summary>
internal enum NodeProperty
{
    Key,
    Name,
    Lname,
    Lfname,
    Lang,
    Languages,
    Fmt,
    Lfmt,
    Require,
    Xrequire,
    Upper,
    Zip,
    Xzip,
    Zipex,
    Posturl,
    Postprefix,
    ZipNameType,
    StateNameType,
    LocalityNameType,
    SublocalityNameType,
    Isoid,
    SubKeys,
    SubNames,
    SubLnames,
    SubLfnames,
    SubZips,
    SubXzips,
    SubZipexs,
    SubMores,
    SubIsoids,
    SubXrequires,
    Countries,
    WidthOverrides,
}

/// <summary>One node of the address data tree: its full key and the properties it has. Immutable.</summary>
internal sealed class AddressNode
{
    /// <summary>The property names in <see cref="NodeProperty"/> order, as the data spells them.</summary>
    internal static readonly string[] PropertyNames =
    [
        "key", "name", "lname", "lfname", "lang", "languages", "fmt", "lfmt", "require", "xrequire", "upper", "zip", "xzip",
        "zipex", "posturl", "postprefix", "zip_name_type", "state_name_type", "locality_name_type", "sublocality_name_type",
        "isoid", "sub_keys", "sub_names", "sub_lnames", "sub_lfnames", "sub_zips", "sub_xzips", "sub_zipexs", "sub_mores",
        "sub_isoids", "sub_xrequires", "countries", "width_overrides",
    ];

    private readonly string?[] _values;

    internal AddressNode(string id, string?[] values)
    {
        Id = id;
        _values = values;
    }

    /// <summary>Gets the full key, such as <c>data/US/CA</c>.</summary>
    internal string Id { get; }

    /// <summary>Gets a property, or <see langword="null"/> when the node does not have it.</summary>
    /// <param name="property">The property.</param>
    /// <returns>The raw value.</returns>
    internal string? this[NodeProperty property] => _values[(int)property];

    /// <summary>Splits a <c>~</c> separated list property into its items; empty when the node does not have it.</summary>
    /// <param name="property">The property.</param>
    /// <returns>The items.</returns>
    internal string[] List(NodeProperty property) => _values[(int)property] is { } text ? text.Split('~') : [];
}
