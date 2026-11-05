namespace RecursiveAI.OneContact.Text.Idna;

/// <summary>
/// Punycode (RFC 3492 section 6): the Bootstring encoding of Unicode code points in ASCII that IDNA uses for the <c>xn--</c> labels.
/// Both directions work on code points and report failure instead of throwing on overflow or malformed input.
/// </summary>
internal static class Punycode
{
    // RFC 3492 section 5: parameter values for Punycode.
    private const int Base = 36;
    private const int TMin = 1;
    private const int TMax = 26;
    private const int Skew = 38;
    private const int Damp = 700;
    private const int InitialBias = 72;
    private const int InitialN = 0x80;

    /// <summary>Decodes the part of a label after <c>xn--</c>.</summary>
    /// <param name="input">The ASCII Punycode text.</param>
    /// <param name="output">The decoded code points.</param>
    /// <returns><see langword="false"/> when the input is not valid Punycode (RFC 3492 section 6.2).</returns>
    internal static bool TryDecode(ReadOnlySpan<char> input, out int[] output)
    {
        output = [];
        var result = new List<int>();
        int basicEnd = input.LastIndexOf('-');
        if (basicEnd < 0)
        {
            basicEnd = 0;
        }

        for (int j = 0; j < basicEnd; j++)
        {
            if (input[j] >= 0x80)
            {
                return false;
            }

            result.Add(input[j]);
        }

        int n = InitialN;
        int i = 0;
        int bias = InitialBias;
        int pos = basicEnd > 0 ? basicEnd + 1 : 0;
        while (pos < input.Length)
        {
            int oldi = i;
            long w = 1;
            for (int k = Base; ; k += Base)
            {
                if (pos >= input.Length)
                {
                    return false;
                }

                int digit = DecodeDigit(input[pos++]);
                if (digit < 0)
                {
                    return false;
                }

                long next = i + ((long)digit * w);
                if (next > int.MaxValue)
                {
                    return false;
                }

                i = (int)next;
                int t = k <= bias ? TMin : k >= bias + TMax ? TMax : k - bias;
                if (digit < t)
                {
                    break;
                }

                w *= Base - t;
                if (w > int.MaxValue)
                {
                    return false;
                }
            }

            int count = result.Count + 1;
            bias = Adapt(i - oldi, count, oldi == 0);
            long newN = n + (i / count);
            if (newN > 0x10FFFF)
            {
                return false;
            }

            n = (int)newN;
            i %= count;

            // A surrogate is not a scalar value and can never be encoded.
            if (n is >= 0xD800 and <= 0xDFFF)
            {
                return false;
            }

            result.Insert(i, n);
            i++;
        }

        output = [.. result];
        return true;
    }

    /// <summary>Encodes code points (without any <c>xn--</c> prefix).</summary>
    /// <param name="input">The code points.</param>
    /// <param name="output">The ASCII Punycode text.</param>
    /// <returns><see langword="false"/> on arithmetic overflow.</returns>
    internal static bool TryEncode(ReadOnlySpan<int> input, out string output)
    {
        output = string.Empty;
        var sb = new System.Text.StringBuilder();
        foreach (int c in input)
        {
            if (c < 0x80)
            {
                sb.Append((char)c);
            }
        }

        int basicCount = sb.Length;
        int handled = basicCount;
        if (basicCount > 0)
        {
            sb.Append('-');
        }

        int n = InitialN;
        long delta = 0;
        int bias = InitialBias;
        while (handled < input.Length)
        {
            int m = int.MaxValue;
            foreach (int c in input)
            {
                if (c >= n && c < m)
                {
                    m = c;
                }
            }

            delta += (long)(m - n) * (handled + 1);
            if (delta > int.MaxValue)
            {
                return false;
            }

            n = m;
            foreach (int c in input)
            {
                if (c < n)
                {
                    delta++;
                    if (delta > int.MaxValue)
                    {
                        return false;
                    }
                }

                if (c == n)
                {
                    int q = (int)delta;
                    for (int k = Base; ; k += Base)
                    {
                        int t = k <= bias ? TMin : k >= bias + TMax ? TMax : k - bias;
                        if (q < t)
                        {
                            break;
                        }

                        sb.Append(EncodeDigit(t + ((q - t) % (Base - t))));
                        q = (q - t) / (Base - t);
                    }

                    sb.Append(EncodeDigit(q));
                    bias = Adapt((int)delta, handled + 1, handled == basicCount);
                    delta = 0;
                    handled++;
                }
            }

            delta++;
            n++;
        }

        output = sb.ToString();
        return true;
    }

    // RFC 3492 section 6.1: bias adaptation.
    private static int Adapt(int delta, int numPoints, bool firstTime)
    {
        delta = firstTime ? delta / Damp : delta / 2;
        delta += delta / numPoints;
        int k = 0;
        while (delta > (((Base - TMin) * TMax) / 2))
        {
            delta /= Base - TMin;
            k += Base;
        }

        return k + (((Base - TMin + 1) * delta) / (delta + Skew));
    }

    private static int DecodeDigit(char c) => c switch
    {
        >= 'a' and <= 'z' => c - 'a',
        >= 'A' and <= 'Z' => c - 'A',
        >= '0' and <= '9' => c - '0' + 26,
        _ => -1,
    };

    private static char EncodeDigit(int d) => (char)(d < 26 ? 'a' + d : '0' + (d - 26));
}
