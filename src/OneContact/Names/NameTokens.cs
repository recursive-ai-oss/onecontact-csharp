namespace RecursiveAI.OneContact.Names;

/// <summary>One word of an input text: where it is in the original input, and whether a comma came right before it.</summary>
internal readonly struct NameToken
{
    internal NameToken(int start, int length, bool commaBefore)
    {
        Start = start;
        Length = length;
        CommaBefore = commaBefore;
    }

    internal int Start { get; }

    internal int Length { get; }

    internal int End => Start + Length;

    internal bool CommaBefore { get; }

    internal ReadOnlySpan<char> Text(ReadOnlySpan<char> whole) => whole.Slice(Start, Length);

    internal bool HasLetterOrDigit(ReadOnlySpan<char> whole)
    {
        foreach (char c in Text(whole))
        {
            if (char.IsLetterOrDigit(c))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Splits text into words on white space and commas. Offsets index the text given, which is the original input.</summary>
internal static class NameTokenizer
{
    /// <summary>
    /// Tokenizes <paramref name="whole"/> between <paramref name="start"/> and <paramref name="end"/>. Characters whose entry in
    /// <paramref name="skip"/> is set (nickname text and its delimiters) count as white space.
    /// </summary>
    internal static List<NameToken> Tokenize(ReadOnlySpan<char> whole, int start, int end, ReadOnlySpan<bool> skip)
    {
        List<NameToken> tokens = [];
        bool comma = false;
        int i = start;
        while (i < end)
        {
            char c = whole[i];
            bool skipped = !skip.IsEmpty && skip[i];
            if (char.IsWhiteSpace(c) || skipped)
            {
                i++;
                continue;
            }

            if (c == ',')
            {
                comma = true;
                i++;
                continue;
            }

            int tokenStart = i;
            while (i < end && !char.IsWhiteSpace(whole[i]) && whole[i] != ',' && (skip.IsEmpty || !skip[i]))
            {
                i++;
            }

            tokens.Add(new NameToken(tokenStart, i - tokenStart, comma && tokens.Count > 0));
            comma = false;
        }

        return tokens;
    }
}
