namespace RecursiveAI.OneContact;

/// <summary>Who decided a field's value.</summary>
public enum DecisionSource
{
    /// <summary>A rule or table decided it. Rules are certain and a model never overrules them.</summary>
    Rule = 0,

    /// <summary>A trained model decided what the rules left open.</summary>
    Model = 1,
}

/// <summary>One field of a parsed value, with where it came from.</summary>
/// <param name="Name">The field's name, such as <c>locality</c> or <c>family</c>.</param>
/// <param name="Span">Where the field sits in the original input, in UTF-16 code units.</param>
/// <param name="Confidence">From 0 to 1; exactly 1 for rule decisions.</param>
/// <param name="Source">Whether a rule or a model decided it.</param>
public readonly record struct ParsedField(string Name, SourceSpan Span, float Confidence, DecisionSource Source);
