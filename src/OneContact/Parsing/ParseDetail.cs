using System.Collections.ObjectModel;

namespace RecursiveAI.OneContact;

/// <summary>
/// What the low-level parse tells besides the value: the issues found (with reason codes and offsets) and, per field, its offsets,
/// confidence and whether a rule or a model decided it. Immutable.
/// </summary>
public sealed class ParseDetail
{
    private static readonly ReadOnlyCollection<ParseIssue> NoIssues = new([]);
    private static readonly ReadOnlyCollection<ParsedField> NoFields = new([]);

    /// <summary>Creates a detail.</summary>
    /// <param name="issues">The issues; copied.</param>
    /// <param name="fields">The fields; copied.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public ParseDetail(IEnumerable<ParseIssue> issues, IEnumerable<ParsedField> fields)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(fields);
        Issues = new ReadOnlyCollection<ParseIssue>([.. issues]);
        Fields = new ReadOnlyCollection<ParsedField>([.. fields]);
    }

    private ParseDetail()
    {
        Issues = NoIssues;
        Fields = NoFields;
    }

    /// <summary>Gets a detail with no issues and no fields, shared by every parse with nothing to report.</summary>
    public static ParseDetail Empty { get; } = new();

    /// <summary>Gets the issues, in the order found.</summary>
    public IReadOnlyList<ParseIssue> Issues { get; }

    /// <summary>Gets the parsed fields, in input order where they have offsets.</summary>
    public IReadOnlyList<ParsedField> Fields { get; }

    /// <summary>Gets the reason of the first error issue, or the default <see cref="ReasonCode"/> when there is none.</summary>
    public ReasonCode PrimaryReason
    {
        get
        {
            foreach (ParseIssue issue in Issues)
            {
                if (issue.Severity == IssueSeverity.Error)
                {
                    return issue.Reason;
                }
            }

            return default;
        }
    }

    /// <summary>Creates the detail of a failed parse: one error issue.</summary>
    /// <param name="reason">Why it failed.</param>
    /// <param name="span">Where in the input.</param>
    /// <returns>The detail.</returns>
    public static ParseDetail Failure(ReasonCode reason, SourceSpan span) =>
        new([new ParseIssue(reason, span)], []);
}
