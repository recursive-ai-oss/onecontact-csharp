using RecursiveAI.OneContact.Extraction.Records;

namespace RecursiveAI.OneContact.Extraction;

/// <summary>What an extraction found on one page: records, every field flat, and what the readers could not read.</summary>
public sealed class ExtractResult
{
    internal ExtractResult(
        ExtractionSources sources,
        OffsetUnit offsetUnit,
        List<ContactRecord> records,
        List<ExtractedField> fields,
        List<ExtractIssue> issues)
    {
        Sources = sources;
        OffsetUnit = offsetUnit;
        Records = records;
        Fields = fields;
        Issues = issues;
    }

    /// <summary>The readers that ran.</summary>
    public ExtractionSources Sources { get; }

    /// <summary>What the offsets in every <see cref="ExtractedField.Span"/> and <see cref="ContactRecord.Span"/> count.</summary>
    public OffsetUnit OffsetUnit { get; }

    /// <summary>The person and organization records, in the order their markup begins.</summary>
    public IReadOnlyList<ContactRecord> Records { get; }

    /// <summary>
    /// Every field, flat, in document order: the fields of all records and the ones the markup does not tie to a record (a
    /// <c>mailto:</c> link in the footer). Fields read more than once appear once; see <see cref="ExtractedField.Duplicates"/>.
    /// </summary>
    public IReadOnlyList<ExtractedField> Fields { get; }

    /// <summary>The fields that belong to no record (<see cref="ExtractedField.RecordId"/> is -1).</summary>
    public IReadOnlyList<ExtractedField> Ungrouped => Fields.Where(f => f.RecordId < 0).ToList();

    /// <summary>Problems the readers met: a malformed JSON-LD block, a budget used up. Empty for a clean page.</summary>
    public IReadOnlyList<ExtractIssue> Issues { get; }

    /// <summary>The flat fields of one kind.</summary>
    /// <param name="kind">The kind to select.</param>
    /// <returns>The matching fields in document order.</returns>
    public IReadOnlyList<ExtractedField> Of(FieldKind kind) => Fields.Where(f => f.Kind == kind).ToList();
}
