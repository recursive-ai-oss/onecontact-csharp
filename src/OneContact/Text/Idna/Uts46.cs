using System.Text;

namespace RecursiveAI.OneContact.Text.Idna;

/// <summary>The flags of UTS #46 section 4 processing.</summary>
[Flags]
internal enum Uts46Options
{
    None = 0,
    CheckHyphens = 1,
    CheckBidi = 2,
    CheckJoiners = 4,
    UseStd3AsciiRules = 8,
    VerifyDnsLength = 16,
    TransitionalProcessing = 32,
    IgnoreInvalidPunycode = 64,
}

/// <summary>
/// Unicode IDNA Compatibility Processing (UTS #46 section 4): map, normalize, break into labels, convert and validate each label,
/// and ToASCII. Errors are recorded and reported as a single failure; the callers (the URL host parser) need no more than that.
/// </summary>
internal static class Uts46
{
    /// <summary>Longest input, in UTF-16 code units, that processing accepts.</summary>
    internal const int MaxDomainLength = 4096;

    /// <summary>ToASCII (UTS #46 section 4.2).</summary>
    /// <param name="domain">The domain name.</param>
    /// <param name="options">The flags.</param>
    /// <param name="result">The ASCII domain; unspecified when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="false"/> when an error was recorded.</returns>
    internal static bool TryToAscii(string domain, Uts46Options options, out string result)
    {
        bool ok = Process(domain, options, out List<int[]> labels);
        var sb = new StringBuilder();
        for (int i = 0; i < labels.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('.');
            }

            int[] label = labels[i];
            bool ascii = true;
            foreach (int cp in label)
            {
                if (cp >= 0x80)
                {
                    ascii = false;
                    break;
                }
            }

            if (ascii)
            {
                foreach (int cp in label)
                {
                    sb.Append((char)cp);
                }
            }
            else if (Punycode.TryEncode(label, out string encoded))
            {
                sb.Append("xn--").Append(encoded);
            }
            else
            {
                ok = false;
            }
        }

        result = sb.ToString();
        if ((options & Uts46Options.VerifyDnsLength) != 0)
        {
            // UTS #46 section 4.2 step 4: 1 to 253 octets and every label 1 to 63; an empty root label is not allowed.
            if (result.Length is < 1 or > 253)
            {
                ok = false;
            }

            foreach (string part in result.Split('.'))
            {
                if (part.Length is < 1 or > 63)
                {
                    ok = false;
                }
            }
        }

        return ok;
    }

    /// <summary>ToUnicode (UTS #46 section 4.3).</summary>
    /// <param name="domain">The domain name.</param>
    /// <param name="options">The flags.</param>
    /// <param name="result">The Unicode domain.</param>
    /// <returns><see langword="false"/> when an error was recorded.</returns>
    internal static bool TryToUnicode(string domain, Uts46Options options, out string result)
    {
        bool ok = Process(domain, options, out List<int[]> labels);
        var sb = new StringBuilder();
        for (int i = 0; i < labels.Count; i++)
        {
            if (i > 0)
            {
                sb.Append('.');
            }

            foreach (int cp in labels[i])
            {
                sb.Append(char.ConvertFromUtf32(cp));
            }
        }

        result = sb.ToString();
        return ok;
    }

    // UTS #46 section 4, "Processing": steps 1 to 4. Returns the labels after conversion and whether no error was recorded.
    private static bool Process(string domain, Uts46Options options, out List<int[]> labels)
    {
        bool ok = true;

        // Hardening limit, not part of UTS #46: a domain name is at most 253 octets, so text this long is no host, and refusing it keeps
        // the quadratic steps (Punycode insertion, combining mark ordering) from being used to burn time.
        if (domain.Length > MaxDomainLength)
        {
            labels = [];
            return false;
        }

        bool transitional = (options & Uts46Options.TransitionalProcessing) != 0;

        // Step 1, map.
        var mapped = new List<int>(domain.Length);
        for (int i = 0; i < domain.Length; i++)
        {
            int cp = domain[i];
            if (char.IsHighSurrogate(domain[i]) && i + 1 < domain.Length && char.IsLowSurrogate(domain[i + 1]))
            {
                cp = char.ConvertToUtf32(domain[i], domain[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(domain[i]))
            {
                // A lone surrogate is not a scalar value; it can only be disallowed.
                cp = domain[i];
            }

            IdnaStatus status = IdnaTables.GetStatus(cp, out ReadOnlySpan<char> mapping);
            switch (status)
            {
                case IdnaStatus.Ignored:
                    break;
                case IdnaStatus.Mapped:
                    if (transitional && cp == 0x1E9E)
                    {
                        mapped.Add('s');
                        mapped.Add('s');
                        break;
                    }

                    AppendCodePoints(mapped, mapping);
                    break;
                case IdnaStatus.Deviation:
                    if (transitional)
                    {
                        AppendCodePoints(mapped, mapping);
                    }
                    else
                    {
                        mapped.Add(cp);
                    }

                    break;
                default:
                    // Valid stays; disallowed stays too and is rejected by the validity check after normalization.
                    mapped.Add(cp);
                    break;
            }
        }

        // Step 2, normalize; step 3, break into labels.
        int[] normalized = NormalizationC.Normalize(mapped.ToArray());
        labels = [];
        var current = new List<int>();
        foreach (int cp in normalized)
        {
            if (cp == '.')
            {
                labels.Add([.. current]);
                current.Clear();
            }
            else
            {
                current.Add(cp);
            }
        }

        labels.Add([.. current]);

        // Step 4, convert xn-- labels; validation comes after, because the Bidi rule looks at the whole domain.
        var fromPunycode = new bool[labels.Count];
        for (int i = 0; i < labels.Count; i++)
        {
            int[] label = labels[i];
            if (label.Length >= 4 && label[0] == 'x' && label[1] == 'n' && label[2] == '-' && label[3] == '-')
            {
                fromPunycode[i] = true;
                bool nonAscii = false;
                foreach (int cp in label)
                {
                    nonAscii |= cp > 0x7F;
                }

                if (nonAscii)
                {
                    ok = false;
                    continue;
                }

                char[] rest = new char[label.Length - 4];
                for (int k = 0; k < rest.Length; k++)
                {
                    rest[k] = (char)label[k + 4];
                }

                if (Punycode.TryDecode(rest, out int[] decoded))
                {
                    labels[i] = decoded;
                    label = decoded;
                }
                else if ((options & Uts46Options.IgnoreInvalidPunycode) == 0)
                {
                    ok = false;
                    continue;
                }

                bool allAscii = true;
                foreach (int cp in label)
                {
                    allAscii &= cp <= 0x7F;
                }

                if (label.Length == 0 || allAscii)
                {
                    ok = false;
                }
            }
        }

        bool bidiDomain = false;
        foreach (int[] label in labels)
        {
            foreach (int cp in label)
            {
                BidiClass b = IdnaTables.GetBidiClass(cp);
                if (b is BidiClass.R or BidiClass.AL or BidiClass.AN)
                {
                    bidiDomain = true;
                }
            }
        }

        for (int i = 0; i < labels.Count; i++)
        {
            ok &= ValidateLabel(labels[i], options, fromPunycode[i], bidiDomain);
        }

        return ok;
    }

    private static void AppendCodePoints(List<int> target, ReadOnlySpan<char> text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length)
            {
                target.Add(char.ConvertToUtf32(text[i], text[i + 1]));
                i++;
            }
            else
            {
                target.Add(text[i]);
            }
        }
    }

    // UTS #46 section 4.1, "Validity Criteria". A label that came from Punycode is validated for non-transitional processing.
    private static bool ValidateLabel(int[] label, Uts46Options options, bool fromPunycode, bool bidiDomain)
    {
        if (label.Length == 0)
        {
            return true;
        }

        bool transitional = (options & Uts46Options.TransitionalProcessing) != 0 && !fromPunycode;

        // 1. NFC. Mapping and normalization already did this for labels that were not Punycode.
        if (fromPunycode)
        {
            int[] nfc = NormalizationC.Normalize(label);
            if (!nfc.AsSpan().SequenceEqual(label))
            {
                return false;
            }
        }

        // 2 and 3.
        if ((options & Uts46Options.CheckHyphens) != 0)
        {
            if (label.Length >= 4 && label[2] == '-' && label[3] == '-')
            {
                return false;
            }

            if (label[0] == '-' || label[^1] == '-')
            {
                return false;
            }
        }
        else if (label.Length >= 4 && label[0] == 'x' && label[1] == 'n' && label[2] == '-' && label[3] == '-')
        {
            // 4. Without CheckHyphens a label must not begin with "xn--" (only a label decoded from Punycode can still do so).
            return false;
        }

        // 5 and 6.
        foreach (int cp in label)
        {
            if (cp == '.')
            {
                return false;
            }
        }

        if (IdnaTables.IsMark(label[0]))
        {
            return false;
        }

        // 7. Status values.
        foreach (int cp in label)
        {
            IdnaStatus status = IdnaTables.GetStatus(cp, out _);
            bool allowed = status == IdnaStatus.Valid || (!transitional && status == IdnaStatus.Deviation);
            if (!allowed)
            {
                return false;
            }

            if ((options & Uts46Options.UseStd3AsciiRules) != 0 && cp <= 0x7F && !(cp is >= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            {
                return false;
            }
        }

        // 8.
        if ((options & Uts46Options.CheckJoiners) != 0 && !ContextJ(label))
        {
            return false;
        }

        // 9.
        if ((options & Uts46Options.CheckBidi) != 0 && bidiDomain && !BidiRule(label))
        {
            return false;
        }

        return true;
    }

    // RFC 5892 appendix A.1 and A.2.
    private static bool ContextJ(int[] label)
    {
        for (int i = 0; i < label.Length; i++)
        {
            int cp = label[i];
            if (cp != 0x200C && cp != 0x200D)
            {
                continue;
            }

            if (i > 0 && IdnaTables.CombiningClass(label[i - 1]) == 9)
            {
                continue;
            }

            if (cp == 0x200D)
            {
                return false;
            }

            // ZWNJ: (Joining_Type:{L,D}) (Joining_Type:T)* ZWNJ (Joining_Type:T)* (Joining_Type:{R,D})
            int b = i - 1;
            while (b >= 0 && IdnaTables.GetJoiningType(label[b]) == JoiningType.T)
            {
                b--;
            }

            if (b < 0 || IdnaTables.GetJoiningType(label[b]) is not (JoiningType.L or JoiningType.D))
            {
                return false;
            }

            int f = i + 1;
            while (f < label.Length && IdnaTables.GetJoiningType(label[f]) == JoiningType.T)
            {
                f++;
            }

            if (f >= label.Length || IdnaTables.GetJoiningType(label[f]) is not (JoiningType.R or JoiningType.D))
            {
                return false;
            }
        }

        return true;
    }

    // RFC 5893 section 2, the Bidi rule: six conditions on a label of a Bidi domain name.
    private static bool BidiRule(int[] label)
    {
        BidiClass first = IdnaTables.GetBidiClass(label[0]);
        bool rtl;
        if (first is BidiClass.R or BidiClass.AL)
        {
            rtl = true;
        }
        else if (first == BidiClass.L)
        {
            rtl = false;
        }
        else
        {
            return false; // condition 1
        }

        bool hasEn = false;
        bool hasAn = false;
        BidiClass lastNonNsm = first;
        foreach (int cp in label)
        {
            BidiClass c = IdnaTables.GetBidiClass(cp);
            if (rtl)
            {
                // condition 2
                if (c is not (BidiClass.R or BidiClass.AL or BidiClass.AN or BidiClass.EN or BidiClass.ES or BidiClass.CS or BidiClass.ET or BidiClass.ON or BidiClass.BN or BidiClass.NSM))
                {
                    return false;
                }
            }
            else if (c is not (BidiClass.L or BidiClass.EN or BidiClass.ES or BidiClass.CS or BidiClass.ET or BidiClass.ON or BidiClass.BN or BidiClass.NSM))
            {
                // condition 5
                return false;
            }

            hasEn |= c == BidiClass.EN;
            hasAn |= c == BidiClass.AN;
            if (c != BidiClass.NSM)
            {
                lastNonNsm = c;
            }
        }

        if (rtl)
        {
            // condition 3 and 4
            return lastNonNsm is BidiClass.R or BidiClass.AL or BidiClass.EN or BidiClass.AN && !(hasEn && hasAn);
        }

        // condition 6
        return lastNonNsm is BidiClass.L or BidiClass.EN;
    }
}
