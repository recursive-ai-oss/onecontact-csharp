namespace RecursiveAI.OneContact;

/// <summary>
/// A range of UTF-16 code units in the input text a value, field or issue refers to. Offsets always index the original input,
/// never a normalized copy.
/// </summary>
/// <param name="Start">The zero-based index of the first code unit.</param>
/// <param name="Length">The number of code units; zero for an empty or unknown range.</param>
public readonly record struct SourceSpan(int Start, int Length)
{
    /// <summary>Gets the index just past the last code unit.</summary>
    public int End => Start + Length;

    /// <summary>Gets a value indicating whether the range holds no code units.</summary>
    public bool IsEmpty => Length == 0;
}
