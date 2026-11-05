namespace RecursiveAI.OneContact;

/// <summary>Facts found about a <see cref="PersonName"/>. Flags report; they never change the value.</summary>
[Flags]
public enum NameFacts : uint
{
    /// <summary>No flags.</summary>
    None = 0,
}
