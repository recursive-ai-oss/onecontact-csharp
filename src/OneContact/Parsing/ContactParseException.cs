namespace RecursiveAI.OneContact;

/// <summary>
/// Thrown by <c>Parse</c> when the text is not a valid value. It is a <see cref="FormatException"/>, so existing handlers keep
/// working; <see cref="Reason"/> carries the machine-readable cause and <see cref="Span"/> the offending part of the input.
/// </summary>
public class ContactParseException : FormatException
{
    /// <summary>Creates an exception.</summary>
    public ContactParseException()
        : this(ContactKind.Unknown, default, default, "The text is not valid.")
    {
    }

    /// <summary>Creates an exception with a message.</summary>
    /// <param name="message">The message.</param>
    public ContactParseException(string? message)
        : this(ContactKind.Unknown, default, default, message)
    {
    }

    /// <summary>Creates an exception with a message and an inner exception.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public ContactParseException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception.</summary>
    /// <param name="kind">The kind of value being parsed.</param>
    /// <param name="reason">Why parsing failed.</param>
    /// <param name="span">The offending part of the input.</param>
    /// <param name="message">The message.</param>
    public ContactParseException(ContactKind kind, ReasonCode reason, SourceSpan span, string? message)
        : base(message)
    {
        Kind = kind;
        Reason = reason;
        Span = span;
    }

    /// <summary>Gets the kind of value that was being parsed.</summary>
    public ContactKind Kind { get; }

    /// <summary>Gets why parsing failed.</summary>
    public ReasonCode Reason { get; }

    /// <summary>Gets the offending part of the input, in UTF-16 code units.</summary>
    public SourceSpan Span { get; }

    internal static ContactParseException FromDetail(ContactKind kind, ParseDetail detail, int inputLength)
    {
        ReasonCode reason = detail.PrimaryReason;
        SourceSpan span = new(0, inputLength);
        foreach (ParseIssue issue in detail.Issues)
        {
            if (issue.Severity == IssueSeverity.Error)
            {
                span = issue.Span;
                break;
            }
        }

        return new ContactParseException(kind, reason, span, Describe(kind, reason));
    }

    private static string Describe(ContactKind kind, ReasonCode reason) =>
        reason.IsNone
            ? $"The text is not a valid {kind} value."
            : $"The text is not a valid {kind} value: {reason.Description} ({reason.Id}).";
}
