namespace RecursiveAI.OneContact;

/// <summary>
/// Optional context for reading names: a country and a language. Both are only hints that switch rules on (family-first order for
/// Hungarian, two family names for Spanish and Catalan, title words that are ambiguous elsewhere, lower-case particles when casing is
/// restored, the legal forms of one jurisdiction). The current culture never matters.
/// </summary>
/// <param name="Country">The country, or <see cref="Region.Unknown"/>.</param>
/// <param name="Language">A two-letter ISO 639-1 language code in lower case (<c>nl</c>, <c>hu</c>, <c>es</c>), or <see langword="null"/>. Other spellings are folded to lower case.</param>
public readonly record struct NameHints(Region Country, string? Language = null)
{
    /// <summary>Gets hints that name only a country.</summary>
    /// <param name="country">The country.</param>
    /// <returns>The hints.</returns>
    public static NameHints ForCountry(Region country) => new(country, null);

    /// <summary>Gets hints that name only a language.</summary>
    /// <param name="language">A two-letter ISO 639-1 code.</param>
    /// <returns>The hints.</returns>
    public static NameHints ForLanguage(string language) => new(Region.Unknown, language);
}
