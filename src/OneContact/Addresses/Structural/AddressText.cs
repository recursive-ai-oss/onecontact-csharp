namespace RecursiveAI.OneContact.Addresses.Structural;

/// <summary>Text rules shared by validation and layout.</summary>
internal static class AddressText
{
    /// <summary>
    /// Trims the characters up to and including the space (U+0000 to U+0020) from both ends and nothing else: the data's rules
    /// treat a field as empty only when it is made of those characters. Other kinds of white space, such as the no-break space,
    /// count as content and are kept.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <returns>The trimmed text.</returns>
    internal static string Trim(string value)
    {
        int start = 0;
        int end = value.Length;
        while (start < end && value[start] <= ' ')
        {
            start++;
        }

        while (end > start && value[end - 1] <= ' ')
        {
            end--;
        }

        return start == 0 && end == value.Length ? value : value[start..end];
    }
}
