namespace RecursiveAI.OneContact;

// Shared by the value types' ToString(format) and TryFormat: one place for the format letters, none of it culture-aware.
internal static class ValueFormatting
{
    internal static string Format(string text, string? format, string letter = "C")
    {
        if (!IsDefault(format, letter))
        {
            throw new FormatException($"The format '{format}' is not defined; use null, empty or \"{letter}\".");
        }

        return text;
    }

    internal static bool TryFormat(string text, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, string letter = "C")
    {
        if (!IsDefault(format, letter))
        {
            throw new FormatException($"The format '{format}' is not defined; use empty or \"{letter}\".");
        }

        if (text.TryCopyTo(destination))
        {
            charsWritten = text.Length;
            return true;
        }

        charsWritten = 0;
        return false;
    }

    private static bool IsDefault(ReadOnlySpan<char> format, string letter) =>
        format.IsEmpty || format.Equals(letter, StringComparison.Ordinal);
}
