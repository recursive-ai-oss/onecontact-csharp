namespace RecursiveAI.OneContact.Extraction.Obfuscation;

/// <summary>
/// Decodes the addresses Cloudflare's email protection writes into pages. The page keeps <c>data-cfemail="HEX"</c> (or a link whose
/// target ends in <c>/cdn-cgi/l/email-protection#HEX</c>) and a script puts the address back: the first byte of the hex is a key, and
/// every following byte is the address's UTF-8 byte exclusive-or the key. The scheme is Cloudflare's published behaviour of its
/// "email address obfuscation" feature (developers.cloudflare.com, Email Address Obfuscation); it is decoded here without running any script.
/// </summary>
internal static class CloudflareEmail
{
    /// <summary>The attribute that carries the encoded address.</summary>
    public const string Attribute = "data-cfemail";

    /// <summary>The path a protected link points at, before its <c>#</c> and the encoded address.</summary>
    public const string LinkMarker = "/cdn-cgi/l/email-protection#";

    /// <summary>Decodes a hex string; false when it is not an even number of hex digits (at least a key and one byte) or does not decode to UTF-8 text.</summary>
    /// <param name="hex">The encoded address.</param>
    /// <param name="decoded">The address.</param>
    public static bool TryDecode(ReadOnlySpan<char> hex, out string decoded)
    {
        decoded = string.Empty;
        hex = hex.Trim();
        if (hex.Length < 4 || hex.Length % 2 != 0 || hex.Length > 2048)
        {
            return false;
        }

        foreach (char c in hex)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        int key = Convert.ToInt32(hex[..2].ToString(), 16);
        var bytes = new byte[hex.Length / 2 - 1];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(Convert.ToInt32(hex.Slice(2 + (2 * i), 2).ToString(), 16) ^ key);
        }

        try
        {
            decoded = new System.Text.UTF8Encoding(false, true).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return false;
        }

        return decoded.Length > 0;
    }
}
