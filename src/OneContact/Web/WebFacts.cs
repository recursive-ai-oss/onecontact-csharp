namespace RecursiveAI.OneContact;

/// <summary>Facts found about a <see cref="WebAddress"/>. Flags report; they never change the value.</summary>
[Flags]
public enum WebFacts : uint
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>The input had no scheme, and <c>https</c> was assumed.</summary>
    SchemeAssumed = 1,
}
