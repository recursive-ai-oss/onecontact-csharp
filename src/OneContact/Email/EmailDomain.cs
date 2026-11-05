using RecursiveAI.OneContact.Text.Idna;

namespace RecursiveAI.OneContact.Email;

/// <summary>Turns the host-name form of an email domain into its canonical, lowercase A-label form.</summary>
internal static class EmailDomain
{
    // RFC 6531 section 3.3 needs U-labels valid under IDNA2008 (RFC 5890), so UTS 46 runs with every strictness flag on. Lengths are
    // reported as warnings by the parser instead of failing here.
    private static readonly Uts46Options IdnaOptions =
        Uts46Options.UseStd3AsciiRules | Uts46Options.CheckHyphens | Uts46Options.CheckBidi | Uts46Options.CheckJoiners;

    /// <summary>Normalizes the labels of a host-name domain (atoms joined by dots, comments and folding already removed).</summary>
    /// <param name="text">The domain as written.</param>
    /// <param name="facts">Receives <see cref="EmailFacts.InternationalizedDomain"/>, <see cref="EmailFacts.SingleLabelDomain"/> and <see cref="EmailFacts.ExceedsLengthLimit"/>.</param>
    /// <param name="ascii">The canonical domain.</param>
    /// <param name="reason">Why it failed, when it did.</param>
    /// <returns><see langword="true"/> when the domain is valid.</returns>
    internal static bool TryNormalize(string text, ref EmailFacts facts, out string ascii, out ReasonCode reason)
    {
        bool needsIdna = false;
        int labelStart = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != '.')
            {
                if (text[i] >= 0x80)
                {
                    needsIdna = true;
                }

                continue;
            }

            // A label that starts with xn-- must be checked as Punycode, whatever its case (UTS 46 section 4, Convert/Validate).
            if (i - labelStart >= 4 && text.AsSpan(labelStart, 4).Equals("xn--", StringComparison.OrdinalIgnoreCase))
            {
                needsIdna = true;
            }

            labelStart = i + 1;
        }

        if (needsIdna)
        {
            if (!Uts46.TryToAscii(text, IdnaOptions, out ascii))
            {
                reason = ReasonCode.EmailDomainIdnaFailed;
                return false;
            }

            facts |= EmailFacts.InternationalizedDomain;
        }
        else
        {
            ascii = text.ToLowerInvariant();
        }

        // RFC 5321 section 4.1.2 sub-domain = Let-dig [Ldh-str]: each label starts and ends with a letter or digit.
        int labels = 0;
        int start = 0;
        for (int i = 0; i <= ascii.Length; i++)
        {
            if (i < ascii.Length && ascii[i] != '.')
            {
                if (!char.IsAsciiLetterOrDigit(ascii[i]) && ascii[i] != '-')
                {
                    reason = ReasonCode.EmailInvalidDomain;
                    return false;
                }

                continue;
            }

            int length = i - start;
            if (length == 0 || ascii[start] == '-' || ascii[i - 1] == '-')
            {
                reason = ReasonCode.EmailInvalidDomain;
                return false;
            }

            if (length > 63)
            {
                facts |= EmailFacts.ExceedsLengthLimit; // RFC 1035 section 2.3.4
            }

            if (i == ascii.Length && IsAllDigits(ascii.AsSpan(start, length)))
            {
                // RFC 3696 section 2: the top-level domain is not all-numeric.
                reason = ReasonCode.EmailNumericTopLevelDomain;
                return false;
            }

            labels++;
            start = i + 1;
        }

        if (labels == 1)
        {
            facts |= EmailFacts.SingleLabelDomain;
        }

        reason = default;
        return true;
    }

    private static bool IsAllDigits(ReadOnlySpan<char> s)
    {
        foreach (char c in s)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Shows a canonical domain with its A-labels as U-labels.</summary>
    internal static string ToUnicode(string domain)
    {
        if (domain.Length == 0 || domain[0] == '[' || !domain.Contains("xn--", StringComparison.Ordinal))
        {
            return domain;
        }

        return Uts46.TryToUnicode(domain, IdnaOptions, out string unicode) ? unicode : domain;
    }
}
