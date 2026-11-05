using RecursiveAI.OneContact.Extraction.Records;

namespace RecursiveAI.OneContact.Extraction.Markup;

/// <summary>The parts of an address the markup gave separately (schema.org <c>PostalAddress</c>, the parts of an h-card address).</summary>
internal sealed class AddressParts
{
    public List<string> StreetLines { get; } = [];

    public string? Locality { get; set; }

    public string? AdminArea { get; set; }

    public string? PostalCode { get; set; }

    /// <summary>The country as the page wrote it: a code or a name.</summary>
    public string? CountryText { get; set; }

    public bool IsEmpty => StreetLines.Count == 0 && Locality is null && AdminArea is null && PostalCode is null && CountryText is null;

    /// <summary>The parts on one line, in the order a reader meets them.</summary>
    public string ToSingleLine()
    {
        var parts = new List<string>(StreetLines);
        foreach (string? p in new[] { Locality, AdminArea, PostalCode, CountryText })
        {
            if (!string.IsNullOrWhiteSpace(p))
            {
                parts.Add(p);
            }
        }

        return string.Join(", ", parts);
    }
}

/// <summary>A value found by a reader, before it is normalized and given an id.</summary>
internal sealed class FieldDraft
{
    public required FieldKind Kind { get; set; }

    public required FieldSource Source { get; init; }

    public required string Text { get; init; }

    public TextRange Raw { get; init; } = TextRange.None;

    public string? Label { get; init; }

    public bool Hidden { get; init; }

    public bool Composed { get; init; }

    /// <summary>The address parts when the markup gave them separately; null for free text.</summary>
    public AddressParts? Parts { get; init; }

    /// <summary>The element the value was read from (links and address elements), -1 otherwise; used to place the field in an enclosing record.</summary>
    public int ElementIndex { get; init; } = -1;

    public int BlockId { get; set; } = -1;

    /// <summary>The value was decoded from an obfuscated form (text read by the visible-text reader).</summary>
    public bool Deobfuscated { get; init; }

    /// <summary>The reader's own confidence; null for the source's default.</summary>
    public double? Confidence { get; init; }

    /// <summary>A phone number found by its shape: kept only when the core parser accepts it as valid.</summary>
    public bool RequireValid { get; init; }

    /// <summary>The line of visible text the value is on (text and the links among it); -1 when it has none.</summary>
    public int Line { get; set; } = -1;

    /// <summary>A name found after a "Company:" style label; it names the organization, and the grouping decides whether that is the record or its employer.</summary>
    public bool CompanyLabel { get; init; }
}

/// <summary>A record under construction.</summary>
internal sealed class RecordDraft
{
    public RecordDraft(ContactRecordKind kind, FieldSource source, TextRange range, int elementIndex)
    {
        Kind = kind;
        Source = source;
        Range = range;
        ElementIndex = elementIndex;
    }

    public ContactRecordKind Kind { get; }

    public FieldSource Source { get; }

    public TextRange Range { get; }

    public int ElementIndex { get; }

    public List<FieldDraft> Fields { get; } = [];

    public List<(RecordLinkKind Kind, RecordDraft Target)> Links { get; } = [];
}
