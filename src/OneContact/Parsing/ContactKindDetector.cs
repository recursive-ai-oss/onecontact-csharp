namespace RecursiveAI.OneContact;

// Decides which kind of value a string most likely is, for Contacts.Normalize. Cheap shape tests only; the per-kind parser makes
// the real decision and may still refuse. PLACEHOLDER rules for the kinds that exist today; names are not detected yet. An address is detected only when the rules read a delivery line and a ZIP or postal code.
internal static class ContactKindDetector
{
    internal static ContactKind Detect(ReadOnlySpan<char> text)
    {
        ReadOnlySpan<char> t = text.Trim();
        if (t.IsEmpty)
        {
            return ContactKind.Unknown;
        }

        // A scheme with // is a web address even when it carries credentials or an @name path.
        if (t.Contains("://", StringComparison.Ordinal))
        {
            return ContactKind.Web;
        }

        if (t.Contains('@'))
        {
            return ContactKind.Email;
        }

        // A scheme with //, a leading www., or a bare domain whose top-level domain is on the Public Suffix List. Mail addresses were
        // taken above (they hold an @), and the TLD test keeps file names, versions and numbers like 555.1234 out.
        if (Web.WebAddressParser.LooksLikeWebAddress(t))
        {
            return ContactKind.Web;
        }

        if (LooksLikePhone(t))
        {
            return ContactKind.Phone;
        }

        // Tried last: it is the most expensive test, and a phone number or web address never reads as a delivery line plus a postal code.
        if (Addresses.Parsing.RuleAddressParser.TryParse(t, Region.Unknown, out Addresses.Parsing.ParsedAddress? address, out _) == System.Buffers.OperationStatus.Done
            && address is { PostalCode: not null, DeliveryLines.Count: > 0 })
        {
            return ContactKind.Address;
        }

        return ContactKind.Unknown;
    }

    // Digits with the punctuation phone numbers are written with, at least five digits, and no letters.
    private static bool LooksLikePhone(ReadOnlySpan<char> t)
    {
        int digits = 0;
        foreach (char c in t)
        {
            if (char.IsAsciiDigit(c))
            {
                digits++;
            }
            else if (c is not ('+' or ' ' or '-' or '.' or '(' or ')' or '/'))
            {
                return false;
            }
        }

        return digits >= 5;
    }
}
