using RecursiveAI.OneContact.Addresses.Streets;
using RecursiveAI.OneContact.Names;

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

        // Omniva writes the postcode before the town ("10145 Tallinn"), but pages also put it after ("Tallinn 10145", "Pärnu mnt 12, Tartu 51010"). Estonian postcodes are five digits,
        // never starting with 0 (UPU Estonia sheet). The unit is rearranged to read postcode first, as for the postcode-last countries; the spans still point at the words in the input.
        // Only for text known to be Estonian: a trailing five-digit number is a ZIP code in the United States.
        private static bool FindEstonianPostcodeLast(List<Unit> units, string? known, out int unitIndex, out int tokenIndex, out PostcodeMatch match)
        {
            unitIndex = tokenIndex = 0;
            match = null!;
            if (known != "EE" || units.Count == 0)
            {
                return false;
            }

            // UPU Estonia sheet: the code may carry the country letters (EE-80010); the match then holds the digits and the prefix, as for Latvia.
            static bool IsCode(string s) => s.Length == 5 && s[0] != '0' && s.All(char.IsAsciiDigit) || s.Length == 8 && s.StartsWith("EE-", StringComparison.OrdinalIgnoreCase) && IsCode(s[3..]);
            static bool Plain(List<Tok> toks) => toks.Count is >= 1 and <= 3 && !toks.Any(t => t.Text.Any(char.IsAsciiDigit)) && !StartsWithSpatial(toks);
            int u = units.Count - 1;
            List<Tok> toks = units[u].Toks;
            PostcodeMatch code = toks[^1].Text.Length == 8 ? new(1, toks[^1].Text[3..], ["EE"], "EE") : new(1, toks[^1].Text, ["EE"], null);
            if (toks.Count == 1 && IsCode(toks[0].Text) && u >= 1 && Plain(units[u - 1].Toks))
            {
                // "Pärnu mnt 12, Tallinn, 10145": the postcode stands alone after a comma.
                units[u - 1] = new Unit([toks[0], .. units[u - 1].Toks]);
                units.RemoveAt(u);
                unitIndex = u - 1;
                match = code;
                return true;
            }

            if (toks.Count < 2 || !IsCode(toks[^1].Text))
            {
                return false;
            }

            // The locality is the plain words after the last number of the line; a line without a number is a locality whole.
            int number = toks.FindLastIndex(t => t.Text.Any(char.IsAsciiDigit) && t.Start != toks[^1].Start);
            List<Tok> locality = toks.GetRange(number + 1, toks.Count - number - 2);
            List<Tok> head = toks.GetRange(0, number + 1);
            if (locality.Count == 0 || !Plain(locality))
            {
                return false;
            }

            units[u] = new Unit([toks[^1], .. locality]);
            if (head.Count > 0)
            {
                units.Insert(u, new Unit(head));
                u++;
            }

            unitIndex = u;
            match = code;
            return true;
        }

        // Omniva: the county (maakond) is the last word group of the last line, but web pages put it on a line of its own after the town: "Pärnu mnt 12, 10148 Tallinn, Harju maakond" and
        // "Pärnu mnt 12, Tallinn 10148, Harju maakond". The county is joined to the line that holds the postcode, which is the form the reader handles. A bare county line is taken only
        // for Estonian text (a hint, or the word maakond itself); the line above must hold the five-digit postcode, first or last.
        private static void MergeTrailingEstonianCounty(List<Unit> units, string? known)
        {
            if (known is not ("EE" or null) || units.Count < 2)
            {
                return;
            }

            List<Tok> last = units[^1].Toks;
            bool county = last.Count == 2 && last[1].Key == "MAAKOND" && !last.Any(t => t.Text.Any(char.IsAsciiDigit))
                || known == "EE" && last.Count == 1 && last[0].Key.Length > 5 && last[0].Key.EndsWith("MAA", StringComparison.Ordinal) && !last[0].Text.Any(char.IsAsciiDigit);
            if (!county)
            {
                return;
            }

            List<Tok> prev = units[^2].Toks;
            static bool IsCode(Tok t) => t.Text.Length == 5 && t.Text[0] != '0' && t.Text.All(char.IsAsciiDigit);
            if (prev.Count < 2)
            {
                return;
            }

            if (IsCode(prev[0]))
            {
                prev.AddRange(last);
            }
            else if (IsCode(prev[^1]))
            {
                // "Tallinn 10148, Harju maakond": the line is rewritten to read postcode, town, county. The town is the plain words after the last other number.
                int number = prev.FindLastIndex(prev.Count - 2, t => t.Text.Any(char.IsAsciiDigit));
                List<Tok> town = prev.GetRange(number + 1, prev.Count - number - 2);
                if (town.Count == 0 || town.Count > 3 || StartsWithSpatial(town))
                {
                    return;
                }

                List<Tok> head = prev.GetRange(0, number + 1);
                units[^2].Toks = [.. head, prev[^1], .. town, .. last];
            }
            else
            {
                return;
            }

            units.RemoveAt(units.Count - 1);
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
        private Line? ParseNorth(List<Tok> toks, T2Country country)
        {
            for (int h = 1; h < toks.Count; h++)
            {
                if (!IsNorthNumber(toks[h].Text) || !toks.Take(h).Any(t => t.Text.Any(char.IsLetter)))
                {
                    continue;
                }

                // Norwegian numbered roads are named "Vei 238" and "Vei 39040": the number is part of the name, the house number follows it.
                if (country.Profile == "no" && h == 1 && toks[0].Key is "VEI" or "VEG" && h + 1 < toks.Count && IsNorthNumber(toks[h + 1].Text))
                {
                    continue;
                }

                // Exactly three words before the number that start with a given name and do not end in a street type word ("Nils Larsen Nestangen 58") are a person written in front of
                // a one-word street name with no comma or line break between them (longer names are left to the rules: a four-word street name is as likely as a person with a middle name). These languages write a street name as one word or end it in a type word (Karl Johans gate,
                // Ole Bulls plass), so where the person ends cannot be told from the words: the line is left to the model instead of reading the person as part of the street.
                if (h == 3 && country.Profile is "no" or "dk" or "se" or "fi" && NameLexicon.IsGivenName(NameFolding.LettersAndDigits(toks[0].Text))
                    && T2Rules.FindType(country, toks[h - 1].Key) is null && !InferenceTables.Instance.IsStreetWord(country.Code, toks[h - 1].Key) && toks.Take(h).All(t => char.IsUpper(t.Text[0])))
                {
                    return null;
                }

                Line? line = NorthLine(toks, h, country);
                if (line is not null)
                {
                    return line;
                }
            }

            return null;
        }

        private Line? NorthLine(List<Tok> toks, int h, T2Country country)
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
            Tok gapFrom = toks[h];
            List<Piece> peeled = [];
            for (int s = 0; s < segments.Count; s++)
            {
                (List<Tok> segment, bool comma) = segments[s];

                // The Baltic posts separate the house and the apartment by a dash, often with spaces ("2 - 22"): the dash is the separator, not a word, and stays where it was written.
                string before = comma ? ", " : " ";
                List<Tok> words = segment.Where(t => !(t.Text.Length == 1 && t.Text[0] is '-' or '–' or '—')).ToList();
                if (words.Count == 0)
                {
                    gapFrom = segment[^1];
                    continue;
                }

                if (!comma && words[0].Start > gapFrom.End)
                {
                    string gap = text.Substring(gapFrom.End, words[0].Start - gapFrom.End);
                    int dashAt = gap.IndexOfAny(['-', '–', '—']);
                    if (dashAt >= 0)
                    {
                        before = (char.IsWhiteSpace(gap[0]) ? " " : string.Empty) + gap[dashAt] + (char.IsWhiteSpace(gap[^1]) ? " " : string.Empty);
                    }
                }

                gapFrom = segment[^1];
                segment = words;

                // Lithuania: a village (k.) and a municipality (sav.) may follow the house number on the same line ("Miško a. 7 Radviliškio r. sav.").
                if (country.Profile == "lt" && s == segments.Count - 1 && !segment.All(NorthTailOk) && LithuanianPlaces(segment, out int placesFrom) is { } places)
                {
                    foreach (List<Tok> place in places)
                    {
                        peeled.Add(LithuanianPlacePiece(place, country));
                    }

                    segment = segment.GetRange(0, placesFrom);
                    if (segment.Count == 0)
                    {
                        continue;
                    }
                }

                // "korter 8 Mustamäe linnaosa": a district or municipality name closes the last segment; it is the dependent locality, not part of the unit.
                if (s == segments.Count - 1 && segment.Count >= 3 && !segment.All(NorthTailOk) && IsDependentLocality(segment.GetRange(segment.Count - 2, 2), country) && segment.Take(segment.Count - 2).All(NorthTailOk))
                {
                    List<Tok> place = segment.GetRange(segment.Count - 2, 2);
                    string placeWritten = Join(place);
                    peeled.Add(new Piece(StreetPartRole.Other, placeWritten, placeWritten, T2Text.Title(placeWritten, capitalsKept: true), Span(place), "t2-dependent-locality"));
                    segment = segment.GetRange(0, segment.Count - 2);
                }

                // Latvian building (korpuss) after the house number: "5 k-8" is one house number.
                if (!comma && country.Profile == "lv" && segment[0].Text.Length >= 2 && segment[0].Text[0] is 'k' or 'K' && segment[0].Text.Skip(1).All(c => char.IsAsciiDigit(c) || c == '-') && segment[0].Text.Any(char.IsAsciiDigit))
                {
                    houseToks.Add(segment[0]);
                    segment = segment.GetRange(1, segment.Count - 1);
                    if (segment.Count == 0)
                    {
                        continue;
                    }
                }

                if (segment.All(NorthTailOk))
                {
                    // A single letter right after the number belongs to it (Karl Johansgate 25 B).
                    if (!comma && segment[0].Text.Length == 1 && char.IsLetter(segment[0].Text[0]))
                    {
                        houseToks.Add(segment[0]);
                        segment = segment.GetRange(1, segment.Count - 1);
                    }

                    if (segment.Count >= 2 && (segment[0].Key is "LGH" or "DZ" or "KORP" or "KORTER" or "BYGNING" or "INNGANG" or "LEIL" or "LEILIGHET" || country.Profile == "lt" && segment[0].Key is "BUTAS" or "B"))
                    {
                        // lgh (PostNord), dz. (Latvian rules), korp.: the word is the designator, what follows it the number.
                        List<Tok> number = segment.GetRange(1, segment.Count - 1);
                        tail.Add(new Piece(StreetPartRole.UnitDesignator, segment[0].Text, segment[0].Text, segment[0].Text, segment[0].Span, "t2-floor-and-door", before));
                        tail.Add(new Piece(StreetPartRole.UnitNumber, Join(number), Join(number), Join(number), Span(number), "t2-floor-and-door"));
                    }
                    else if (segment.Count > 0)
                    {
                        // Floor and door ("1. th", "st. a19", "13", a dwelling number H0302) are one unit number as the writer set it.
                        string unit = Join(segment);
                        tail.Add(new Piece(StreetPartRole.UnitNumber, unit, unit, unit, Span(segment), "t2-floor-and-door", before));
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

            tail.AddRange(peeled);

            List<Tok> nameToks = toks.GetRange(0, h);
            string nameWritten = Join(nameToks);
            List<Piece> pieces = [new Piece(StreetPartRole.StreetName, nameWritten, nameWritten, DisplayName(nameWritten), Span(nameToks), "t2-street-name")];
            string houseWritten = Join(houseToks);
            // Estonia also writes the apartment after a slash ("46/49"); in Latvia a slash joins a double number ("48/50").
            int dash = country.Profile is "ee" or "lv" or "lt" && houseToks.Count == 1 ? houseWritten.LastIndexOfAny(country.Profile is "ee" or "lt" ? ['-', '–', '—', '/'] : ['-', '–', '—']) : -1;
            if (dash > 0 && dash < houseWritten.Length - 1 && houseWritten[(dash + 1)..].All(c => char.IsAsciiDigit(c) || char.IsAsciiLetter(c)) && char.IsAsciiDigit(houseWritten[dash + 1]))
            {
                // The apartment follows the house number after a dash (UPU Estonia and Lithuania sheets, Latvian rules): two parts, written as one.
                string house = houseWritten[..dash];
                string apartment = houseWritten[(dash + 1)..];
                int start = houseToks[0].Start;
                pieces.Add(new Piece(StreetPartRole.HouseNumber, house, house, house, new SourceSpan(start, dash), "t2-house-number"));
                pieces.Add(new Piece(StreetPartRole.UnitNumber, apartment, apartment, apartment, new SourceSpan(start + dash + 1, apartment.Length), "t2-apartment", houseWritten[dash].ToString()));
            }
            else
            {
                pieces.Add(new Piece(StreetPartRole.HouseNumber, houseWritten, houseWritten, houseWritten, Span(houseToks), "t2-house-number"));
            }

            pieces.AddRange(tail);
            return new Line(StreetLineKind.Street, pieces);
        }

        // Swedish company registration numbers (organisationsnummer, six digits, a hyphen, four digits: 556123-4567) stand on pages next to the address, often with a label
        // ("org.nr"). They are not house numbers, floors or apartments, so they are taken out of the line they stand in and kept as a line of their own, first.
        private static void SplitOrganizationNumber(List<Unit> before)
        {
            static bool IsNumber(string s) => s.Length == 11 && s[6] == '-' && s[..6].All(char.IsAsciiDigit) && s[7..].All(char.IsAsciiDigit);
            static bool IsLabel(Tok t) => t.Key is "ORGNR" or "ORG" or "NR" or "NUMMER" or "ORGNUMMER" or "ORGANISATIONSNUMMER" or "ORGANISATIONSNR" or "REG" or "REGNR";
            List<Unit> numbers = [];
            List<Unit> rest = [];
            foreach (Unit unit in before)
            {
                List<Tok> toks = unit.Toks;
                int at = toks.FindIndex(t => IsNumber(t.Text));
                if (at < 0)
                {
                    rest.Add(unit);
                    continue;
                }

                int from = at;
                while (from > 0 && IsLabel(toks[from - 1]))
                {
                    from--;
                }

                numbers.Add(new Unit(toks.GetRange(from, at - from + 1)) { Leading = true });
                toks.RemoveRange(from, at - from + 1);
                if (toks.Count > 0)
                {
                    rest.Add(unit);
                }
            }

            if (numbers.Count > 0)
            {
                before.Clear();
                before.AddRange(numbers);
                before.AddRange(rest);
            }
        }

        // On one line a company name that runs into the street ("Nielsen Byg ApS Amagerbrogade 21") is split after its legal form (ApS, A/S, AB, Oy, AS, ehf), when the rest is a street line.
        private void SplitCompanyPrefix(List<Unit> before, T2Country country)
        {
            for (int i = 0; i < before.Count; i++)
            {
                List<Tok> toks = before[i].Toks;
                for (int j = 1; j < toks.Count - 1; j++)
                {
                    if (!T2Rules.NorthLegalForms.Contains(toks[j].Key))
                    {
                        continue;
                    }

                    List<Tok> rest = toks.GetRange(j + 1, toks.Count - j - 1);
                    if (rest.Any(t => IsNorthNumber(t.Text)) && ParseNorth(rest, country) is not null)
                    {
                        before[i] = new Unit(rest);
                        before.Insert(i, new Unit(toks.GetRange(0, j + 1)));
                        i++;
                    }

                    break;
                }
            }
        }

        // Latvia: "Valmiera, Valmieras nov., LV-4201" is a locality and its municipality (novads); a parish (pag.) that stands before the locality is the dependent locality.
        // Estonia: "48103 Põltsamaa Jõgeva maakond" is a locality and its county (maakond). Without a locality besides the municipality, the municipality stays the locality.
        private void NorthLocality(T2Country country, List<Unit> before, ref List<Tok> localityToks, out List<Tok>? admin, out List<Tok>? dependent, out List<Tok>? houseName)
        {
            admin = null;
            dependent = null;
            houseName = null;
            if (country.Profile == "ee")
            {
                // "Tallinn 10111 Harju maakond" and "Tartu 51003 Tartumaa": the town stands before the postcode and the county (maakond, or one word ending in maa) after it.
                bool county = localityToks.Count == 2 && localityToks[^1].Key == "MAAKOND" || localityToks.Count == 1 && localityToks[0].Key.Length > 5 && localityToks[0].Key.EndsWith("MAA", StringComparison.Ordinal);
                if (county && _headSplit && before.Count > 0 && before[^1].Toks.Count <= 3
                    && !before[^1].Toks.Any(t => t.Text.Any(char.IsAsciiDigit)) && !StartsWithSpatial(before[^1].Toks))
                {
                    admin = localityToks;
                    localityToks = before[^1].Toks;
                    before.RemoveAt(before.Count - 1);
                    return;
                }

                // "Kodu küla, Rae vald, 75301 Harju maakond": a county alone after the postcode, with a village line before it, is the county of that village (Omniva: village and
                // rural municipality, the postcode, the town or county).
                if (localityToks.Count == 2 && localityToks[^1].Key == "MAAKOND" && !_headSplit)
                {
                    int village = before.FindLastIndex(u => u.Toks.Count is >= 2 and <= 3 && u.Toks[^1].Key is "KULA" or "ALEVIK" or "ALEV" or "ASULA" && !u.Toks.Any(t => t.Text.Any(char.IsAsciiDigit)));
                    if (village >= 0 && village >= before.Count - 2)
                    {
                        admin = localityToks;
                        localityToks = before[village].Toks;
                        before.RemoveAt(village);
                        return;
                    }
                }

                // "Ülikooli 18, Tartu, Tartu maakond, 51003" and "..., Tallinn, Harju maakond 10148": the county stands between the town and the postcode, so the plain town word before
                // it is the locality. A rural municipality, village or town-with-status word (vald, küla, linn ...) before the county is read as before.
                if (!_headSplit && (localityToks.Count == 2 && localityToks[^1].Key == "MAAKOND" || localityToks.Count == 1 && localityToks[0].Key.Length > 5 && localityToks[0].Key.EndsWith("MAA", StringComparison.Ordinal))
                    && before.Count > 1 && before[^1].Toks.Count <= 2 && !before[^1].Toks.Any(t => t.Text.Any(char.IsAsciiDigit)) && !StartsWithSpatial(before[^1].Toks)
                    && before[^1].Toks[^1].Key is not ("VALD" or "LINN" or "LINNAOSA" or "KULA" or "ALEVIK" or "ALEV" or "ASULA" or "MAAKOND"))
                {
                    admin = localityToks;
                    localityToks = before[^1].Toks;
                    before.RemoveAt(before.Count - 1);
                    return;
                }

                if (localityToks.Count >= 3 && localityToks[^1].Key == "MAAKOND")
                {
                    admin = localityToks.GetRange(localityToks.Count - 2, 2);
                    localityToks = localityToks.GetRange(0, localityToks.Count - 2);
                }

                return;
            }

            if (country.Profile != "lv" || localityToks.Count == 0)
            {
                return;
            }

            List<List<Tok>> segments = [];
            for (int i = 0; i < localityToks.Count; i++)
            {
                if (i == 0 || CommaBetween(localityToks[i - 1], localityToks[i]))
                {
                    segments.Add([]);
                }

                segments[^1].Add(localityToks[i]);
            }

            static bool IsMunicipality(List<Tok> s) => s[^1].Key is "NOV" or "NOVADS" or "NOVADA";
            static bool IsParish(List<Tok> s) => s[^1].Key is "PAG" or "PAGASTS" or "PAGASTA";
            if (segments.Count >= 2 && IsMunicipality(segments[^1]))
            {
                admin = segments[^1];
                segments.RemoveAt(segments.Count - 1);
                if (segments.Count >= 2 && IsParish(segments[0]))
                {
                    dependent = segments[0];
                    segments.RemoveAt(0);
                }

                localityToks = [.. segments.SelectMany(s => s)];
            }
            else if (segments.Count == 1 && IsMunicipality(segments[0]) && !_multiLine && before.Count > 0)
            {
                // One line, cut at the commas: the locality is the unit before the municipality.
                List<Tok> previous = before[^1].Toks;
                bool parishInside = previous.FindIndex(t => t.Key is "PAG" or "PAGASTS" or "PAGASTA") >= 2;
                bool locality = !previous.Any(t => t.Text.Any(char.IsAsciiDigit)) && !StartsWithSpatial(previous) && (previous.Count <= 2 || previous.Count == 3 && !IsParish(previous) || parishInside && previous.Count <= 5);
                if (locality)
                {
                    admin = localityToks;
                    localityToks = previous;
                    before.RemoveAt(before.Count - 1);
                }
            }

            // "Saulītes Sēļu pag. Sēļi": a house name, the parish (a word and pag.) and the locality, with no comma between them. Without a locality after the parish, the parish is the locality.
            if (admin is not null || segments.Count == 1)
            {
                int at = localityToks.FindIndex(t => t.Key is "PAG" or "PAGASTS" or "PAGASTA");
                if (dependent is null && at >= 2 && !localityToks.Any(t => t.Text.Any(char.IsAsciiDigit)))
                {
                    List<Tok> parish = localityToks.GetRange(at - 1, 2);
                    List<Tok> after = localityToks.GetRange(at + 1, localityToks.Count - at - 1);
                    houseName = localityToks.GetRange(0, at - 1);
                    if (after.Count > 0)
                    {
                        dependent = parish;
                    }

                    localityToks = after.Count > 0 ? after : parish;
                }
            }
        }

        // "Vainukivi Järva vald" and "Saulītes Sēļu pag.": a house name and the parish or municipality (one word and pag., vald, linn) written on one line are two lines.
        private void SplitHouseNameAndParish(List<Unit> before, T2Country country)
        {
            for (int i = 0; i < before.Count; i++)
            {
                List<Tok> toks = before[i].Toks;
                if (country.Profile != "lt" && toks.Count == 3 && IsDependentLocality(toks.GetRange(1, 2), country) && !toks.Any(t => t.Text.Any(char.IsAsciiDigit)) && ParseDeliveryLine(toks, country.Code, country, allowStreet: true) is null)
                {
                    before[i] = new Unit(toks.GetRange(1, 2));
                    before.Insert(i, new Unit(toks.GetRange(0, 1)));
                    i++;
                }
            }
        }

        // Latvian rules for a unified postal address: in the countryside the house name (mājvārds) stands where the street would; with a parish (pag.) named and no street line,
        // the line right above the parish is the house name. Returns the index of that line in <paramref name="before"/>, or -1.
        private static int NorthHouseName(List<Unit> before, List<Line?> parsed, List<Tok> localityToks, List<Tok>? dependent, List<Tok>? admin, T2Country country)
        {
            static bool IsParish(List<Tok> t) => t[^1].Key is "PAG" or "PAGASTS" or "PAGASTA";
            int parish = before.FindLastIndex(u => IsDependentLocality(u.Toks, country));
            // Estonia: a village or country village (küla, alevik) as the locality and a line above besides the addressee: the farm or house name (UPU Estonia sheet, rural region).
            bool village = country.Profile == "ee" && before.Count >= 2 && localityToks.Count > 0 && localityToks[^1].Key is "KULA" or "ALEVIK" or "ALEV" or "ASULA";
            bool rural = parish >= 0 || village || country.Profile == "lv" && (dependent is not null || IsParish(localityToks) || admin is not null);
            int at = parish >= 0 ? parish - 1 : before.Count - 1;
            if (!rural || at < 0 || parsed[at] is not null || before[at].Toks.Count > 3 || before[at].Toks.Any(t => t.Text.Any(char.IsAsciiDigit)) || StartsWithSpatial(before[at].Toks))
            {
                return -1;
            }

            Piece name = NamePiece(StreetPartRole.StreetName, before[at].Toks, country, "t2-house-name");
            parsed[at] = new Line(StreetLineKind.Street, [name]);
            return at;
        }

        // A line the rules cannot place: a parish (pag.) or rural municipality (vald) name is the dependent locality, anything else is kept as written.
        private static Piece NorthOtherPiece(List<Tok> toks, T2Country country)
        {
            Piece piece = OtherPiece(toks, country, afterStreet: true);
            return country.Profile == "lt" && IsLithuanianMunicipality(toks) ? piece with { Rule = "t2-admin-area" } : IsDependentLocality(toks, country) ? piece with { Rule = "t2-dependent-locality" } : piece;
        }

        // Lithuania: a village is written with k. (kaimas) after its name, a municipality with sav. (savivaldybė, "r. sav." for a district municipality, "m. sav." for a city one).
        // The marker words make a line certain; the UPU Lithuania sheet prints the rural address with the municipality in the postcode line ("RASEINIU R. SAV.").
        private static bool IsLithuanianVillage(List<Tok> toks) => toks.Count is >= 2 and <= 4 && toks[^1].Key == "K" && !toks.Any(t => t.Text.Any(char.IsAsciiDigit));

        private static bool IsLithuanianMunicipality(List<Tok> toks) => toks.Count is >= 2 and <= 4 && toks[^1].Key == "SAV" && !toks.Any(t => t.Text.Any(char.IsAsciiDigit));

        private static Piece LithuanianPlacePiece(List<Tok> toks, T2Country country)
        {
            Piece piece = OtherPiece(toks, country, afterStreet: true);
            return piece with { Rule = IsLithuanianMunicipality(toks) ? "t2-admin-area" : "t2-dependent-locality" };
        }

        // The words after a house number that are villages and municipalities only: from the first word that is not a floor or door word to the end, cut after each k. and sav.
        private static List<List<Tok>>? LithuanianPlaces(List<Tok> segment, out int start)
        {
            start = segment.FindIndex(t => !NorthTailOk(t));
            if (start < 0)
            {
                return null;
            }

            List<List<Tok>> places = [];
            List<Tok> current = [];
            for (int i = start; i < segment.Count; i++)
            {
                current.Add(segment[i]);
                if (segment[i].Key is "K" or "SAV")
                {
                    places.Add(current);
                    current = [];
                }
            }

            return current.Count == 0 && places.Count > 0 && places.All(p => IsLithuanianVillage(p) || IsLithuanianMunicipality(p)) ? places : null;
        }

        // In lines, "Pienionių k., Anykščių r. sav." is one line with a comma inside: a village and a municipality, each a line of its own.
        private static void SplitLithuanianPlaces(List<Unit> before, T2Country country)
        {
            if (country.Profile != "lt")
            {
                return;
            }

            for (int i = 0; i < before.Count; i++)
            {
                List<Tok> toks = before[i].Toks;
                if (toks.Any(t => t.Text.Any(char.IsAsciiDigit)) || toks[^1].Key is not ("K" or "SAV"))
                {
                    continue;
                }

                List<List<Tok>> places = [];
                List<Tok> current = [];
                foreach (Tok t in toks)
                {
                    current.Add(t);
                    if (t.Key is "K" or "SAV")
                    {
                        places.Add(current);
                        current = [];
                    }
                }

                if (places.Count < 2 || current.Count > 0 || !places.All(p => IsLithuanianVillage(p) || IsLithuanianMunicipality(p)))
                {
                    continue;
                }

                before.RemoveAt(i);
                before.InsertRange(i, places.Select(p => new Unit(p)));
                i += places.Count - 1;
            }
        }

        // On one line "Sakalauskas ir Ko, MB, Aušros g. 23": the legal form after a comma belongs to the company name before it.
        private static void MergeLithuanianLegalSuffix(List<Unit> before, T2Country country)
        {
            if (country.Profile != "lt")
            {
                return;
            }

            for (int i = 1; i < before.Count; i++)
            {
                List<Tok> t = before[i].Toks;
                if (t.Count == 1 && T2Rules.NorthLegalForms.Contains(t[0].Key) && !before[i - 1].Toks.Any(x => x.Text.Any(char.IsAsciiDigit)))
                {
                    before[i - 1].Toks = [.. before[i - 1].Toks, .. t];
                    before.RemoveAt(i);
                    i--;
                }
            }
        }

        // A parish (Latvia: pag.) or a rural municipality or city district (Estonia: vald, linnaosa) name stands for the dependent locality.
        private static bool IsDependentLocality(List<Tok> toks, T2Country country)
        {
            if (country.Profile == "lt")
            {
                return IsLithuanianVillage(toks);
            }

            bool dependent = country.Profile == "lv" && toks[^1].Key is "PAG" or "PAGASTS" or "PAGASTA" || country.Profile == "ee" && toks[^1].Key is "VALD" or "LINN" or "LINNAOSA";
            return dependent && toks.Count <= 2 && !toks.Any(t => t.Text.Any(char.IsAsciiDigit));
        }

        // On one line a name and the number that follows it may be cut by a comma ("Folkparksgatan, 40 lgh 1702"): they belong together when the name has no digit.
        private static void MergeNorthNameAndNumber(List<Unit> before)
        {
            for (int i = 1; i < before.Count; i++)
            {
                List<Tok> t = before[i].Toks;
                List<Tok> name = before[i - 1].Toks;
                if (IsNorthNumber(t[0].Text) && t.Skip(1).All(NorthTailOk) && name.Any(x => x.Text.Any(char.IsLetter)) && !name.Any(x => x.Text.Any(char.IsAsciiDigit)) && !StartsWithSpatial(name))
                {
                    before[i - 1].Toks = [.. name, .. t];
                    before.RemoveAt(i);
                    i--;
                }
            }
        }

        // On one line the commas cut the address into units: the floor and door that follow the house number go back to the street line, with their comma.
        private static void MergeNorthFloorAndDoor(List<Unit> before, T2Country country)
        {
            for (int i = 1; i < before.Count; i++)
            {
                List<Tok> t = before[i].Toks;
                bool unit = t.Count is >= 1 and <= 3 && t.All(NorthTailOk) && (t[0].Text.Any(char.IsAsciiDigit) || T2Rules.NorthUnitWords.Contains(t[0].Key) || country.Profile == "lt" && t.Count == 2 && t[0].Key == "B")
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
