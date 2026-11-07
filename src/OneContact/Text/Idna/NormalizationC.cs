namespace RecursiveAI.OneContact.Text.Idna;

/// <summary>
/// Unicode Normalization Form C (UAX #15) over code points, from the compiled Unicode data, so the result is the same on every
/// platform and under every globalization setting (the BCL's normalization depends on the operating system's ICU).
/// </summary>
internal static class NormalizationC
{
    // UAX #15 section 3.12 (Hangul syllable composition and decomposition).
    private const int SBase = 0xAC00;
    private const int LBase = 0x1100;
    private const int VBase = 0x1161;
    private const int TBase = 0x11A7;
    private const int LCount = 19;
    private const int VCount = 21;
    private const int TCount = 28;
    private const int NCount = VCount * TCount;
    private const int SCount = LCount * NCount;

    /// <summary>Normalizes code points to NFC.</summary>
    /// <param name="input">The code points.</param>
    /// <returns>The normalized code points; the same array when nothing needs to change is not promised.</returns>
    internal static int[] Normalize(ReadOnlySpan<int> input)
    {
        if (IsAllAsciiOrNeedsNothing(input))
        {
            return input.ToArray();
        }

        return NormalizeFull(input);
    }

    /// <summary>Normalizes code points the caller will not use again, returning the same array when nothing needs to change.</summary>
    /// <param name="input">The code points.</param>
    /// <returns>The normalized code points.</returns>
    internal static int[] NormalizeOwned(int[] input) => IsAllAsciiOrNeedsNothing(input) ? input : NormalizeFull(input);

    /// <summary>Normalizes without the shortcut for code points below <see cref="IdnaTables.NfcStableLimit"/> (the reference for the test of that shortcut).</summary>
    internal static int[] NormalizeFull(ReadOnlySpan<int> input)
    {
        var decomposed = new List<int>(input.Length + 8);
        foreach (int cp in input)
        {
            Decompose(cp, decomposed);
        }

        // Canonical ordering: a stable sort of each run of non-starters by combining class (UAX #15 section 3.11, D109).
        int i = 0;
        while (i < decomposed.Count)
        {
            if (IdnaTables.CombiningClass(decomposed[i]) == 0)
            {
                i++;
                continue;
            }

            int j = i;
            while (j < decomposed.Count && IdnaTables.CombiningClass(decomposed[j]) != 0)
            {
                j++;
            }

            for (int a = i + 1; a < j; a++)
            {
                int cp = decomposed[a];
                byte cc = IdnaTables.CombiningClass(cp);
                int b = a - 1;
                while (b >= i && IdnaTables.CombiningClass(decomposed[b]) > cc)
                {
                    decomposed[b + 1] = decomposed[b];
                    b--;
                }

                decomposed[b + 1] = cp;
            }

            i = j;
        }

        return Compose(decomposed);
    }

    // Code points below IdnaTables.NfcStableLimit are certainly unchanged; anything else is run through the algorithm.
    private static bool IsAllAsciiOrNeedsNothing(ReadOnlySpan<int> input)
    {
        foreach (int cp in input)
        {
            if (cp >= IdnaTables.NfcStableLimit && !IdnaTables.IsNfcStable(cp))
            {
                return false;
            }
        }

        return true;
    }

    private static void Decompose(int cp, List<int> output)
    {
        if (cp is >= SBase and < (SBase + SCount))
        {
            int s = cp - SBase;
            output.Add(LBase + (s / NCount));
            output.Add(VBase + ((s % NCount) / TCount));
            int t = TBase + (s % TCount);
            if (t != TBase)
            {
                output.Add(t);
            }

            return;
        }

        if (IdnaTables.TryGetDecomposition(cp, out int a, out int b))
        {
            Decompose(a, output);
            if (b != 0)
            {
                Decompose(b, output);
            }

            return;
        }

        output.Add(cp);
    }

    // UAX #15 section 3.11 (canonical composition algorithm).
    private static int[] Compose(List<int> text)
    {
        if (text.Count == 0)
        {
            return [];
        }

        var result = new List<int>(text.Count);
        int starterIndex = -1;
        int lastClass = -1;
        foreach (int cp in text)
        {
            byte cc = IdnaTables.CombiningClass(cp);
            if (starterIndex >= 0)
            {
                // The code point is blocked from the starter when a code point between them has a combining class that is zero
                // or not lower than its own.
                bool blocked = result.Count - 1 != starterIndex && (lastClass == 0 || lastClass >= cc);
                if (!blocked)
                {
                    int composite = ComposePair(result[starterIndex], cp);
                    if (composite != 0)
                    {
                        result[starterIndex] = composite;
                        continue;
                    }
                }
            }

            if (cc == 0)
            {
                starterIndex = result.Count;
            }

            lastClass = cc;
            result.Add(cp);
        }

        return [.. result];
    }

    private static int ComposePair(int first, int second)
    {
        // Hangul: LV and LVT syllables.
        if (first is >= LBase and < (LBase + LCount) && second is >= VBase and < (VBase + VCount))
        {
            return SBase + ((((first - LBase) * VCount) + (second - VBase)) * TCount);
        }

        if (first is >= SBase and < (SBase + SCount) && ((first - SBase) % TCount) == 0 && second is > TBase and < (TBase + TCount))
        {
            return first + (second - TBase);
        }

        return IdnaTables.Compose(first, second);
    }
}
