using RecursiveAI.OneContact.Extraction.Records;

namespace RecursiveAI.OneContact.Extraction.Markup;

/// <summary>A half-open range of UTF-16 offsets in the decoded page text. <see cref="Start"/> is negative when there is no position.</summary>
internal readonly record struct TextRange(int Start, int End)
{
    public static TextRange None => new(-1, -1);

    public bool IsPresent => Start >= 0 && End >= Start;

    /// <summary>The smallest range holding both; a missing range is ignored.</summary>
    public static TextRange Hull(TextRange a, TextRange b)
    {
        if (!a.IsPresent)
        {
            return b;
        }

        if (!b.IsPresent)
        {
            return a;
        }

        return new TextRange(Math.Min(a.Start, b.Start), Math.Max(a.End, b.End));
    }
}

/// <summary>
/// What every markup reader produces: a thing with types and properties, in schema.org's vocabulary (the h-card reader maps its
/// property names onto it). Keeping one shape lets one builder turn all of them into records.
/// </summary>
internal sealed class MarkupItem
{
    public MarkupItem(FieldSource source)
    {
        Source = source;
    }

    public FieldSource Source { get; }

    /// <summary>Type names without their vocabulary, for example <c>Person</c> or <c>h-card</c>.</summary>
    public List<string> Types { get; } = [];

    public List<MarkupProperty> Properties { get; } = [];

    /// <summary>JSON-LD <c>@id</c> or microdata <c>itemid</c>; null when there is none.</summary>
    public string? Id { get; set; }

    /// <summary>The source slice of the whole item.</summary>
    public TextRange Range { get; set; } = TextRange.None;

    /// <summary>The element that carries the item; -1 for JSON-LD.</summary>
    public int ElementIndex { get; set; } = -1;

    public bool Hidden { get; set; }
}

/// <summary>One property value: text with its source range, a nested item, or a JSON-LD reference to an item by <c>@id</c>.</summary>
internal sealed class MarkupProperty
{
    public MarkupProperty(string name)
    {
        Name = name;
    }

    public string Name { get; }

    /// <summary>The decoded text (when this is not an item).</summary>
    public string? Text { get; set; }

    /// <summary>The source slice of the value as written.</summary>
    public TextRange Raw { get; set; } = TextRange.None;

    /// <summary>A nested item.</summary>
    public MarkupItem? Item { get; set; }

    /// <summary>A reference to an item elsewhere, by id.</summary>
    public string? Reference { get; set; }

    /// <summary>The element whose value this is (-1 for JSON-LD); used to mark that markup consumed.</summary>
    public int ElementIndex { get; set; } = -1;

    public bool Hidden { get; set; }
}
