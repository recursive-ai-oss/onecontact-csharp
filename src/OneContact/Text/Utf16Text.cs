namespace RecursiveAI.OneContact.Text;

/// <summary>Checks of UTF-16 text that the BCL does not offer in one call.</summary>
internal static class Utf16Text
{
    /// <summary>Tells whether every surrogate in the text is half of a high-then-low pair. Noncharacters such as U+FFFE are scalar values and count as well formed.</summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when the text can be encoded as UTF-8 without loss.</returns>
    internal static bool IsWellFormed(ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (!char.IsSurrogate(c))
            {
                continue;
            }

            if (!char.IsHighSurrogate(c) || i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
            {
                return false;
            }

            i++;
        }

        return true;
    }
}
