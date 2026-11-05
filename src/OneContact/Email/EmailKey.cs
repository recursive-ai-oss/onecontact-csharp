using RecursiveAI.OneContact.Email;
using RecursiveAI.OneContact.Text.Idna;

namespace RecursiveAI.OneContact.Keys;

/// <summary>
/// Computes the comparison key of a <see cref="EmailAddress"/>. The key is the aggressive, versioned form (see <see cref="KeyVersion"/>);
/// every rule of a version is documented in <c>docs/email.md</c> and a published version's output never changes.
/// </summary>
public static class EmailKey
{
    /// <summary>Computes the key of <paramref name="value"/> under key version <paramref name="version"/>.</summary>
    /// <param name="value">The value.</param>
    /// <param name="version">The key version.</param>
    /// <returns>The key; empty for the default value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is not a defined <see cref="KeyVersion"/>.</exception>
    public static string Compute(EmailAddress value, KeyVersion version)
    {
        KeyVersions.ThrowIfUndefined(version);
        if (value.Canonical.Length == 0)
        {
            return string.Empty;
        }

        return ComputeV1(value.LocalPart, value.Domain);
    }

    // Key rules, version 1 (docs/email.md):
    //  1. A local part written as one quoted string that would be a valid unquoted dot-atom loses its quotes (RFC 5321 section 4.1.2:
    //     quoted and unquoted forms of the same string are the same mailbox; quoted-pairs resolve to the character they quote).
    //  2. The local part is lowercased with the invariant culture and composed to NFC.
    //  3. Provider rules, for the exact domains below only.
    //  4. Everything else (unknown domains, domain literals): rules 1 and 2 only. The domain is already lowercase and an A-label.
    private static string ComputeV1(string local, string domain)
    {
        bool quoted = local.Length > 0 && local[0] == '"';
        string key = Unquote(local, out bool unquoted);
        key = Fold(key);
        if (quoted && !unquoted)
        {
            return string.Concat(key, "@", domain);
        }

        switch (domain)
        {
            // Gmail Help "Dots don't matter in Gmail addresses" (support.google.com/mail/answer/7436150): dotted variants are one address.
            // Gmail Help "Send emails from a different address or alias", section "Use Gmail aliases" (support.google.com/mail/answer/22370):
            // everything after a + up to the @ is an alias of the same mailbox. gmail.com and googlemail.com are one service: Gmail Help
            // Community, "Confused email addresses" (support.google.com/mail/thread/378507106).
            case "gmail.com":
            case "googlemail.com":
                key = StripTag(key, '+');
                string undotted = key.Replace(".", string.Empty, StringComparison.Ordinal);
                return string.Concat(undotted.Length > 0 ? undotted : key, "@gmail.com");

            // Microsoft Support "Send email from a different address in Outlook.com" (support.microsoft.com/en-us/outlook/send-email-from-a-different-address-in-outlook-com),
            // section "Using site-specific email addresses in Outlook.com": a +site suffix still reaches the mailbox.
            case "outlook.com":
            case "hotmail.com":
            case "live.com":
                return string.Concat(StripTag(key, '+'), "@", domain);

            // Yahoo Help SLN28338 (help.yahoo.com/kb/SLN28338.html): a disposable address is basename-keyword@yahoo.com and delivers to the
            // account that owns the base name.
            case "yahoo.com":
                return string.Concat(StripTag(key, '-'), "@", domain);

            // Fastmail Help "Plus addressing and subdomain addressing" (www.fastmail.com/help/receive/addressing.html): username+tag@domain
            // and, with one subdomain level, anything@username.domain both reach username@domain.
            case "fastmail.com":
                return string.Concat(StripTag(key, '+'), "@", domain);
        }

        if (domain.EndsWith(".fastmail.com", StringComparison.Ordinal))
        {
            string user = domain[..^".fastmail.com".Length];
            if (user.Length > 0 && !user.Contains('.', StringComparison.Ordinal))
            {
                return string.Concat(user, "@fastmail.com");
            }
        }

        return string.Concat(key, "@", domain);
    }

    // Removes the tag from the first separator on, unless that would leave nothing.
    private static string StripTag(string local, char separator)
    {
        int at = local.IndexOf(separator, StringComparison.Ordinal);
        return at > 0 ? local[..at] : local;
    }

    private static string Fold(string s)
    {
        string lower = s.ToLowerInvariant();
        foreach (char c in lower)
        {
            if (c >= 0x80)
            {
                return ToNfc(lower);
            }
        }

        return lower;
    }

    private static string ToNfc(string s)
    {
        int[] cps = [.. s.EnumerateRunes().Select(r => r.Value)];
        System.Text.StringBuilder sb = new(s.Length);
        foreach (int cp in NormalizationC.Normalize(cps))
        {
            sb.Append(char.ConvertFromUtf32(cp));
        }

        return sb.ToString();
    }

    // Rule 1. Returns the local part without quotes when the whole part is one quoted string whose text is a dot-atom; otherwise the
    // quoted text itself with unquoted = false.
    private static string Unquote(string local, out bool unquoted)
    {
        unquoted = false;
        if (local.Length < 2 || local[0] != '"' || local[^1] != '"')
        {
            return local;
        }

        System.Text.StringBuilder sb = new(local.Length);
        for (int i = 1; i < local.Length - 1; i++)
        {
            char c = local[i];
            if (c == '"')
            {
                return local; // a quote inside: this is several quoted words (an obsolete form), kept as written
            }

            if (c == '\\')
            {
                i++;
                if (i >= local.Length - 1)
                {
                    return local;
                }

                c = local[i];
            }

            sb.Append(c);
        }

        string text = sb.ToString();
        if (IsDotAtom(text))
        {
            unquoted = true;
            return text;
        }

        return local;
    }

    private static bool IsDotAtom(string s)
    {
        if (s.Length == 0 || s[0] == '.' || s[^1] == '.')
        {
            return false;
        }

        char previous = '\0';
        foreach (char c in s)
        {
            bool ok = c >= 0x80 || char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-/=?^_`{|}~".Contains(c, StringComparison.Ordinal) || (c == '.' && previous != '.');
            if (!ok)
            {
                return false;
            }

            previous = c;
        }

        return true;
    }
}
