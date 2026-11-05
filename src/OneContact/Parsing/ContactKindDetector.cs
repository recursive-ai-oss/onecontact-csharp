namespace RecursiveAI.OneContact;

// Decides which kind of value a string most likely is, for Contacts.Normalize. Cheap shape tests only; the per-kind parser makes
// the real decision and may still refuse. PLACEHOLDER rules for the kinds that exist today; addresses and names are not detected yet.
internal static class ContactKindDetector
{
    internal static ContactKind Detect(ReadOnlySpan<char> text)
    {
        ReadOnlySpan<char> t = text.Trim();
        if (t.IsEmpty)
        {
            return ContactKind.Unknown;
        }

        if (t.Contains('@'))
        {
            return ContactKind.Email;
        }

        if (t.Contains("://", StringComparison.Ordinal) || t.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            return ContactKind.Web;
        }

        if (LooksLikePhone(t))
        {
            return ContactKind.Phone;
        }

        return ContactKind.Unknown;
    }

    // Digits with the punctuation phone numbers are written with, at least five digits, and no letters, except that an
    // extension marker ("ext", "ext.", "extn", "x", ";ext=", "#") may follow with its digits.
    private static bool LooksLikePhone(ReadOnlySpan<char> t)
    {
        int digits = 0;
        for (int i = 0; i < t.Length; i++)
        {
            char c = t[i];
            if (char.IsAsciiDigit(c))
            {
                digits++;
            }
            else if (c is not ('+' or ' ' or '-' or '.' or '(' or ')' or '/'))
            {
                return digits >= 5 && IsExtensionTail(t[i..]);
            }
        }

        return digits >= 5;
    }

    private static bool IsExtensionTail(ReadOnlySpan<char> tail)
    {
        ReadOnlySpan<char> rest = tail;
        if (rest.StartsWith(";", StringComparison.Ordinal))
        {
            rest = rest[1..];
        }

        int letters = 0;
        while (!rest.IsEmpty && char.IsAsciiLetter(rest[0]))
        {
            rest = rest[1..];
            letters++;
        }

        if (letters > 4 || (letters == 0 && !tail.StartsWith("#", StringComparison.Ordinal)))
        {
            return false;
        }

        int digits = 0;
        foreach (char c in rest)
        {
            if (char.IsAsciiDigit(c))
            {
                digits++;
            }
            else if (c is not ('.' or ' ' or '=' or '#' or ':'))
            {
                return false;
            }
        }

        return digits > 0;
    }
}
