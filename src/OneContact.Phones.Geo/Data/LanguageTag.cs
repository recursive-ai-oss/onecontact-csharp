namespace RecursiveAI.OneContact.Phones.Geo.Data;

/// <summary>
/// The language, script and region of a BCP 47 language tag, as far as the mappers need them. Parsing is lenient the way the reference
/// is: a tag whose first subtag is not a language gives the empty tag (which the mappers treat like English, the data's fallback
/// language), and subtags after the region (variants, extensions) are ignored. Letter case is folded in ordinal ASCII.
/// </summary>
internal readonly record struct LanguageTag(string Language, string Script, string Region)
{
    /// <summary>Gets the empty tag: no language, script or region.</summary>
    internal static LanguageTag Empty { get; } = new(string.Empty, string.Empty, string.Empty);

    /// <summary>Parses a tag.</summary>
    /// <param name="tag">Subtags separated by hyphens. Underscores are not separators, so <c>zh_TW</c> is not a language tag.</param>
    /// <returns>The parts; the empty tag when <paramref name="tag"/> does not start with a language.</returns>
    internal static LanguageTag Parse(string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return Empty;
        }

        string[] parts = tag.Split('-');
        string language = parts[0];
        if (!IsLanguage(language))
        {
            return Empty;
        }

        language = Fold(language, upper: false);
        language = language switch
        {
            // Retired codes still in use: the data and the territory names use the current ones.
            "iw" => "he",
            "in" => "id",
            "ji" => "yi",
            "und" => string.Empty,
            _ => language,
        };

        string script = string.Empty;
        string region = string.Empty;
        int next = 1;
        if (language.Length != 0 && next < parts.Length && parts[next].Length == 4 && IsAllLetters(parts[next]))
        {
            script = string.Concat(Fold(parts[next][..1], upper: true), Fold(parts[next][1..], upper: false));
            next++;
        }

        if (language.Length != 0 && next < parts.Length && ((parts[next].Length == 2 && IsAllLetters(parts[next])) || (parts[next].Length == 3 && parts[next].All(char.IsAsciiDigit))))
        {
            region = Fold(parts[next], upper: true);
        }

        return new LanguageTag(language, script, region);
    }

    private static bool IsLanguage(string s) => (s.Length is 2 or 3 or (>= 5 and <= 8)) && IsAllLetters(s);

    private static bool IsAllLetters(string s) => s.All(char.IsAsciiLetter);

    private static string Fold(string s, bool upper) =>
        string.Create(s.Length, (s, upper), static (span, state) =>
        {
            for (int i = 0; i < span.Length; i++)
            {
                char c = state.s[i];
                span[i] = state.upper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);
            }
        });
}
