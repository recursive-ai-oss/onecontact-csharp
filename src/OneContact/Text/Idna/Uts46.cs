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

    // Fixed size of the stack buffers below: domains and labels up to this many code units take them, longer ones fall back to the heap or
    // to the general route.
    private const int ScratchLength = 128;

    /// <summary>ToASCII (UTS #46 section 4.2).</summary>
    /// <param name="domain">The domain name.</param>
    /// <param name="options">The flags.</param>
    /// <param name="result">The ASCII domain; unspecified when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="false"/> when an error was recorded.</returns>
    internal static bool TryToAscii(string domain, Uts46Options options, out string result)
    {
        if (TryToAsciiOfAsciiText(domain, options, out result, out bool fastOk))
        {
            return fastOk;
        }

        if (TryToAsciiOfValidBmpText(domain, options, out result))
        {
            return true;
        }

        return TryToAsciiGeneral(domain, options, out result);
    }

    /// <summary>ToASCII by the general route alone (map, normalize, convert and validate every label); the short route is held to this one in tests.</summary>
    internal static bool TryToAsciiGeneral(string domain, Uts46Options options, out string result)
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

    // ToASCII of a domain made only of ASCII letters, digits, hyphens and dots, which is what a domain with Punycode labels looks like when it
    // arrives already encoded. Nothing in the general route changes such text except lower-casing it and checking its labels, so this does
    // the same checks label by label, without code point arrays: a plain label is checked as it stands, and a label of xn-- form is decoded
    // (UTS #46 section 4 step 4), validated as the general route validates it, and kept as written, because Punycode is its own inverse
    // (RFC 3492 section 6: the encoder emits the one shortest form). Returns false, leaving the answer to the general route, for anything
    // else: other characters, invalid Punycode that is to be ignored, or a label that decodes to right-to-left text, which changes how the
    // Bidi rule treats every other label.
    internal static bool TryToAsciiOfAsciiText(string domain, Uts46Options options, out string result, out bool ok)
    {
        result = domain;
        ok = false;
        if (domain.Length is 0 or > MaxDomainLength)
        {
            return false;
        }

        bool upper = false;
        foreach (char c in domain)
        {
            if (char.IsAsciiLetterUpper(c))
            {
                upper = true;
            }
            else if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '.'))
            {
                return false;
            }
        }

        string lower = upper ? domain.ToLowerInvariant() : domain;

        bool good = true;
        bool dnsLength = (options & Uts46Options.VerifyDnsLength) != 0;
        bool checkHyphens = (options & Uts46Options.CheckHyphens) != 0;
        int start = 0;
        for (int i = 0; i <= lower.Length; i++)
        {
            if (i < lower.Length && lower[i] != '.')
            {
                continue;
            }

            ReadOnlySpan<char> label = lower.AsSpan(start, i - start);
            start = i + 1;
            if (dnsLength && label.Length is 0 or > 63)
            {
                good = false;
            }

            if (label.Length == 0)
            {
                continue;
            }

            if (label.Length >= 4 && label[0] == 'x' && label[1] == 'n' && label[2] == '-' && label[3] == '-')
            {
                switch (CheckPunycodeLabel(label[4..], options))
                {
                    case PunycodeCheck.Decline:
                        return false;
                    case PunycodeCheck.Invalid:
                        good = false;
                        break;
                }

                continue;
            }

            // A plain label: lower-case letters, digits and hyphens are valid, and the hyphen rules are all that is left to check.
            if (checkHyphens && ((label.Length >= 4 && label[2] == '-' && label[3] == '-') || label[0] == '-' || label[^1] == '-'))
            {
                good = false;
            }
        }

        if (dnsLength && lower.Length is < 1 or > 253)
        {
            good = false;
        }

        result = lower;
        ok = good;
        return true;
    }

    private enum PunycodeCheck { Valid, Invalid, Decline }

    // Step 4 and the label validity checks for one xn-- label of an ASCII-only domain (the text after the prefix is given). Decline means the
    // general route must decide: invalid Punycode that is to be ignored, or a label that decodes to right-to-left text, which changes how the
    // Bidi rule treats every other label.
    private static PunycodeCheck CheckPunycodeLabel(ReadOnlySpan<char> rest, Uts46Options options)
    {
        // A decoded label is never longer than its Punycode; a small stack buffer serves every usual label.
        // Bounds: ScratchLength integers, used only when the label fits; a longer one gets a heap array.
        Span<int> buffer = rest.Length <= ScratchLength ? stackalloc int[ScratchLength] : new int[rest.Length];
        if (!Punycode.TryDecode(rest, buffer, out int decodedLength))
        {
            return (options & Uts46Options.IgnoreInvalidPunycode) != 0 ? PunycodeCheck.Decline : PunycodeCheck.Invalid;
        }

        ReadOnlySpan<int> decoded = buffer[..decodedLength];
        bool allAscii = true;
        foreach (int cp in decoded)
        {
            allAscii &= cp <= 0x7F;
            if ((options & Uts46Options.CheckBidi) != 0 && IdnaTables.GetBidiClass(cp) is BidiClass.R or BidiClass.AL or BidiClass.AN)
            {
                return PunycodeCheck.Decline;
            }
        }

        bool good = decoded.Length != 0 && !allAscii;
        good &= ValidateLabel(decoded, options, true, false);
        return good ? PunycodeCheck.Valid : PunycodeCheck.Invalid;
    }

    // ToASCII of a short domain whose code points are all in the Basic Multilingual Plane and whose mapping and validity checks all pass: the
    // common internationalized domain (one script, no right-to-left text, nothing for NFC to reorder). It follows the general route step by
    // step on fixed stack buffers (ScratchLength code points) instead of arrays and lists, and returns true only when the answer is a
    // success. Anything else returns false and is left to the general route, which also produces the failures: a surrogate, a disallowed
    // code point, a mapping that leaves the plane, a character NFC could change, transitional processing, right-to-left text, any
    // validity failure, a domain or result longer than the buffers.
    internal static bool TryToAsciiOfValidBmpText(string domain, Uts46Options options, out string result)
    {
        result = domain;
        if (domain.Length is 0 or > ScratchLength || (options & (Uts46Options.TransitionalProcessing | Uts46Options.IgnoreInvalidPunycode)) != 0)
        {
            return false;
        }

        // Step 1, map.
        // Bounds: the domain is at most ScratchLength code units and a mapping that would overflow declines the route; the four buffers
        // here are ScratchLength integers, ScratchLength characters, ScratchLength integers and twice ScratchLength characters.
        Span<int> mapped = stackalloc int[ScratchLength];
        int length = 0;
        foreach (char c in domain)
        {
            if (char.IsSurrogate(c))
            {
                return false;
            }

            switch (IdnaTables.GetStatus(c, out ReadOnlySpan<char> mapping))
            {
                case IdnaStatus.Valid:
                case IdnaStatus.Deviation: // non-transitional processing keeps a deviation character
                    if (length == mapped.Length)
                    {
                        return false;
                    }

                    mapped[length++] = c;
                    break;
                case IdnaStatus.Ignored:
                    break;
                case IdnaStatus.Mapped:
                    if (length + mapping.Length > mapped.Length)
                    {
                        return false;
                    }

                    foreach (char m in mapping)
                    {
                        if (char.IsSurrogate(m))
                        {
                            return false;
                        }

                        mapped[length++] = m;
                    }

                    break;
                default:
                    return false;
            }
        }

        // Step 2, normalize: a text made only of code points that NFC never changes is in NFC.
        for (int i = 0; i < length; i++)
        {
            if (mapped[i] >= IdnaTables.NfcStableLimit && !IdnaTables.IsNfcStable(mapped[i]))
            {
                return false;
            }
        }

        bool checkBidi = (options & Uts46Options.CheckBidi) != 0;
        Span<char> punycodeText = stackalloc char[ScratchLength];
        Span<int> decoded = stackalloc int[ScratchLength];
        Span<char> output = stackalloc char[ScratchLength * 2];
        int written = 0;

        // Steps 3 and 4 for every label; pass 0 only looks for right-to-left text (the Bidi rule applies to every label once any label has
        // some), pass 1 validates and converts.
        for (int pass = checkBidi ? 0 : 1; pass < 2; pass++)
        {
            int start = 0;
            bool firstLabel = true;
            for (int i = 0; i <= length; i++)
            {
                if (i < length && mapped[i] != '.')
                {
                    continue;
                }

                ReadOnlySpan<int> label = mapped.Slice(start, i - start);
                start = i + 1;
                bool punycode = label.Length >= 4 && label[0] == 'x' && label[1] == 'n' && label[2] == '-' && label[3] == '-';
                if (punycode)
                {
                    // Step 4: a Punycode label with non-ASCII text, invalid Punycode or an all-ASCII result is an error.
                    for (int k = 4; k < label.Length; k++)
                    {
                        if (label[k] > 0x7F)
                        {
                            return false;
                        }

                        punycodeText[k - 4] = (char)label[k];
                    }

                    if (!Punycode.TryDecode(punycodeText[..(label.Length - 4)], decoded, out int decodedLength) || decodedLength == 0)
                    {
                        return false;
                    }

                    label = decoded[..decodedLength];
                    bool allAscii = true;
                    foreach (int cp in label)
                    {
                        allAscii &= cp <= 0x7F;
                    }

                    if (allAscii)
                    {
                        return false;
                    }
                }

                if (pass == 0)
                {
                    foreach (int cp in label)
                    {
                        if (IdnaTables.GetBidiClass(cp) is BidiClass.R or BidiClass.AL or BidiClass.AN)
                        {
                            return false;
                        }
                    }

                    continue;
                }

                if (!ValidateLabel(label, options, punycode, false))
                {
                    return false;
                }

                // ToASCII (section 4.2 step 3): a label with any non-ASCII code point becomes xn-- and its Punycode.
                if (!firstLabel)
                {
                    if (written == output.Length)
                    {
                        return false;
                    }

                    output[written++] = '.';
                }

                firstLabel = false;
                bool ascii = true;
                foreach (int cp in label)
                {
                    ascii &= cp <= 0x7F;
                }

                if (ascii)
                {
                    if (written + label.Length > output.Length)
                    {
                        return false;
                    }

                    foreach (int cp in label)
                    {
                        output[written++] = (char)cp;
                    }
                }
                else
                {
                    if (written + 4 > output.Length)
                    {
                        return false;
                    }

                    "xn--".CopyTo(output[written..]);
                    written += 4;
                    if (!Punycode.TryEncode(label, output[written..], out int encodedLength))
                    {
                        return false;
                    }

                    written += encodedLength;
                }
            }
        }

        if ((options & Uts46Options.VerifyDnsLength) != 0)
        {
            // UTS #46 section 4.2 step 4: 1 to 253 octets and every label 1 to 63; an empty root label is not allowed.
            if (written is < 1 or > 253)
            {
                return false;
            }

            ReadOnlySpan<char> text = output[..written];
            int labelStart = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                if (i == text.Length || text[i] == '.')
                {
                    if (i - labelStart is < 1 or > 63)
                    {
                        return false;
                    }

                    labelStart = i + 1;
                }
            }
        }

        result = new string(output[..written]);
        return true;
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
                // Punycode can decode to a surrogate code point, which is not a scalar value and cannot be written as UTF-16; it is already an error
                // (surrogates are disallowed), so the result carries U+FFFD in its place.
                sb.Append(cp is >= 0xD800 and <= 0xDFFF ? '�' : char.ConvertFromUtf32(cp));
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
        var mapped = new CodePointBuffer(domain.Length);
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
                // The domain is a USVString: a lone surrogate reads as U+FFFD (WHATWG URL, domain to ASCII), which is disallowed.
                cp = 0xFFFD;
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

                    mapped.AddAll(mapping);
                    break;
                case IdnaStatus.Deviation:
                    if (transitional)
                    {
                        mapped.AddAll(mapping);
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
        int[] normalized = NormalizationC.NormalizeOwned(mapped.ToArray());
        int dots = 0;
        foreach (int cp in normalized)
        {
            if (cp == '.')
            {
                dots++;
            }
        }

        labels = new List<int[]>(dots + 1);
        int labelStart = 0;
        for (int i = 0; i <= normalized.Length; i++)
        {
            if (i == normalized.Length || normalized[i] == '.')
            {
                labels.Add(normalized[labelStart..i]);
                labelStart = i + 1;
            }
        }

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

        // Only the Bidi rule reads this, so it is looked for only when that rule is on.
        bool bidiDomain = false;
        if ((options & Uts46Options.CheckBidi) != 0)
        {
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
        }

        for (int i = 0; i < labels.Count; i++)
        {
            ok &= ValidateLabel(labels[i], options, fromPunycode[i], bidiDomain);
        }

        return ok;
    }

    // The mapped code points of step 1. The array starts at the length of the input, which is the final length when nothing is ignored or
    // expanded (the usual case), so it is then handed on as it is.
    private sealed class CodePointBuffer
    {
        private int[] _items;
        private int _count;

        internal CodePointBuffer(int capacity) => _items = new int[Math.Max(capacity, 4)];

        internal void Add(int cp)
        {
            if (_count == _items.Length)
            {
                Array.Resize(ref _items, _items.Length * 2);
            }

            _items[_count++] = cp;
        }

        // Code points of UTF-16 text; a lone surrogate reads as U+FFFD.
        internal void AddAll(ReadOnlySpan<char> text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    Add(char.ConvertToUtf32(text[i], text[i + 1]));
                    i++;
                }
                else
                {
                    Add(char.IsSurrogate(text[i]) ? 0xFFFD : text[i]);
                }
            }
        }

        internal int[] ToArray() => _count == _items.Length ? _items : _items[.._count];
    }

    // UTS #46 section 4.1, "Validity Criteria". A label that came from Punycode is validated for non-transitional processing.
    private static bool ValidateLabel(ReadOnlySpan<int> label, Uts46Options options, bool fromPunycode, bool bidiDomain)
    {
        if (label.Length == 0)
        {
            return true;
        }

        bool transitional = (options & Uts46Options.TransitionalProcessing) != 0 && !fromPunycode;

        // 1. NFC. Mapping and normalization already did this for labels that were not Punycode.
        if (fromPunycode && !AllBelowNfcLimit(label))
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

    // Code points below IdnaTables.NfcStableLimit never change under NFC, so a label made only of them is in NFC.
    private static bool AllBelowNfcLimit(ReadOnlySpan<int> label)
    {
        foreach (int cp in label)
        {
            if (cp >= IdnaTables.NfcStableLimit)
            {
                return false;
            }
        }

        return true;
    }

    // RFC 5892 appendix A.1 and A.2.
    private static bool ContextJ(ReadOnlySpan<int> label)
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
    private static bool BidiRule(ReadOnlySpan<int> label)
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
