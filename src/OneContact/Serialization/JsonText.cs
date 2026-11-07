using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace RecursiveAI.OneContact.Serialization;

/// <summary>
/// Writes canonical forms as JSON strings. The default encoder writes a plus sign as <c>\u002B</c> because it guards against HTML injection, but a phone number's
/// canonical form starts with one and an email's local part or a URL's query may hold one, so a value is written with the default escaping for everything else and
/// the plus sign as it is. Quotes, backslashes, control characters, angle brackets, ampersands, apostrophes and non-ASCII text keep the default escaping, so the output
/// stays as safe to place in HTML or script as before. A property name uses the relaxed encoder, which also leaves the plus sign alone, when it holds nothing but letters,
/// digits and ASCII punctuation free of angle brackets, apostrophes and backticks; the writer offers no way to escape only some characters of a name, so an ampersand
/// next to a plus sign is written as it is in a name (as a value it stays escaped). Any other name keeps the default escaping.
/// </summary>
internal static class JsonText
{
    internal static void WriteString(Utf8JsonWriter writer, string value)
    {
        if (!value.Contains('+', StringComparison.Ordinal))
        {
            writer.WriteStringValue(value);
            return;
        }

        var raw = new StringBuilder(value.Length + 8).Append('"');
        int start = 0;
        while (true)
        {
            int plus = value.IndexOf('+', start);
            string piece = plus < 0 ? value[start..] : value[start..plus];
            raw.Append(JsonEncodedText.Encode(piece).ToString());
            if (plus < 0)
            {
                break;
            }

            raw.Append('+');
            start = plus + 1;
        }

        // The pieces are already escaped by the default encoder and the plus signs need no escape, so the text is valid JSON as it stands.
        writer.WriteRawValue(raw.Append('"').ToString(), skipInputValidation: true);
    }

    internal static void WriteString(Utf8JsonWriter writer, string name, string value)
    {
        writer.WritePropertyName(name);
        WriteString(writer, value);
    }

    internal static void WritePropertyName(Utf8JsonWriter writer, string name) =>
        writer.WritePropertyName(IsPlain(name) ? JsonEncodedText.Encode(name, JavaScriptEncoder.UnsafeRelaxedJsonEscaping) : JsonEncodedText.Encode(name));

    private static bool IsPlain(string value)
    {
        foreach (char c in value)
        {
            if (c < 0x80)
            {
                if (c < 0x20 || c == 0x7F || c is '<' or '>' or '\'' or '`')
                {
                    return false;
                }
            }
            else if (!char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark))
            {
                return false;
            }
        }

        return true;
    }
}
