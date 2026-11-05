namespace RecursiveAI.OneContact;

/// <summary>How serious an issue is.</summary>
public enum IssueSeverity
{
    /// <summary>The input was read, and this is only worth knowing.</summary>
    Info = 0,

    /// <summary>The input was read, but something about it is doubtful.</summary>
    Warning = 1,

    /// <summary>The input could not be read as asked.</summary>
    Error = 2,
}

/// <summary>One thing found while parsing: a reason code, where in the input, and how serious.</summary>
/// <param name="Reason">What the issue is about.</param>
/// <param name="Span">Where in the original input; empty when it concerns the whole input.</param>
/// <param name="Severity">How serious it is.</param>
public readonly record struct ParseIssue(ReasonCode Reason, SourceSpan Span, IssueSeverity Severity = IssueSeverity.Error);
