namespace RecursiveAI.OneContact;

/// <summary>How an address is written.</summary>
public enum AddressStyle
{
    /// <summary>The postal authority's own style: upper case, its abbreviations, no punctuation beyond what the guide keeps (USPS Publication 28 section 212, Canada Post Addressing Guidelines section 2).</summary>
    Postal = 0,

    /// <summary>The same tokens as <see cref="Postal"/> in natural title case, with the authority's abbreviations and no periods.</summary>
    Display = 1,
}

/// <summary>Whether an address is written on several lines or on one.</summary>
public enum AddressLayout
{
    /// <summary>One line per address line, separated by a line feed, as on a mailing label.</summary>
    Label = 0,

    /// <summary>All lines on one line, separated by a comma and a space.</summary>
    SingleLine = 1,
}
