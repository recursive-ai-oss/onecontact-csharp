namespace RecursiveAI.OneContact;

/// <summary>Facts found about a <see cref="PostalAddress"/>. Flags report; they never change the value.</summary>
[Flags]
public enum AddressFacts : uint
{
    /// <summary>No flags.</summary>
    None = 0,
}
