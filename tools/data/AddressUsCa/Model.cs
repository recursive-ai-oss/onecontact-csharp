namespace OneContact.Data.AddressUsCa;

/// <summary>
/// One register record reduced to the parts the templates render. Text fields keep the register's spelling; <c>Std</c> fields
/// hold the authority's standard abbreviation looked up in the rule tables, so the renderer can print either form.
/// </summary>
internal sealed class Rec
{
    /// <summary>The register and its own identifier, for example <c>nad:{uuid}</c>: the key of the split and of the per-record random source.</summary>
    internal string Id { get; init; } = string.Empty;

    /// <summary>US or CA.</summary>
    internal string Country { get; init; } = string.Empty;

    /// <summary>State or province code (two letters).</summary>
    internal string Region { get; init; } = string.Empty;

    /// <summary>House number as one token, prefix and suffix letters attached (<c>123</c>, <c>12B</c>, <c>W123N456</c>).</summary>
    internal string HouseNumber { get; init; } = string.Empty;

    /// <summary>A written fraction (<c>1/2</c>), or empty.</summary>
    internal string Fraction { get; init; } = string.Empty;

    internal string PreDirectional { get; init; } = string.Empty;

    internal string StreetName { get; init; } = string.Empty;

    internal string StreetType { get; init; } = string.Empty;

    /// <summary>The standard abbreviation of <see cref="StreetType"/>, or empty.</summary>
    internal string StreetTypeStd { get; init; } = string.Empty;

    internal string PostDirectional { get; init; } = string.Empty;

    /// <summary>Whether the street type is printed before the name (French civic style).</summary>
    internal bool TypeBefore { get; init; }

    /// <summary>Unit number or letters as the register holds them, without a designator.</summary>
    internal string Unit { get; init; } = string.Empty;

    /// <summary>The designator word the register wrote with the unit (<c>APT</c>), or empty when it wrote none.</summary>
    internal string UnitDesignator { get; init; } = string.Empty;

    internal string City { get; init; } = string.Empty;

    /// <summary>Puerto Rico urbanization name (the words after <c>URB</c>), or empty. USPS Publication 28 section 292.</summary>
    internal string Urbanization { get; init; } = string.Empty;

    /// <summary>Five-digit ZIP, or a Canadian postal code without its space, or empty.</summary>
    internal string PostalCode { get; init; } = string.Empty;

    /// <summary>The register's ZIP+4 extension, or empty.</summary>
    internal string Plus4 { get; init; } = string.Empty;

    /// <summary>en, fr or es (Puerto Rico): the language the civic form is written in.</summary>
    internal string Language { get; init; } = "en";
}

/// <summary>A piece of an example's text with the label of the span it belongs to, or none for separators and punctuation.</summary>
internal sealed class Piece
{
    internal Piece(string text, string? label)
    {
        Text = text;
        Label = label;
    }

    internal string Text { get; set; }

    internal string? Label { get; }
}

/// <summary>A rendered example: text, token boundaries, labelled spans, and what the generator did (for the manifest counts).</summary>
internal sealed class Example
{
    internal string Id { get; init; } = string.Empty;

    internal string Country { get; init; } = string.Empty;

    internal string Language { get; init; } = string.Empty;

    internal string Layout { get; init; } = string.Empty;

    internal string Text { get; init; } = string.Empty;

    internal List<(int Start, int End)> Tokens { get; init; } = [];

    internal List<(int Start, int End, string Label)> Entities { get; init; } = [];

    internal List<(int Start, int End, string Label)> Fields { get; init; } = [];

    internal List<string> Events { get; init; } = [];
}
