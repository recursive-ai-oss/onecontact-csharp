namespace RecursiveAI.OneContact;

/// <summary>Facts found about a <see cref="PhoneNumber"/>. Flags report; they never change the value.</summary>
[Flags]
public enum PhoneFacts : uint
{
    /// <summary>No flags.</summary>
    None = 0,
}
