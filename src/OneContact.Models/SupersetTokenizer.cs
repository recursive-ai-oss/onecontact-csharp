using System.Globalization;

namespace RecursiveAI.OneContact.Models.Superset;

/// <summary>
/// The words the models read. Letters, digits and combining marks form a word; every other character that is not white space is a word of its
/// own; a line break is a word too, because where the lines end tells a recipient line from a street line. The training data is cut the same way,
/// so a model sees at run time exactly the words it was trained on.
/// </summary>
internal static class SupersetTokenizer
{
    /// <summary>The text of the word that stands for a line break in the encoder's input.</summary>
    internal const char LineBreak = '\n';

    /// <summary>Cuts a text into words.</summary>
    /// <param name="text">The text.</param>
    /// <param name="cuts">Offsets where a word must end and the next begin even inside a run of letters (the edges of labelled spans in training data); may be <see langword="null"/>.</param>
    /// <returns>The words as (start, length) in text order.</returns>
    internal static List<(int Start, int Length)> Tokenize(string text, HashSet<int>? cuts = null)
    {
        List<(int, int)> tokens = [];
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == LineBreak)
            {
                tokens.Add((i, 1));
                i++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            int start = i;
            if (IsWordPart(c))
            {
                i++;
                while (i < text.Length && IsWordPart(text[i]) && (cuts is null || !cuts.Contains(i)))
                {
                    i++;
                }
            }
            else
            {
                i++;
                if (char.IsHighSurrogate(c) && i < text.Length && char.IsLowSurrogate(text[i]))
                {
                    i++;
                }
            }

            tokens.Add((start, i - start));
        }

        return tokens;
    }

    internal static bool IsWordPart(char c)
    {
        UnicodeCategory cat = char.GetUnicodeCategory(c);
        return cat is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter
            or UnicodeCategory.OtherLetter or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
    }
}
