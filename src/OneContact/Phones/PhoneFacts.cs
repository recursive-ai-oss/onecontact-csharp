namespace RecursiveAI.OneContact;

/// <summary>Facts found about a <see cref="PhoneNumber"/>. Flags report; they never change the value.</summary>
[Flags]
public enum PhoneFacts : uint
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>The number matches a number pattern of its region (it is valid in the sense of the numbering plan).</summary>
    Valid = 1,

    /// <summary>The text carried an extension.</summary>
    HasExtension = 2,

    /// <summary>The text used letters for digits (three or more letters, as in a vanity number).</summary>
    Vanity = 4,

    /// <summary>The text was written in international form: with a plus sign or the region's international dialling prefix.</summary>
    InternationalInput = 8,

    /// <summary>The number belongs to a non-geographic entity such as the +800 universal free numbers.</summary>
    NonGeographic = 16,
}
