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
        int[] buffer = new int[input.Length];
        if (!TryDecode(input, buffer, out int written))
        {
            output = [];
            return false;
        }

        output = written == buffer.Length ? buffer : buffer[..written];
        return true;
    }

    /// <summary>Decodes the part of a label after <c>xn--</c> into a caller-owned buffer.</summary>
    /// <param name="input">The ASCII Punycode text.</param>
    /// <param name="output">Receives the code points; as long as <paramref name="input"/> is always enough, because every decoded code point takes at least one input character.</param>
    /// <param name="written">The number of code points written.</param>
    /// <returns><see langword="false"/> when the input is not valid Punycode (RFC 3492 section 6.2) or <paramref name="output"/> is too short.</returns>
    internal static bool TryDecode(ReadOnlySpan<char> input, Span<int> output, out int written)
    {
        written = 0;
        int count0 = 0;
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

            if (count0 >= output.Length)
            {
                return false;
            }

            output[count0++] = input[j];
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

            int count = count0 + 1;
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

            if (count0 >= output.Length)
            {
                return false;
            }

            output.Slice(i, count0 - i).CopyTo(output[(i + 1)..]);
            output[i] = n;
            count0++;
            i++;
        }

        written = count0;
        return true;
    }

    /// <summary>Encodes code points (without any <c>xn--</c> prefix).</summary>
    /// <param name="input">The code points.</param>
    /// <param name="output">The ASCII Punycode text.</param>
    /// <returns><see langword="false"/> on arithmetic overflow.</returns>
    internal static bool TryEncode(ReadOnlySpan<int> input, out string output)
    {
        char[] buffer = new char[(input.Length * 8) + 8];
        if (!TryEncode(input, buffer, out int written))
        {
            output = string.Empty;
            return false;
        }

        output = new string(buffer, 0, written);
        return true;
    }

    /// <summary>Encodes code points (without any <c>xn--</c> prefix) into a caller-owned buffer.</summary>
    /// <param name="input">The code points.</param>
    /// <param name="output">Receives the ASCII Punycode text.</param>
    /// <param name="written">The number of characters written.</param>
    /// <returns><see langword="false"/> on arithmetic overflow or when <paramref name="output"/> is too short.</returns>
    internal static bool TryEncode(ReadOnlySpan<int> input, Span<char> output, out int written)
    {
        written = 0;
        var sb = new Appender(output);
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

        written = sb.Length;
        return !sb.Overflowed;
    }

    // Writes into a fixed buffer and remembers when it ran out, so the caller can report failure once at the end.
    private ref struct Appender(Span<char> buffer)
    {
        private readonly Span<char> _buffer = buffer;

        internal int Length { get; private set; }

        internal bool Overflowed { get; private set; }

        internal void Append(char c)
        {
            if (Length < _buffer.Length)
            {
                _buffer[Length++] = c;
            }
            else
            {
                Overflowed = true;
            }
        }
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
