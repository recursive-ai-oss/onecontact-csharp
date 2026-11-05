namespace RecursiveAI.OneContact.Addresses.Streets;

/// <summary>One word of the input with where it sits in the original text. Offsets are UTF-16 code units into the text the caller passed.</summary>
/// <param name="Text">The word as written, without surrounding punctuation.</param>
/// <param name="Start">The offset of the first code unit.</param>
/// <param name="Length">The number of code units.</param>
internal readonly record struct AddressToken(string Text, int Start, int Length)
{
    /// <summary>Gets the word in upper case with periods removed, the form rule tables are matched against (<c>P.O.</c> is <c>PO</c>).</summary>
    internal string Key => Text.Replace(".", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    /// <summary>Gets where the word is.</summary>
    internal SourceSpan Span => new(Start, Length);

    /// <summary>Splits a stretch of the input into words. White space separates words; a leading <c>#</c> is its own word; commas, semicolons and
    /// periods at the ends of a word are dropped (periods inside a word, as in <c>39.2</c>, stay).</summary>
    /// <param name="text">The whole input.</param>
    /// <param name="start">Where the stretch starts.</param>
    /// <param name="end">Where the stretch ends (exclusive).</param>
    /// <returns>The words in order.</returns>
    internal static List<AddressToken> Split(string text, int start, int end)
    {
        List<AddressToken> tokens = [];
        int i = start;
        while (i < end)
        {
            while (i < end && (char.IsWhiteSpace(text[i]) || text[i] is ',' or ';' or '|'))
            {
                i++;
            }

            int s = i;
            while (i < end && !char.IsWhiteSpace(text[i]) && text[i] is not (',' or ';' or '|'))
            {
                i++;
            }

            int e = i;
            while (e > s && text[e - 1] is '.' or ':' or ')' )
            {
                e--;
            }

            while (s < e && text[s] is '(' or ':')
            {
                s++;
            }

            if (e <= s)
            {
                continue;
            }

            if (text[s] == '#' && e - s > 1)
            {
                tokens.Add(new AddressToken("#", s, 1));
                tokens.Add(new AddressToken(text.Substring(s + 1, e - s - 1), s + 1, e - s - 1));
            }
            else
            {
                tokens.Add(new AddressToken(text.Substring(s, e - s), s, e - s));
            }
        }

        return tokens;
    }
}
