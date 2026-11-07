namespace RecursiveAI.OneContact.Addresses.Tier2;

// Forms of Britain, the Crown dependencies and Ireland that are written the same way on web pages as the posts print them:
// the island's name above the postcode (UPU Jersey, Guernsey and Isle of Man sheets), counties on a line of their own (Royal Mail PAF Programmers' Guide;
// UPU Ireland sheet), and a postcode with no post town.
internal static partial class Tier2Reader
{
    private sealed partial class Reader
    {
        private static bool IsCountyWord(Tok t) => t.Key is "CO" or "COUNTY" || IsGluedCounty(t);

        // "Co.Louth": the county word and the name are written together.
        private static bool IsGluedCounty(Tok t) => t.Text.Length > 4 && t.Text.StartsWith("Co.", StringComparison.OrdinalIgnoreCase) && char.IsLetter(t.Text[3]);

        // Road types that end a street name and may be followed by the post town on the same line ("14 Mill Lane Henley-on-Thames"). The abbreviation "St" is left out:
        // it is as often the start of a place name ("St Lawrence").
        private static bool IsSplittingRoadType(Tok t) =>
            t.Key != "ST" && T2Rules.FindTailType(t.Key) is { Full: "ROAD" or "STREET" or "LANE" or "AVENUE" or "DRIVE" or "CLOSE" or "CRESCENT" or "TERRACE" or "GARDENS" or "WAY" or "SQUARE" or "PLACE"
                or "COURT" or "RISE" or "GROVE" or "MEWS" or "WALK" or "LAWN" or "LAWNS" or "PARADE" or "ROW" };

        // The number of words at the end of the locality that are the name of the hinted island ("Isle of Man" is three words), or 0.
        private static int TrailingIslandWords(List<Tok> toks, string code)
        {
            string name = code switch { "IM" => "ISLE OF MAN", "JE" => "JERSEY", _ => "GUERNSEY" };
            int words = name.Count(c => c == ' ') + 1;
            return toks.Count > words && T2Rules.Key(Join(toks.GetRange(toks.Count - words, words))) == name ? words : 0;
        }

        // Jersey, Guernsey and the Isle of Man write the name of the island on the line above the postcode.
        private static bool IsIslandName(List<Tok> toks) =>
            toks.Count > 0 && T2Rules.Key(Join(toks)) is "JERSEY" or "GUERNSEY" or "ISLE OF MAN";
        // The number of words at the end of the tokens that name any of the three islands, or 0.
        private static int TrailingIslandAny(List<Tok> toks) =>
            Math.Max(TrailingIslandWords(toks, "IM"), Math.Max(TrailingIslandWords(toks, "JE"), TrailingIslandWords(toks, "GG")));

        // A street and a post town written with no commas before the postcode, nothing else on the line: "Victoria Road Douglas IM1 1AA", "Tromode Road Douglas Isle of Man IM2 5AA".
        // The street ends at the last road type that leaves one to three words for the post town (Royal Mail PAF Programmers' Guide: the thoroughfare, then the post town).
        private static bool SplitInlineStreet(List<Tok> locality, out int streetWords)
        {
            streetWords = 0;
            int end = locality.Count - TrailingIslandAny(locality);
            for (int i = end - 2; i >= 1; i--)
            {
                if (IsSplittingRoadType(locality[i]) && end - i - 1 <= 3 && PlaceFits(locality.GetRange(i + 1, end - i - 1), false, "GB"))
                {
                    streetWords = i + 1;
                    return true;
                }
            }

            return false;
        }
    }
}
