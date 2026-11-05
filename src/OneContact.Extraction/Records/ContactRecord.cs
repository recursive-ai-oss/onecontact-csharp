namespace RecursiveAI.OneContact.Extraction.Records;

/// <summary>
/// One value read from a page: where it came from, exactly which bytes it is, and what the core library makes of it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Raw"/> is the slice of the original input at <see cref="Span"/>, as written: character references and JSON escapes are
/// still undecoded, and newlines are normalized to LF (as HTML parsing does). <see cref="Text"/> is the value after decoding, which is
/// what the normalizer was given. For most fields they are equal.
/// </para>
/// <para>
/// A field read twice (the same phone in JSON-LD and in a <c>tel:</c> link of the same record) is kept once; the other sightings are in
/// <see cref="Duplicates"/>, each with its own source and span.
/// </para>
/// </remarks>
public sealed class ExtractedField
{
    internal ExtractedField(
        FieldKind kind,
        FieldSource source,
        string raw,
        string text,
        SourceSpan span,
        bool hasSpan,
        ExtractedFieldFacts flags,
        string? label)
    {
        Kind = kind;
        Source = source;
        Raw = raw;
        Text = text;
        Span = span;
        HasSpan = hasSpan;
        Flags = flags;
        Label = label;
    }

    /// <summary>The position of the field in <see cref="ExtractResult.Fields"/>; unique within a result. Duplicates share the id of their primary field.</summary>
    public int Id { get; internal set; }

    /// <summary>Which field of the record this fills.</summary>
    public FieldKind Kind { get; }

    /// <summary>Which reader found it.</summary>
    public FieldSource Source { get; }

    /// <summary>The source text at <see cref="Span"/>, undecoded.</summary>
    public string Raw { get; }

    /// <summary>The decoded value: character references, JSON escapes and markup removed, whitespace collapsed. Address lines are separated by <c>\n</c>.</summary>
    public string Text { get; }

    /// <summary>
    /// Where <see cref="Raw"/> sits in the original input, in <see cref="ExtractResult.OffsetUnit"/>s: bytes for bytes and documents
    /// parsed from bytes, UTF-16 code units for strings. Empty when <see cref="HasSpan"/> is false.
    /// </summary>
    public SourceSpan Span { get; }

    /// <summary>True when <see cref="Span"/> is known. It is false only for text that has no source position (a block whose script text the parser rewrote).</summary>
    public bool HasSpan { get; }

    /// <summary>
    /// How sure the reader is that this is the field it says, from 0 to 1. Markup that names the property outright counts 1; a link or an
    /// <c>address</c> element, which only suggest it, count less; a value that could not be normalized counts half.
    /// </summary>
    public double Confidence { get; internal set; }

    /// <summary>Facts about how the field was read.</summary>
    public ExtractedFieldFacts Flags { get; internal set; }

    /// <summary>The value normalized by the core library, or the default <see cref="ContactValue"/> when <see cref="ExtractedFieldFacts.Unnormalized"/> is set.</summary>
    public ContactValue Normalized { get; internal set; }

    /// <summary>True when <see cref="Normalized"/> holds a value.</summary>
    public bool IsNormalized => Normalized.Kind != ContactKind.Unknown;

    /// <summary>Why normalization failed (when <see cref="IsNormalized"/> is false) or the first reason the value parser reported; <see cref="ReasonCode.IsNone"/> when there is none.</summary>
    public ReasonCode Issue { get; internal set; }

    /// <summary>The names of the facts the value type reports for <see cref="Normalized"/> (for example <c>FreeProvider</c> on an email address), in declaration order. Empty when there are none.</summary>
    public IReadOnlyList<string> Facts { get; internal set; } = [];

    /// <summary>A label the page gave the value: the link text of a <c>mailto:</c> or <c>tel:</c> link, or the <c>contactType</c> of a <c>ContactPoint</c>. Null when there is none.</summary>
    public string? Label { get; }

    /// <summary>The id of the enclosing <see cref="ContactRecord"/>, or -1 when the markup does not tie the field to a record.</summary>
    public int RecordId { get; internal set; } = -1;

    /// <summary>For a field outside every record: the id of the nearest <c>address</c> element it sits in (the index of that element in the document), or -1. Later grouping can use it. -1 for fields in records.</summary>
    public int BlockId { get; internal set; } = -1;

    /// <summary>The other places the same value was read for the same record, each as its own field. Empty when it was read once.</summary>
    public IReadOnlyList<ExtractedField> Duplicates { get; internal set; } = [];

    internal string DedupKey => IsNormalized ? Normalized.Key : "raw:" + Text.ToLowerInvariant();

    /// <summary>Returns the kind, the source and the text, for debugging.</summary>
    public override string ToString() => $"{Kind} ({Source}): {Text}";
}

/// <summary>
/// Values of one kind on a record that do not fit together: either two different names, or values from different sources whose
/// sets do not overlap at all (a stale JSON-LD phone number against the one in the visible link). Every value is kept; nothing is picked.
/// </summary>
/// <param name="Kind">The kind of field in dispute.</param>
/// <param name="Fields">The disputed fields, in document order: at least two, from at least two sources, or two names.</param>
public sealed record FieldDisagreement(FieldKind Kind, IReadOnlyList<ExtractedField> Fields);

/// <summary>A relation from one record to another, such as a person to the organization they work for.</summary>
/// <param name="Kind">How the target relates to this record.</param>
/// <param name="Target">The other record.</param>
public sealed record RecordLink(RecordLinkKind Kind, ContactRecord Target);

/// <summary>
/// A person or an organization as one page's structured markup describes it, with every field it carries.
/// </summary>
/// <remarks>
/// Records follow the markup's own nesting: a JSON-LD <c>Organization</c> that lists a <c>Person</c> as an <c>employee</c> gives two
/// records linked both ways. The same entity described twice on the page (JSON-LD in the head and microdata in the body) is one record
/// whose fields come from both. Fields are never dropped to resolve a conflict: see <see cref="Disagreements"/>.
/// </remarks>
public sealed class ContactRecord
{
    private readonly List<ExtractedField> _fields;
    private readonly List<RecordLink> _links = [];
    private readonly List<FieldDisagreement> _disagreements = [];

    internal ContactRecord(int id, ContactRecordKind kind, FieldSource source, SourceSpan span, bool hasSpan, List<ExtractedField> fields)
    {
        Id = id;
        Kind = kind;
        Source = source;
        Span = span;
        HasSpan = hasSpan;
        _fields = fields;
    }

    /// <summary>The position of the record in <see cref="ExtractResult.Records"/>.</summary>
    public int Id { get; }

    /// <summary>Whether this is a person or an organization; <see cref="ContactRecordKind.Unknown"/> for fields found together in visible text with nothing that says which.</summary>
    public ContactRecordKind Kind { get; }

    /// <summary>The reader that first found the record. Fields can come from other readers.</summary>
    public FieldSource Source { get; }

    /// <summary>Where the record's markup sits (the JSON object, or the element with its content), in <see cref="ExtractResult.OffsetUnit"/>s. Empty when <see cref="HasSpan"/> is false.</summary>
    public SourceSpan Span { get; }

    /// <summary>True when <see cref="Span"/> is known.</summary>
    public bool HasSpan { get; }

    /// <summary>
    /// How sure the reader is that this is a record of <see cref="Kind"/>: the highest confidence among its name fields, or 0.5 for a
    /// record that has no name.
    /// </summary>
    public double Confidence { get; internal set; }

    /// <summary>Every field of the record, in document order.</summary>
    public IReadOnlyList<ExtractedField> Fields => _fields;

    /// <summary>The name fields: normally one; several when the page gives more than one (see <see cref="Disagreements"/>).</summary>
    public IReadOnlyList<ExtractedField> Names => Of(FieldKind.Name);

    /// <summary>The first name field, or null when the markup gives no name.</summary>
    public ExtractedField? Name => Names.Count > 0 ? Names[0] : null;

    /// <summary>The organizations a person is said to work for, by name.</summary>
    public IReadOnlyList<ExtractedField> Organizations => Of(FieldKind.Organization);

    /// <summary>The telephone numbers.</summary>
    public IReadOnlyList<ExtractedField> Phones => Of(FieldKind.Phone);

    /// <summary>The email addresses.</summary>
    public IReadOnlyList<ExtractedField> Emails => Of(FieldKind.Email);

    /// <summary>The websites.</summary>
    public IReadOnlyList<ExtractedField> Websites => Of(FieldKind.Website);

    /// <summary>The postal addresses.</summary>
    public IReadOnlyList<ExtractedField> Addresses => Of(FieldKind.Address);

    /// <summary>The fields of one kind, in document order.</summary>
    /// <param name="kind">The kind to select.</param>
    /// <returns>The matching fields; empty when there are none.</returns>
    public IReadOnlyList<ExtractedField> Of(FieldKind kind)
    {
        List<ExtractedField>? found = null;
        foreach (ExtractedField f in _fields)
        {
            if (f.Kind == kind)
            {
                (found ??= []).Add(f);
            }
        }

        return found ?? (IReadOnlyList<ExtractedField>)[];
    }

    /// <summary>Links to other records (both directions of each relation).</summary>
    public IReadOnlyList<RecordLink> Links => _links;

    /// <summary>The first organization record this person works for, or null.</summary>
    public ContactRecord? Organization
    {
        get
        {
            foreach (RecordLink l in _links)
            {
                if (l.Kind == RecordLinkKind.WorksFor)
                {
                    return l.Target;
                }
            }

            return null;
        }
    }

    /// <summary>Kinds of field on which the record's values disagree. Empty when they all fit.</summary>
    public IReadOnlyList<FieldDisagreement> Disagreements => _disagreements;

    internal void AddLink(RecordLinkKind kind, ContactRecord target)
    {
        foreach (RecordLink l in _links)
        {
            if (l.Kind == kind && ReferenceEquals(l.Target, target))
            {
                return;
            }
        }

        _links.Add(new RecordLink(kind, target));
    }

    internal void AddDisagreement(FieldDisagreement d) => _disagreements.Add(d);

    /// <summary>Returns the kind and the first name, for debugging.</summary>
    public override string ToString() => $"{Kind}: {Name?.Text ?? "(unnamed)"}";
}
