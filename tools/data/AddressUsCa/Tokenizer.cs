using System.Globalization;

namespace OneContact.Data.AddressUsCa;


/// <summary>
/// Word tokens for the examples. Letters, digits and combining marks form a token; every other non-space character is a token on its
/// own (hyphens and apostrophes do not join words here, so a span edge inside "4-123" or "O'Brien" is still a token edge); tokens are
/// then split at every labelled span edge. Based on the pipeline's rule tokenizer, minus its joiners.
/// </summary>
internal static class Tokenizer
{
    internal static List<(int Start, int End)> Tokenize(string text, List<(int Start, int End, string Label)> entities)
    {
        HashSet<int> cuts = [];
        foreach ((int s, int e, _) in entities)
        {
            cuts.Add(s);
            cuts.Add(e);
        }

        List<(int, int)> tokens = [];
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            int start = i;
            if (IsWordPart(text, i))
            {
                i++;
                while (i < text.Length && IsWordPart(text, i) && !cuts.Contains(i))
                {
                    i++;
                }
            }
            else
            {
                i++;
                if (char.IsHighSurrogate(text[i - 1]) && i < text.Length && char.IsLowSurrogate(text[i]))
                {
                    i++;
                }
            }

            tokens.Add((start, i));
        }

        return tokens;
    }

    private static bool IsWordPart(string text, int i)
    {
        UnicodeCategory cat = char.GetUnicodeCategory(text[i]);
        return cat is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
    }
}
