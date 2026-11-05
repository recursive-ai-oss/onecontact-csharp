namespace RecursiveAI.OneContact;

/// <summary>Facts found about an <see cref="EmailAddress"/>. Flags report; they never change the value.</summary>
[Flags]
public enum EmailFacts : uint
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>The local part names a role (<c>info</c>, <c>sales</c>) rather than a person.</summary>
    RoleAccount = 1,

    /// <summary>The domain is a free mailbox provider.</summary>
    FreeProvider = 2,

    /// <summary>The domain is on the list of disposable-address domains.</summary>
    Disposable = 4,
}
