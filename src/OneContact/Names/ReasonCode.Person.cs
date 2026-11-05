namespace RecursiveAI.OneContact;

public readonly partial struct ReasonCode
{
    /// <summary>The text is a company name (a legal form or a company word was found), not a person's.</summary>
    public static ReasonCode PersonNotAPerson { get; } = new(ContactKind.Person, 1, "not-a-person", "the text is a company name, not a person's");

    /// <summary>The text names several people; parse it with <c>NameParsing.Split</c>.</summary>
    public static ReasonCode PersonMultiple { get; } = new(ContactKind.Person, 2, "multiple-people", "the text names several people");

    /// <summary>The text has titles or suffixes but no name.</summary>
    public static ReasonCode PersonNoName { get; } = new(ContactKind.Person, 3, "no-name", "the text has no name, only titles or suffixes");

    /// <summary>The text has digits, an at sign or other characters that do not occur in a person's name.</summary>
    public static ReasonCode PersonUnexpectedCharacters { get; } = new(ContactKind.Person, 4, "unexpected-characters", "the text has characters that do not occur in a person's name");

    /// <summary>The text has more words than a person's name has.</summary>
    public static ReasonCode PersonTooLong { get; } = new(ContactKind.Person, 5, "too-long", "the text has too many words for a person's name");

    /// <summary>Two single words joined by a connector: two people or one company, and the rules cannot tell.</summary>
    public static ReasonCode PersonAmbiguousConnector { get; } = new(ContactKind.Person, 6, "ambiguous-connector", "two words joined by a connector may be two people or a company");

    /// <summary>The name is a single word, taken as a given name (or as the family name after a title).</summary>
    public static ReasonCode PersonSingleWord { get; } = new(ContactKind.Person, 7, "single-word", "the name is a single word");

    /// <summary>The input was all upper case or all lower case and its casing was restored.</summary>
    public static ReasonCode PersonCasingRestored { get; } = new(ContactKind.Person, 8, "casing-restored", "the input was all one case and its casing was restored");

    /// <summary>The words were taken as given names with no way to tell the family name apart from the rest.</summary>
    public static ReasonCode PersonFamilyNameGuessed { get; } = new(ContactKind.Person, 9, "family-name-guessed", "the last word was taken as the family name");

    /// <summary>Nothing but plain words: no title, suffix, nickname or known given name, so nothing proves the text is a person; it was taken as one.</summary>
    public static ReasonCode PersonNoEvidence { get; } = new(ContactKind.Person, 10, "no-evidence", "nothing proves the text is a person's name; it was taken as one because it has no sign of a company");
}
