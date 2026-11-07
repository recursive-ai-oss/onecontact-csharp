namespace RecursiveAI.OneContact;

/// <summary>How sure the matcher must be that a piece of text is a phone number before it reports it. Each level includes the one before it.</summary>
public enum PhoneMatchLeniency
{
    /// <summary>The text parses as a number whose length is plausible for its region. Finds the most, including strings of digits that only look like numbers.</summary>
    Possible = 0,

    /// <summary>The number is valid for its region, is not glued to letters or currency signs, and carries its national prefix where the region's format needs one.</summary>
    Valid = 1,

    /// <summary>Valid, and the digits are grouped the way the region groups them, or an alternate way it is known to be written: a group may not be split, but separators may be dropped between groups.</summary>
    StrictGrouping = 2,

    /// <summary>Valid, and every group of digits is exactly as one of the region's formats writes it.</summary>
    ExactGrouping = 3,
}
