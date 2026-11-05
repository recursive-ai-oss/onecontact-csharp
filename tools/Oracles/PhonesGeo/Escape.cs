using System.Globalization;
using System.Text;

namespace RecursiveAI.OneContact.Oracles.PhonesGeo;

/// <summary>Text in the harness files: a backslash is <c>\\</c> and every UTF-16 unit outside printable ASCII is <c>\uXXXX</c>; tabs and line breaks therefore never occur raw.</summary>
internal static class Escape
{
    internal static string Write(string s)
    {
        StringBuilder sb = new();
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
}
