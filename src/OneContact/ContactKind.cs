namespace RecursiveAI.OneContact;

/// <summary>The kinds of contact value. Members are only ever added; numbers never change.</summary>
public enum ContactKind
{
    /// <summary>No particular kind: used by reason codes that apply to every kind.</summary>
    Unknown = 0,

    /// <summary>A <see cref="PhoneNumber"/>.</summary>
    Phone = 1,

    /// <summary>An <see cref="EmailAddress"/>.</summary>
    Email = 2,

    /// <summary>A <see cref="WebAddress"/>.</summary>
    Web = 3,

    /// <summary>A <see cref="PostalAddress"/>.</summary>
    Address = 4,

    /// <summary>A <see cref="PersonName"/>.</summary>
    Person = 5,

    /// <summary>A <see cref="CompanyName"/>.</summary>
    Company = 6,
}
