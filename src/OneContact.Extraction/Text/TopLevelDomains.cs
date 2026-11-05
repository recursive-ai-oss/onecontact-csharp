using RecursiveAI.OneContact.Web;

namespace RecursiveAI.OneContact.Extraction.Text;

/// <summary>Asks the Public Suffix List whether a label is a top-level domain, for text that may hold anything.</summary>
internal static class TopLevelDomains
{
    /// <summary>
    /// True when the ICANN section of the list has a rule for the label. Text with a lone surrogate (invalid UTF-16 in a page or a
    /// string) is never a domain, and is refused before the list's IDNA step sees it.
    /// </summary>
    /// <param name="label">The label, in Unicode or ASCII form.</param>
    public static bool IsKnown(string label) => label.Length > 0 && IsWellFormed(label) && PublicSuffixList.IsKnownTopLevel(label);

    private static bool IsWellFormed(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(s[i]))
            {
                return false;
            }
        }

        return true;
    }
}
