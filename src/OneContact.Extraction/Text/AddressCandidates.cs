namespace RecursiveAI.OneContact.Extraction.Text;

/// <summary>An address found in the text: from its delivery line to its postal code.</summary>
/// <param name="Start">Index of the first character in <see cref="TextView.Text"/>.</param>
/// <param name="End">Index past the postal code.</param>
/// <param name="Text">The address lines, trimmed, separated by <c>\n</c>.</param>
/// <param name="FirstLine">Index of the line it starts on.</param>
/// <param name="LastLine">Index of the line the postal code is on.</param>
internal readonly record struct AddressHit(int Start, int End, string Text, int FirstLine, int LastLine);

/// <summary>
/// Finds United States and Canadian addresses in text by the shape of their last line, which is how Publication 28 §21 and the Canada
/// Post guidelines describe it: a locality, a two-letter state or province code and a ZIP code or postal code. The delivery line is
/// then the text in front of the locality on the same line (comma-separated), or the line above (with a secondary-unit line above or
/// below it). Nothing is added: lines above the delivery line (a recipient, a company) are not part of the address found. Whether the text is an
/// address is for the core parser to say; that needs a region, which this finder never chooses.
/// </summary>
internal static class AddressCandidates
{
    private static readonly HashSet<string> StateCodes = new(StringComparer.Ordinal)
    {
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "DC", "FL", "GA", "HI", "ID", "IL", "IN", "IA", "KS", "KY", "LA", "ME", "MD", "MA", "MI", "MN", "MS",
        "MO", "MT", "NE", "NV", "NH", "NJ", "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC", "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI",
        "WY", "PR", "VI", "GU", "AS", "MP", "AA", "AE", "AP",
    };

    private static readonly HashSet<string> ProvinceCodes = new(StringComparer.Ordinal)
    {
        "AB", "BC", "MB", "NB", "NL", "NS", "NT", "NU", "ON", "PE", "QC", "SK", "YT",
    };

    private static readonly string[] SecondaryWords =
    [
        "apt", "apartment", "suite", "ste", "unit", "floor", "fl", "room", "rm", "bldg", "building", "lot", "space", "spc", "trlr", "trailer", "pmb", "dept", "bureau", "app", "porte",
    ];

    public static void Find(TextView view, List<AddressHit> sink)
    {
        string text = view.Text;
        int lastEnd = -1;
        for (int l = 0; l < view.Lines.Count; l++)
        {
            TextLine line = view.Lines[l];
            foreach ((int stateStart, int anchorEnd) in Anchors(text, line.Start, line.End))
            {
                if (TryBuild(view, l, stateStart, anchorEnd, out AddressHit hit) && hit.Start >= lastEnd)
                {
                    sink.Add(hit);
                    lastEnd = hit.End;
                }
            }
        }
    }

    // Each position where "ST 12345", "ST 12345-6789" or "ST A1A 1A1" ends a line's worth of address: (start of ST, end of the code).
    private static IEnumerable<(int StateStart, int AnchorEnd)> Anchors(string text, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            int codeEnd = -1;
            bool canada = false;
            if (char.IsAsciiDigit(text[i]) && i + 5 <= end && IsFiveDigits(text, i) && (i == start || text[i - 1] is ' ' or ',' or '\u00A0'))
            {
                codeEnd = i + 5;
                if (codeEnd + 5 <= end && text[codeEnd] == '-' && IsFourDigits(text, codeEnd + 1))
                {
                    codeEnd += 5;
                }
            }
            else if (IsPostalCode(text, i, end, out int pcEnd) && (i == start || text[i - 1] is ' ' or ',' or '\u00A0'))
            {
                codeEnd = pcEnd;
                canada = true;
            }

            if (codeEnd < 0 || (codeEnd < end && (char.IsLetterOrDigit(text[codeEnd]) || text[codeEnd] == '-')))
            {
                continue;
            }

            int j = i - 1;
            while (j >= start && text[j] is ' ' or ',' or '.' or '\u00A0')
            {
                j--;
            }

            if (j - 1 < start || !char.IsAsciiLetterUpper(text[j]) || !char.IsAsciiLetterUpper(text[j - 1]) || (j - 2 >= start && char.IsLetter(text[j - 2])))
            {
                continue;
            }

            string code = text.Substring(j - 1, 2);
            if (canada ? ProvinceCodes.Contains(code) : StateCodes.Contains(code))
            {
                yield return (j - 1, codeEnd);
            }
        }
    }

    private static bool IsFiveDigits(string t, int i)
    {
        for (int k = 0; k < 5; k++)
        {
            if (!char.IsAsciiDigit(t[i + k]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFourDigits(string t, int i)
    {
        for (int k = 0; k < 4; k++)
        {
            if (i + k >= t.Length || !char.IsAsciiDigit(t[i + k]))
            {
                return false;
            }
        }

        return true;
    }

    // Canada Post: A1A 1A1, with a letter from the forward sortation area's set first, and a space or hyphen (or nothing) in the middle.
    private static bool IsPostalCode(string t, int i, int end, out int codeEnd)
    {
        codeEnd = -1;
        if (i + 6 > end || !"ABCEGHJKLMNPRSTVXY".Contains(t[i], StringComparison.Ordinal) || !char.IsAsciiDigit(t[i + 1]) || !char.IsAsciiLetterUpper(t[i + 2]))
        {
            return false;
        }

        int k = i + 3;
        if (k < end && t[k] is ' ' or '-' or '\u00A0')
        {
            k++;
        }

        if (k + 3 > end || !char.IsAsciiDigit(t[k]) || !char.IsAsciiLetterUpper(t[k + 1]) || !char.IsAsciiDigit(t[k + 2]))
        {
            return false;
        }

        codeEnd = k + 3;
        return true;
    }

    private static bool TryBuild(TextView view, int lineIndex, int stateStart, int anchorEnd, out AddressHit hit)
    {
        hit = default;
        string text = view.Text;
        TextLine line = view.Lines[lineIndex];
        int prefixEnd = stateStart;
        while (prefixEnd > line.Start && text[prefixEnd - 1] is ' ' or ',' or '\u00A0')
        {
            prefixEnd--;
        }

        int firstNonSpace = line.Start;
        while (firstNonSpace < prefixEnd && text[firstNonSpace] is ' ' or '\t' or '\u00A0')
        {
            firstNonSpace++;
        }

        if (prefixEnd <= firstNonSpace)
        {
            return false;
        }

        int comma = text.LastIndexOf(',', prefixEnd - 1, prefixEnd - firstNonSpace);
        int deliveryStart;
        int pieceStart;
        if (comma >= 0)
        {
            // "<delivery>, <locality>, ST ZIP" on one line, or "<locality>, ST ZIP" with the delivery line above.
            deliveryStart = SameLineDelivery(text, firstNonSpace, comma);
            pieceStart = deliveryStart >= 0 ? deliveryStart : comma + 1;
        }
        else
        {
            int token = DeliveryStart(text, firstNonSpace, prefixEnd);
            deliveryStart = token;
            pieceStart = token >= 0 ? token : firstNonSpace;
            if (token < 0 && !LooksLikeLocality(text, firstNonSpace, prefixEnd))
            {
                return false;
            }
        }

        while (pieceStart < stateStart && text[pieceStart] is ' ' or '\t' or '\u00A0')
        {
            pieceStart++;
        }

        if (deliveryStart >= 0)
        {
            hit = new AddressHit(pieceStart, anchorEnd, text[pieceStart..anchorEnd], lineIndex, lineIndex);
            return true;
        }

        // The delivery line is above: a line that starts with a house number or a box, with a secondary-unit line on either side of it.
        if (lineIndex == 0)
        {
            return false;
        }

        var pieces = new List<string>();
        int first = lineIndex;
        int firstStart = pieceStart;
        TextLine above = view.Lines[lineIndex - 1];
        string aboveText = text[above.Start..above.End].Trim();
        if (DeliveryStart(aboveText, 0, aboveText.Length) == 0)
        {
            first = lineIndex - 1;
            pieces.Add(aboveText);
            if (lineIndex >= 2)
            {
                TextLine two = view.Lines[lineIndex - 2];
                string twoText = text[two.Start..two.End].Trim();
                if (IsSecondary(twoText))
                {
                    first = lineIndex - 2;
                    pieces.Insert(0, twoText);
                }
            }
        }
        else if (IsSecondary(aboveText) && lineIndex >= 2)
        {
            TextLine two = view.Lines[lineIndex - 2];
            string twoText = text[two.Start..two.End].Trim();
            if (DeliveryStart(twoText, 0, twoText.Length) != 0)
            {
                return false;
            }

            first = lineIndex - 2;
            pieces.Add(twoText);
            pieces.Add(aboveText);
        }
        else
        {
            return false;
        }

        pieces.Add(text[pieceStart..anchorEnd]);
        TextLine firstLine = view.Lines[first];
        int start = first == lineIndex ? firstStart : firstLine.Start;
        while (start < firstLine.End && text[start] is ' ' or '\t' or '\u00A0')
        {
            start++;
        }

        hit = new AddressHit(start, anchorEnd, string.Join('\n', pieces), first, lineIndex);
        return true;
    }

    private static bool LooksLikeLocality(string text, int start, int end)
    {
        int words = 0;
        bool inWord = false;
        for (int i = start; i < end; i++)
        {
            char c = text[i];
            if (char.IsLetter(c) || c is '.' or '\'' or '-')
            {
                if (!inWord)
                {
                    words++;
                    inWord = true;
                }
            }
            else if (c is ' ' or '\u00A0')
            {
                inWord = false;
            }
            else
            {
                return false;
            }
        }

        return words is >= 1 and <= 5;
    }

    // The start of the delivery line in the comma-separated pieces text[start..commaAt]: the last piece that is not a secondary unit, with
    // the secondary pieces around it. -1 when no piece has a house number or a box.
    private static int SameLineDelivery(string text, int start, int commaAt)
    {
        var parts = new List<(int Start, int End)>();
        int s = start;
        for (int i = start; i <= commaAt; i++)
        {
            if (i == commaAt || text[i] == ',')
            {
                parts.Add((s, i));
                s = i + 1;
            }
        }

        int idx = parts.Count - 1;
        while (idx >= 0 && IsSecondary(text[parts[idx].Start..parts[idx].End].Trim()))
        {
            idx--;
        }

        if (idx < 0)
        {
            return -1;
        }

        int at = DeliveryStart(text, parts[idx].Start, parts[idx].End);
        if (at < 0)
        {
            return -1;
        }

        while (idx - 1 >= 0 && IsSecondary(text[parts[idx - 1].Start..parts[idx - 1].End].Trim()))
        {
            idx--;
            at = parts[idx].Start;
        }

        return at;
    }

    /// <summary>
    /// The index in <c>text[start..end]</c> of the first word that begins a delivery line: a house number (<c>123</c>, <c>12B</c>,
    /// <c>12-14</c>) followed by a word, or a box (<c>PO Box</c>, <c>P.O. Box</c>, <c>Box</c>, <c>RR 2</c>). -1 when there is none.
    /// </summary>
    internal static int DeliveryStart(string text, int start, int end)
    {
        int i = start;
        while (i < end)
        {
            while (i < end && text[i] is ' ' or '\t' or '\u00A0')
            {
                i++;
            }

            int ws = i;
            while (i < end && !(text[i] is ' ' or '\t' or '\u00A0'))
            {
                i++;
            }

            if (i == ws)
            {
                break;
            }

            ReadOnlySpan<char> word = text.AsSpan(ws, i - ws);
            int next = i;
            while (next < end && text[next] is ' ' or '\t' or '\u00A0')
            {
                next++;
            }

            if (IsHouseNumber(word) && next < end && (char.IsLetter(text[next]) || (char.IsAsciiDigit(text[next]) && StartsOrdinal(text.AsSpan(next, end - next)))))
            {
                return ws;
            }

            if (IsBoxWord(word, text, next, end))
            {
                return ws;
            }
        }

        return -1;
    }

    private static bool IsHouseNumber(ReadOnlySpan<char> w)
    {
        int d = 0;
        while (d < w.Length && char.IsAsciiDigit(w[d]))
        {
            d++;
        }

        if (d == 0 || d > 6)
        {
            return false;
        }

        if (d == w.Length)
        {
            return true;
        }

        if (d == w.Length - 1 && char.IsAsciiLetter(w[d]))
        {
            return true;
        }

        if (w[d] == '-')
        {
            ReadOnlySpan<char> rest = w[(d + 1)..];
            int e = 0;
            while (e < rest.Length && char.IsAsciiDigit(rest[e]))
            {
                e++;
            }

            return e > 0 && e <= 5 && (e == rest.Length || (e == rest.Length - 1 && char.IsAsciiLetter(rest[e])));
        }

        return false;
    }

    private static bool StartsOrdinal(ReadOnlySpan<char> t)
    {
        int d = 0;
        while (d < t.Length && char.IsAsciiDigit(t[d]))
        {
            d++;
        }

        if (d == 0 || d + 1 >= t.Length)
        {
            return false;
        }

        ReadOnlySpan<char> suffix = t.Slice(d, 2);
        return suffix.Equals("st", StringComparison.OrdinalIgnoreCase) || suffix.Equals("nd", StringComparison.OrdinalIgnoreCase)
            || suffix.Equals("rd", StringComparison.OrdinalIgnoreCase) || suffix.Equals("th", StringComparison.OrdinalIgnoreCase);
    }

    // "PO Box 12", "P.O. Box 12", "POB 12", "Box 12", "RR 2", "Rural Route 2": a box word followed by a number or "Box".
    private static bool IsBoxWord(ReadOnlySpan<char> word, string text, int next, int end)
    {
        if (word.Length > 6)
        {
            return false;
        }

        string w = word.ToString().Replace(".", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        ReadOnlySpan<char> following = text.AsSpan(next, Math.Max(0, end - next));
        if (w is "PO" or "POB")
        {
            return following.StartsWith("Box", StringComparison.OrdinalIgnoreCase) || (w == "POB" && following.Length > 0 && char.IsAsciiDigit(following[0]));
        }

        if (w is "BOX" or "RR" or "CP")
        {
            return following.Length > 0 && char.IsAsciiDigit(following[0]);
        }

        return false;
    }

    internal static bool IsSecondary(string line)
    {
        if (line.Length == 0)
        {
            return false;
        }

        if (line[0] == '#')
        {
            return line.Length > 1;
        }

        int e = 0;
        while (e < line.Length && char.IsLetter(line[e]))
        {
            e++;
        }

        if (e == 0)
        {
            return false;
        }

        ReadOnlySpan<char> word = line.AsSpan(0, e);
        foreach (string w in SecondaryWords)
        {
            if (word.Equals(w, StringComparison.OrdinalIgnoreCase))
            {
                // "Suite 200", "Apt. 4B", "Unit #5", "Floor 3": a designator, then something that identifies the unit.
                int k = e;
                while (k < line.Length && line[k] is ' ' or '.' or '#' or '\u00A0')
                {
                    k++;
                }

                return k < line.Length && char.IsLetterOrDigit(line[k]);
            }
        }

        return false;
    }
}
