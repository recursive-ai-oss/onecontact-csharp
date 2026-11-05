
namespace RecursiveAI.OneContact.Extraction.Text;

/// <summary>
/// Lines of a plain string (a CRM note, a signature, a user comment). Offsets are those of the string itself: the text is not copied or
/// changed. A blank line or a rule of dashes, underscores or equals signs starts a new paragraph; the paragraph is the unit that groups
/// fields into records.
/// </summary>
internal static class PlainTextView
{
    public static TextView Build(string text)
    {
        var lines = new List<TextLine>();
        int paragraph = 0;
        bool newParagraph = true;
        int i = 0;
        while (i < text.Length)
        {
            int start = i;
            while (i < text.Length && !IsLineEnd(text[i]))
            {
                i++;
            }

            int end = i;
            // CR LF is one line end.
            if (i < text.Length)
            {
                i += text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
            }

            ReadOnlySpan<char> line = text.AsSpan(start, end - start);
            if (line.Trim().IsEmpty || IsRule(line))
            {
                if (!newParagraph)
                {
                    paragraph++;
                    newParagraph = true;
                }

                continue;
            }

            lines.Add(new TextLine(start, end, newParagraph && lines.Count > 0 ? LineBreak.Hard : lines.Count == 0 ? LineBreak.Start : LineBreak.Soft, paragraph, 0, -1));
            newParagraph = false;
        }

        return new TextView(text, lines, null, null, null, [], []);
    }

    private static bool IsLineEnd(char c) => c is '\n' or '\r' or '\u0085' or '\u2028' or '\u2029';

    // "-- " is the signature delimiter of RFC 3676 §4.3; the others are the rules people draw between notes.
    private static bool IsRule(ReadOnlySpan<char> line)
    {
        ReadOnlySpan<char> t = line.Trim();
        if (t.Length < 2)
        {
            return false;
        }

        char c = t[0];
        if (c is not ('-' or '_' or '=' or '*' or '~' or '\u2014'))
        {
            return false;
        }

        foreach (char x in t)
        {
            if (x != c)
            {
                return false;
            }
        }

        return true;
    }
}
