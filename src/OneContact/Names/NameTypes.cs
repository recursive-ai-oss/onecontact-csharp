namespace RecursiveAI.OneContact.Names;

/// <summary>What a name string is, as far as rules can tell.</summary>
public enum NameClass
{
    /// <summary>The rules cannot decide (a single bare word, text with digits, too many words, an ambiguous <c>A &amp; B</c>). The parse says why in its issues.</summary>
    Unknown = 0,

    /// <summary>One person.</summary>
    Person = 1,

    /// <summary>A company or other organization: a legal form or a company word was found.</summary>
    Company = 2,

    /// <summary>Several people ("Mr &amp; Mrs John Smith", "John and Jane Smith", names separated by a semicolon or a line break).</summary>
    Multiple = 3,
}

/// <summary>The order the parts of a person's name were written in.</summary>
public enum NameOrder
{
    /// <summary>Given names first, then the family name ("John Smith").</summary>
    GivenFamily = 0,

    /// <summary>The family name, a comma, then the given names ("Smith, John").</summary>
    FamilyCommaGiven = 1,

    /// <summary>The family name first without a comma, because a family-first language or country was given (Hungarian "Kovács János").</summary>
    FamilyGiven = 2,
}

/// <summary>A part of a name: its text (casing restored where the input was all one case) and where it was in the original input.</summary>
public readonly record struct NamePart
{
    private readonly string? _text;

    /// <summary>Creates a part.</summary>
    /// <param name="text">The text of the part.</param>
    /// <param name="span">Where the part is in the original input, in UTF-16 code units.</param>
    public NamePart(string text, SourceSpan span)
    {
        _text = text;
        Span = span;
    }

    /// <summary>Gets the text of the part; an empty string when the name has no such part.</summary>
    public string Text => _text ?? string.Empty;

    /// <summary>Gets where the part is in the original input, in UTF-16 code units; empty when absent.</summary>
    public SourceSpan Span { get; }

    /// <summary>Gets a value indicating whether the part is absent.</summary>
    public bool IsEmpty => string.IsNullOrEmpty(_text);

    /// <summary>Returns <see cref="Text"/>.</summary>
    /// <returns>The text; an empty string for an absent part.</returns>
    public override string ToString() => Text;
}
