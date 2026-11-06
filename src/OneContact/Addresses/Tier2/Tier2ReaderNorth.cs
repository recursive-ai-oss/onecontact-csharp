using RecursiveAI.OneContact.Addresses.Streets;

namespace RecursiveAI.OneContact.Addresses.Tier2;

// The northern group (Denmark, Sweden, Finland, Norway, Iceland, Estonia, Latvia, Lithuania): postcode forms the first groups do not have, and the delivery line,
// which in these countries has the street name first and the house number after it, then the floor, door or apartment.
internal static partial class Tier2Reader
{
    private sealed partial class Reader
    {
        // UPU Sweden sheet: five digits with a space between the third and the fourth (123 45), left of the locality. UPU Iceland sheet: three digits (220).
        // Three digits alone are a postcode only when the country is known to be Iceland: they would be a house number anywhere else.
        private static PostcodeMatch? NorthMatchAt(List<Tok> toks, int i, string t, string? known)
        {
            string digits = t;
            string? prefix = null;
            int dash = t.IndexOf('-', StringComparison.Ordinal);
            if (dash is 1 or 2 && t[..dash].All(char.IsLetter))
            {
                prefix = t[..dash].ToUpperInvariant();
                digits = t[(dash + 1)..];
            }

            if (digits.Length != 3 || !digits.All(char.IsAsciiDigit))
            {
                return null;
            }

            bool swedishPrefix = prefix is "SE" or "S";
            if ((swedishPrefix || prefix is null && known is null or "SE") && i + 1 < toks.Count && toks[i + 1].Text.Length == 2 && toks[i + 1].Text.All(char.IsAsciiDigit))
            {
                return new PostcodeMatch(2, digits + " " + toks[i + 1].Text, ["SE"], swedishPrefix ? "SE" : null);
            }

            if (prefix == "IS" || prefix is null && known == "IS")
            {
                return new PostcodeMatch(1, digits, ["IS"], prefix);
            }

            return null;
        }

        // UPU Latvia sheet: the postcode is LV, a dash and four digits, to the right of the locality name and separated from it by a comma (RIGA, LV-1050).
        private static PostcodeMatch? LatvianPostcodeAt(List<Tok> toks)
        {
            string last = toks[^1].Text.Replace('\u2013', '-').Replace('\u2014', '-');
            if (last.Length == 7 && last[..3].Equals("LV-", StringComparison.OrdinalIgnoreCase) && last[3..].All(char.IsAsciiDigit))
            {
                return new PostcodeMatch(1, last[3..], ["LV"], "LV");
            }

            if (toks.Count >= 2 && toks[^2].Text.Equals("LV", StringComparison.OrdinalIgnoreCase) && last.Length == 4 && last.All(char.IsAsciiDigit))
            {
                return new PostcodeMatch(2, last, ["LV"], "LV");
            }

            return null;
        }

        private bool CommaBetween(Tok previous, Tok next) =>
            next.Start > previous.End && text.AsSpan(previous.End, next.Start - previous.End).Contains(',');

        // A house number as these posts write it: 25, 10a, 2B, and with the apartment after a dash (Latvian rules, UPU Estonia and Lithuania sheets: 90-23, 6-1, 40-12),
        // a typeset en or em dash included.
        private static bool IsNorthNumber(string s) =>
            s.Length > 0 && char.IsAsciiDigit(s[0]) && IsHouseNumber(s.Replace('\u2013', '-').Replace('\u2014', '-'));

        // What may follow the house number on the street line: digits (floor, door, apartment), a letter or two (25 B, th., tv.), and the unit words of the guides.
        private static bool NorthTailOk(Tok t) =>
            t.Text.Any(char.IsLetterOrDigit)
            && (t.Text.Any(char.IsAsciiDigit) || T2Rules.NorthUnitWords.Contains(t.Key) || t.Text.Count(char.IsLetter) <= 2 && t.Text.Length <= 4);

        // Name, house number, floor and door: "Kastanievej 15, 2, Agerskov" (UPU Denmark), "Mäkelänkatu 25 B 13" (UPU Finland), "Kattstigen 5 lgh 1234" (PostNord),
        // "Brīvības ielā 90-23" (Latvian rules), "Laisvės pr. 40-12" (UPU Lithuania). The street name has no required type: these languages write it as one word or with a
        // type word that is part of the name.
        private Line? ParseNorth(List<Tok> toks)
        {
            for (int h = 1; h < toks.Count; h++)
            {
                if (!IsNorthNumber(toks[h].Text) || !toks.Take(h).Any(t => t.Text.Any(char.IsLetter)))
                {
                    continue;
                }

                Line? line = NorthLine(toks, h);
                if (line is not null)
                {
                    return line;
                }
            }

            return null;
        }

        private Line? NorthLine(List<Tok> toks, int h)
        {
            // What follows the number, cut at the commas the writer set: Danmarks Adresser separates floor, door and the supplementary town name by commas.
            List<(List<Tok> Toks, bool Comma)> segments = [];
            Tok previous = toks[h];
            for (int i = h + 1; i < toks.Count; i++)
            {
                bool comma = CommaBetween(previous, toks[i]);
                if (segments.Count == 0 || comma)
                {
                    segments.Add(([toks[i]], comma));
                }
                else
                {
                    segments[^1].Toks.Add(toks[i]);
                }

                previous = toks[i];
            }

            List<Tok> houseToks = [toks[h]];
            List<Piece> tail = [];
            for (int s = 0; s < segments.Count; s++)
            {
                (List<Tok> segment, bool comma) = segments[s];
                string before = comma ? ", " : " ";
                if (segment.All(NorthTailOk))
                {
                    // A single letter right after the number belongs to it (Karl Johansgate 25 B).
                    if (!comma && segment[0].Text.Length == 1 && char.IsLetter(segment[0].Text[0]))
                    {
                        houseToks.Add(segment[0]);
                        segment = segment.GetRange(1, segment.Count - 1);
                    }

                    if (segment.Count > 0)
                    {
                        string unit = Join(segment);
                        tail.Add(new Piece(StreetPartRole.UnitDesignator, unit, unit, unit, Span(segment), "t2-floor-and-door", before));
                    }

                    continue;
                }

                // A town or village name after the last comma (Danmarks Adresser: the supplementary town name) stays on the line as written.
                if (s == segments.Count - 1 && comma && segment.Count <= 3 && !segment.Any(t => t.Text.Any(char.IsAsciiDigit)))
                {
                    string place = Join(segment);
                    tail.Add(new Piece(StreetPartRole.Other, place, place, T2Text.Title(place, capitalsKept: true), Span(segment), "t2-line", before));
                    continue;
                }

                return null;
            }

            List<Tok> nameToks = toks.GetRange(0, h);
            string nameWritten = Join(nameToks);
            string houseWritten = Join(houseToks);
            List<Piece> pieces =
            [
                new Piece(StreetPartRole.StreetName, nameWritten, nameWritten, DisplayName(nameWritten), Span(nameToks), "t2-street-name"),
                new Piece(StreetPartRole.HouseNumber, houseWritten, houseWritten, houseWritten, Span(houseToks), "t2-house-number"),
                .. tail,
            ];
            return new Line(StreetLineKind.Street, pieces);
        }

        // On one line the commas cut the address into units: the floor and door that follow the house number go back to the street line, with their comma.
        private static void MergeNorthFloorAndDoor(List<Unit> before)
        {
            for (int i = 1; i < before.Count; i++)
            {
                List<Tok> t = before[i].Toks;
                bool unit = t.Count is >= 1 and <= 3 && t.All(NorthTailOk) && (t[0].Text.Any(char.IsAsciiDigit) || T2Rules.NorthUnitWords.Contains(t[0].Key))
                    && before[i - 1].Toks.Any(x => IsNorthNumber(x.Text)) && !StartsWithSpatial(before[i - 1].Toks);
                if (unit)
                {
                    before[i - 1].Toks = [.. before[i - 1].Toks, .. t];
                    before.RemoveAt(i);
                    i--;
                }
            }
        }
    }
}
