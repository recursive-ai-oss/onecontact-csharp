namespace RecursiveAI.OneContact;

/// <summary>Facts found about a <see cref="PersonName"/>. Flags report; they never change the value.</summary>
[Flags]
public enum NameFacts : uint
{
    /// <summary>No flags.</summary>
    None = 0,

    /// <summary>The input was all upper case or all lower case and its casing was restored.</summary>
    CasingRestored = 1,

    /// <summary>The name has a title.</summary>
    HasPrefix = 2,

    /// <summary>The name has a generational or professional suffix.</summary>
    HasSuffix = 4,

    /// <summary>The name has a nickname in quotes or parentheses.</summary>
    HasNickname = 8,

    /// <summary>The family name has a particle ("van der", "de la").</summary>
    HasParticle = 16,

    /// <summary>The name has a second family name.</summary>
    HasSecondFamily = 32,

    /// <summary>The input wrote the family name first ("Smith, John" or a Hungarian name).</summary>
    FamilyFirstInput = 64,

    /// <summary>The name is a single word, taken as a given name.</summary>
    SingleWord = 128,
}
