using System.Globalization;
using System.Text;

namespace RecursiveAI.OneContact.Oracles.Phones;

/// <summary>The escaping used in case and result files: backslash as two backslashes, every char outside printable ASCII as \uXXXX.</summary>
internal static class Escape
{
    internal static string Encode(string? s)
    {
        if (s is null)
        {
            return "-";
        }

        StringBuilder sb = new(s.Length);
        foreach (char c in s)
        {
            if (c == '\\')
            {
                sb.Append("\\\\");
            }
            else if (c is >= ' ' and <= '~')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }

    internal static string Decode(string s)
    {
        StringBuilder sb = new(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\')
            {
                sb.Append(s[i]);
            }
            else if (s[i + 1] == '\\')
            {
                sb.Append('\\');
                i++;
            }
            else
            {
                sb.Append((char)int.Parse(s.AsSpan(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                i += 5;
            }
        }

        return sb.ToString();
    }
}
