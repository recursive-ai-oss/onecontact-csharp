namespace RecursiveAI.OneContact;

/// <summary>Facts found about a <see cref="CompanyName"/>. Flags report; they never change the value.</summary>
[Flags]
public enum CompanyFacts : uint
{
    /// <summary>No flags.</summary>
    None = 0,
}
