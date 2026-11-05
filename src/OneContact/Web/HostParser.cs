using System.Text;
using RecursiveAI.OneContact.Text.Idna;

namespace RecursiveAI.OneContact.Web;

/// <summary>The kind of a parsed host (WHATWG URL Standard, section 3.1).</summary>
internal enum HostKind
{
    /// <summary>The URL has no host.</summary>
    None,
    Domain,
    Ipv4,
    Ipv6,
    Opaque,
    Empty,
}

/// <summary>
/// The host parser of the WHATWG URL Standard (section 3.5): domains through UTS #46, IPv4 and IPv6 addresses, and opaque hosts.
/// Only the outcome is reported (a serialized host or failure); validation errors that do not fail the parse are not recorded.
/// </summary>
internal static class HostParser
{
    /// <summary>Parses and serializes a host.</summary>
    /// <param name="input">The host text, without brackets handled elsewhere.</param>
    /// <param name="isOpaque">Whether the URL is not special (the host is opaque unless bracketed).</param>
    /// <param name="host">The serialized host.</param>
    /// <param name="kind">Its kind.</param>
    /// <returns><see langword="false"/> for failure.</returns>
    internal static bool TryParse(string input, bool isOpaque, out string host, out HostKind kind)
    {
        host = string.Empty;
        kind = HostKind.None;
        if (input.StartsWith('['))
        {
            if (!input.EndsWith(']'))
            {
                return false;
            }

            if (!TryParseIpv6(input[1..^1], out ushort[] pieces))
            {
                return false;
            }

            host = "[" + SerializeIpv6(pieces) + "]";
            kind = HostKind.Ipv6;
            return true;
        }

        if (isOpaque)
        {
            if (!TryParseOpaque(input, out host))
            {
                return false;
            }

            kind = host.Length == 0 ? HostKind.Empty : HostKind.Opaque;
            return true;
        }

        // Host parser step 5 and 6: percent-decode, then UTF-8 decode without BOM (invalid bytes become U+FFFD).
        string domain = Encoding.UTF8.GetString(PercentEncoding.Decode(input));
        if (!TryDomainToAscii(domain, beStrict: false, out string asciiDomain))
        {
            return false;
        }

        if (EndsInANumber(asciiDomain))
        {
            if (!TryParseIpv4(asciiDomain, out uint address))
            {
                return false;
            }

            host = SerializeIpv4(address);
            kind = HostKind.Ipv4;
            return true;
        }

        host = asciiDomain;
        kind = HostKind.Domain;
        return true;
    }

    /// <summary>The domain parser (section 3.3).</summary>
    /// <param name="domain">The domain.</param>
    /// <param name="beStrict">Whether the strict flags apply.</param>
    /// <param name="result">The ASCII domain.</param>
    /// <returns><see langword="false"/> for failure.</returns>
    internal static bool TryDomainToAscii(string domain, bool beStrict, out string result)
    {
        result = string.Empty;
        if (beStrict)
        {
            Uts46Options strict = Uts46Options.CheckHyphens | Uts46Options.CheckBidi | Uts46Options.CheckJoiners | Uts46Options.UseStd3AsciiRules | Uts46Options.VerifyDnsLength;
            return Uts46.TryToAscii(domain, strict, out result);
        }

        bool isAscii = true;
        foreach (char c in domain)
        {
            isAscii &= c < 0x80;
        }

        if (isAscii)
        {
            // Web compatibility: an ASCII domain is only lowercased, whatever UTS #46 would say about it.
            result = domain.ToLowerInvariant();
        }
        else if (!Uts46.TryToAscii(domain, Uts46Options.CheckBidi | Uts46Options.CheckJoiners, out result))
        {
            return false;
        }

        if (result.Length == 0)
        {
            return false;
        }

        foreach (char c in result)
        {
            if (IsForbiddenDomainCodePoint(c))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsForbiddenHostCodePoint(int c) =>
        c is 0 or 9 or 0xA or 0xD or ' ' or '#' or '/' or ':' or '<' or '>' or '?' or '@' or '[' or '\\' or ']' or '^' or '|';

    internal static bool IsForbiddenDomainCodePoint(int c) => IsForbiddenHostCodePoint(c) || c <= 0x1F || c == '%' || c == 0x7F;

    // Section 3.5, opaque-host parser.
    private static bool TryParseOpaque(string input, out string host)
    {
        host = string.Empty;
        foreach (char c in input)
        {
            if (IsForbiddenHostCodePoint(c))
            {
                return false;
            }
        }

        host = PercentEncoding.Encode(input, PercentEncodeSet.C0Control);
        return true;
    }

    // Section 3.5, the ends in a number checker.
    private static bool EndsInANumber(string input)
    {
        string[] parts = input.Split('.');
        int count = parts.Length;
        if (parts[count - 1].Length == 0)
        {
            if (count == 1)
            {
                return false;
            }

            count--;
        }

        string last = parts[count - 1];
        if (last.Length > 0 && IsAllAsciiDigits(last))
        {
            return true;
        }

        return TryParseIpv4Number(last, out _, out _);
    }

    private static bool IsAllAsciiDigits(string s)
    {
        foreach (char c in s)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        return s.Length > 0;
    }

    // Section 3.5, the IPv4 parser.
    private static bool TryParseIpv4(string input, out uint address)
    {
        address = 0;
        var parts = new List<string>(input.Split('.'));
        if (parts[^1].Length == 0 && parts.Count > 1)
        {
            parts.RemoveAt(parts.Count - 1);
        }

        if (parts.Count > 4)
        {
            return false;
        }

        var numbers = new List<ulong>();
        foreach (string part in parts)
        {
            if (!TryParseIpv4Number(part, out ulong n, out _))
            {
                return false;
            }

            numbers.Add(n);
        }

        for (int i = 0; i < numbers.Count - 1; i++)
        {
            if (numbers[i] > 255)
            {
                return false;
            }
        }

        // The last number must fit in the bytes that remain: 256^(5 - count).
        ulong limit = 1UL << (8 * (5 - numbers.Count));
        if (numbers[^1] >= limit)
        {
            return false;
        }

        ulong ipv4 = numbers[^1];
        for (int i = 0; i < numbers.Count - 1; i++)
        {
            ipv4 += numbers[i] * (1UL << (8 * (3 - i)));
        }

        address = (uint)ipv4;
        return true;
    }

    // Section 3.5, the IPv4 number parser. Numbers too large for 64 bits saturate: they are out of range either way.
    private static bool TryParseIpv4Number(string input, out ulong number, out bool validationError)
    {
        number = 0;
        validationError = false;
        if (input.Length == 0)
        {
            return false;
        }

        int radix = 10;
        int start = 0;
        if (input.Length >= 2 && input[0] == '0' && (input[1] == 'x' || input[1] == 'X'))
        {
            validationError = true;
            start = 2;
            radix = 16;
        }
        else if (input.Length >= 2 && input[0] == '0')
        {
            validationError = true;
            start = 1;
            radix = 8;
        }

        if (start == input.Length)
        {
            return true;
        }

        ulong value = 0;
        for (int i = start; i < input.Length; i++)
        {
            char c = input[i];
            int digit = c is >= '0' and <= '9' ? c - '0' : c is >= 'a' and <= 'f' ? c - 'a' + 10 : c is >= 'A' and <= 'F' ? c - 'A' + 10 : 99;
            if (digit >= radix)
            {
                return false;
            }

            value = value > (ulong.MaxValue - (ulong)digit) / (ulong)radix ? ulong.MaxValue : (value * (ulong)radix) + (ulong)digit;
        }

        number = value;
        return true;
    }

    private static string SerializeIpv4(uint address) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{address >> 24}.{(address >> 16) & 255}.{(address >> 8) & 255}.{address & 255}");

    // Section 3.5, the IPv6 parser.
    private static bool TryParseIpv6(string input, out ushort[] address)
    {
        address = new ushort[8];
        int pieceIndex = 0;
        int compress = -1;
        int pointer = 0;
        int Char(int p) => p < input.Length ? input[p] : -1;

        if (Char(pointer) == ':')
        {
            if (Char(pointer + 1) != ':')
            {
                return false;
            }

            pointer += 2;
            pieceIndex++;
            compress = pieceIndex;
        }

        while (Char(pointer) != -1)
        {
            if (pieceIndex == 8)
            {
                return false;
            }

            if (Char(pointer) == ':')
            {
                if (compress != -1)
                {
                    return false;
                }

                pointer++;
                pieceIndex++;
                compress = pieceIndex;
                continue;
            }

            int value = 0;
            int length = 0;
            while (length < 4 && PercentEncoding.IsHex(Char(pointer)))
            {
                value = (value * 16) + PercentEncoding.HexValue(Char(pointer));
                pointer++;
                length++;
            }

            if (Char(pointer) == '.')
            {
                if (length == 0)
                {
                    return false;
                }

                pointer -= length;
                if (pieceIndex > 6)
                {
                    return false;
                }

                int numbersSeen = 0;
                while (Char(pointer) != -1)
                {
                    int? ipv4Piece = null;
                    if (numbersSeen > 0)
                    {
                        if (Char(pointer) == '.' && numbersSeen < 4)
                        {
                            pointer++;
                        }
                        else
                        {
                            return false;
                        }
                    }

                    if (Char(pointer) is < '0' or > '9')
                    {
                        return false;
                    }

                    while (Char(pointer) is >= '0' and <= '9')
                    {
                        int number = Char(pointer) - '0';
                        if (ipv4Piece is null)
                        {
                            ipv4Piece = number;
                        }
                        else if (ipv4Piece == 0)
                        {
                            return false;
                        }
                        else
                        {
                            ipv4Piece = (ipv4Piece * 10) + number;
                        }

                        if (ipv4Piece > 255)
                        {
                            return false;
                        }

                        pointer++;
                    }

                    address[pieceIndex] = (ushort)((address[pieceIndex] * 0x100) + ipv4Piece!.Value);
                    numbersSeen++;
                    if (numbersSeen is 2 or 4)
                    {
                        pieceIndex++;
                    }
                }

                if (numbersSeen != 4)
                {
                    return false;
                }

                break;
            }
            else if (Char(pointer) == ':')
            {
                pointer++;
                if (Char(pointer) == -1)
                {
                    return false;
                }
            }
            else if (Char(pointer) != -1)
            {
                return false;
            }

            address[pieceIndex] = (ushort)value;
            pieceIndex++;
        }

        if (compress != -1)
        {
            int swaps = pieceIndex - compress;
            pieceIndex = 7;
            while (pieceIndex != 0 && swaps > 0)
            {
                (address[pieceIndex], address[compress + swaps - 1]) = (address[compress + swaps - 1], address[pieceIndex]);
                pieceIndex--;
                swaps--;
            }
        }
        else if (pieceIndex != 8)
        {
            return false;
        }

        return true;
    }

    // Section 3.6, the IPv6 serializer.
    private static string SerializeIpv6(ushort[] address)
    {
        // Find the first longest run of two or more zero pieces to compress.
        int longestIndex = -1;
        int longestSize = 1;
        int foundIndex = -1;
        int foundSize = 0;
        for (int i = 0; i < 8; i++)
        {
            if (address[i] != 0)
            {
                if (foundSize > longestSize)
                {
                    longestIndex = foundIndex;
                    longestSize = foundSize;
                }

                foundIndex = -1;
                foundSize = 0;
            }
            else
            {
                if (foundIndex == -1)
                {
                    foundIndex = i;
                }

                foundSize++;
            }
        }

        int compress = foundSize > longestSize ? foundIndex : longestIndex;
        var sb = new StringBuilder();
        bool ignore0 = false;
        for (int i = 0; i < 8; i++)
        {
            if (ignore0 && address[i] == 0)
            {
                continue;
            }

            ignore0 = false;
            if (compress == i)
            {
                sb.Append(i == 0 ? "::" : ":");
                ignore0 = true;
                continue;
            }

            sb.Append(address[i].ToString("x", System.Globalization.CultureInfo.InvariantCulture));
            if (i != 7)
            {
                sb.Append(':');
            }
        }

        return sb.ToString();
    }
}
