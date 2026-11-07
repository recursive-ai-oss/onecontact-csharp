using System.Text;
using RecursiveAI.OneContact.Addresses.Expansion;
using RecursiveAI.OneContact.Addresses.Streets;

namespace RecursiveAI.OneContact.Addresses.Tier2;

// Poland, the Czech Republic, Slovakia, Slovenia, Croatia, Hungary, Romania, Mexico and Australia.
// Sources: the UPU country sheets; Poczta Polska's rules for addressing letters; Czech Post's recommended address formats; Magyar Posta's addressing notice;
// Australia Post's Correct Addressing brochure and Address Presentation Standards. See docs/address-rules-tier2-c-south.md.
internal static partial class Tier2Reader
{
    private sealed partial class Reader
    {
        // Hungary: the locality is printed above the street line. Where the reader moved it, this is the number of lines that stand above it in the input; -1 means directly above the street line.
        private int _huLocalityIndex = -1;

        // Mexico: the state abbreviation when it stood on a line of its own after the postcode line (single-line text splits at the comma before it).
        private List<Tok>? _state;
        private string _stateAbbreviation = string.Empty;

        // ---- postcodes ---------------------------------------------------------------------------------------------------------------------------------

        // Forms of the last group: Poland (UPU Poland sheet: five digits with a dash between the second and third), the Czech Republic and Slovakia (UPU sheets: a space between the third
        // and fourth digit), Romania (UPU Romania sheet: six digits). The country the text is known to be (hint or name) must agree, so a form never reads another country's text.
        private static PostcodeMatch? MatchCentralForm(List<Tok> toks, int i, string digits, string? prefixCountry, string? known)
        {
            if (digits.Length == 6 && char.IsAsciiDigit(digits[0]) && char.IsAsciiDigit(digits[1]) && digits[2] is '-' or '\u2013' or '\u2212' && digits[3..].All(char.IsAsciiDigit)
                && prefixCountry is null or "PL" && known is null or "PL")
            {
                return new PostcodeMatch(1, digits[..2] + "-" + digits[3..], ["PL"], prefixCountry);
            }

            // Poczta Polska writes the code with its dash; written without it, five digits are Polish only when the text is known to be Polish, because other countries share the form.
            if (known == "PL" && prefixCountry is null or "PL" && digits.Length == 5 && digits.All(char.IsAsciiDigit))
            {
                return new PostcodeMatch(1, digits[..2] + "-" + digits[2..], ["PL"], prefixCountry);
            }

            if (known is null or "CZ" or "SK" && prefixCountry is null or "CZ" or "SK")
            {
                List<string> candidates = prefixCountry is not null ? [prefixCountry] : known is "CZ" or "SK" ? [known] : ["CZ", "SK"];
                if (digits.Length == 3 && digits.All(char.IsAsciiDigit) && i + 1 < toks.Count && toks[i + 1].Text.Length == 2 && toks[i + 1].Text.All(char.IsAsciiDigit))
                {
                    return new PostcodeMatch(2, digits + " " + toks[i + 1].Text, candidates, prefixCountry);
                }

                // Written without the space: only when the country is known, because five digits are shared with several countries.
                if (known is "CZ" or "SK" && digits.Length == 5 && digits.All(char.IsAsciiDigit))
                {
                    return new PostcodeMatch(1, digits[..3] + " " + digits[3..], candidates, prefixCountry);
                }
            }

            if (digits.Length == 6 && digits.All(char.IsAsciiDigit) && prefixCountry is null or "RO" && known is null or "RO")
            {
                return new PostcodeMatch(1, digits, ["RO"], prefixCountry);
            }

            return null;
        }

        // UPU Australia sheet: "NORTH SYDNEY NSW 2060": the postcode is four digits after the state abbreviation, which is capitals. The unit is rearranged to read postcode,
        // state, locality, as the other countries' last lines do; the spans still point at the words in the input.
        private bool FindAustralian(List<Unit> units, string? known, out int unitIndex, out int tokenIndex, out PostcodeMatch match)
        {
            unitIndex = tokenIndex = 0;
            match = null!;
            if (known is not null and not "AU" || units.Count == 0)
            {
                return false;
            }

            // Written on one line, a comma before the state puts "NSW 2827" in a unit of its own ("Downer, ACT 2602"); it belongs to the locality before it.
            int u = units.Count - 1;
            if (units.Count >= 2 && units[u].Toks.Count is >= 2 and <= 4 && IsAustralianPostcode(units[u].Toks[^1].Text) && AustralianState(units[u].Toks, units[u].Toks.Count - 1, known, out _, out _) == 0
                && !units[u - 1].Toks.Any(t => IsAustralianPostcode(t.Text)))
            {
                units[u - 1] = new Unit([.. units[u - 1].Toks, .. units[u].Toks]);
                units.RemoveAt(u);
                u--;
            }
            else if (units.Count >= 2 && units[u].Toks.Count == 1 && IsAustralianPostcode(units[u].Toks[0].Text) && !units[u - 1].Toks.Any(t => IsAustralianPostcode(t.Text)))
            {
                // Commas on both sides of the state ("Sydney NSW, 2000", "Sydney, NSW, 2000", the way web forms print it) set the state and the postcode in units of their own.
                // Each belongs to the locality before it; the state must stand alone or end the unit before the postcode, so a street line is never taken for a locality.
                List<Tok> two = [.. units[u - 1].Toks, .. units[u].Toks];
                List<Tok>? three = units.Count >= 3 && !units[u - 2].Toks.Any(t => IsAustralianPostcode(t.Text)) ? [.. units[u - 2].Toks, .. two] : null;
                if (AustralianState(two, two.Count - 1, known, out _, out _) >= 1)
                {
                    units[u - 1] = new Unit(two);
                    units.RemoveAt(u);
                    u--;
                }
                else if (three is not null && AustralianState(three, three.Count - 1, known, out _, out _) is var at && at >= units[u - 2].Toks.Count)
                {
                    units[u - 2] = new Unit(three);
                    units.RemoveRange(u - 1, 2);
                    u -= 2;
                }
            }

            List<Tok> toks = units[u].Toks;
            if (toks.Count < 3 || !IsAustralianPostcode(toks[^1].Text))
            {
                return false;
            }

            Tok pc = toks[^1];
            int stateAt = AustralianState(toks, toks.Count - 1, known, out string? printed, out bool abbreviation);
            if (stateAt < 1)
            {
                return false;
            }

            List<Tok> stateToks = toks.GetRange(stateAt, toks.Count - 1 - stateAt);
            List<Tok> locality = toks.GetRange(0, stateAt);
            List<Tok>? street = null;

            // Several lines' worth of words on one line ("187 Wilson Esplanade Downer, ACT 2602"): the locality starts after the one street type that can end a street name.
            if (locality.Any(t => t.Text.Any(char.IsAsciiDigit)))
            {
                int house = locality.FindIndex(t => t.Text.Any(char.IsAsciiDigit));
                T2Country au = T2Rules.Countries["AU"];
                List<int> ends = [];
                for (int k = house + 2; k < locality.Count - 1 && locality.Count - 1 - k <= 3; k++)
                {
                    if (T2Rules.FindType(au, locality[k].Key) is not null && locality.Skip(k + 1).All(t => !t.Text.Any(char.IsAsciiDigit)))
                    {
                        ends.Add(k);
                    }
                }

                if (ends.Count != 1)
                {
                    return false;
                }

                street = locality.GetRange(0, ends[0] + 1);
                locality = locality.GetRange(ends[0] + 1, locality.Count - ends[0] - 1);
            }

            if (street is not null)
            {
                units[u] = new Unit(street);
                units.Add(new Unit(abbreviation ? [pc, .. stateToks, .. locality] : [pc, .. locality]));
                u++;
            }
            else
            {
                units[u] = new Unit(abbreviation ? [pc, .. stateToks, .. locality] : [pc, .. locality]);
            }

            if (!abbreviation)
            {
                _state = stateToks;
                _stateAbbreviation = printed!;
            }

            unitIndex = u;
            match = new PostcodeMatch(1, pc.Text, ["AU"], null);
            return true;
        }

        private static bool IsAustralianPostcode(string s) => s.Length == 4 && s.All(char.IsAsciiDigit);

        // The state before the postcode: the abbreviation in capitals (in any case once the country is known to be Australian), or the name in full in any case. Returns the
        // index of its first token, or -1.
        private static int AustralianState(List<Tok> toks, int end, string? known, out string? printed, out bool abbreviation)
        {
            printed = null;
            abbreviation = false;
            if (end >= 1 && T2Rules.States.TryGetValue(("AU", toks[end - 1].Key), out string? abbr) && abbr.Length <= 3 && toks[end - 1].Text.Length <= 3
                && (toks[end - 1].Text.All(char.IsAsciiLetterUpper) || known == "AU" && toks[end - 1].Text.All(char.IsAsciiLetter)))
            {
                printed = abbr;
                abbreviation = toks[end - 1].Text.All(char.IsAsciiLetterUpper);
                return end - 1;
            }

            for (int n = Math.Min(3, end - 1); n >= 1; n--)
            {
                if (T2Rules.States.TryGetValue(("AU", T2Rules.Key(Join(toks.GetRange(end - n, n)))), out string? name) && !(n == 1 && toks[end - 1].Text.Length <= 3))
                {
                    printed = name;
                    return end - n;
                }
            }

            return -1;
        }

        // ---- Romania -----------------------------------------------------------------------------------------------------------------------------------

        private static bool IsRomanianSectorWord(string key) => key is "SECTOR" or "SECTORUL" or "JUD" or "JUD." or "JUDETUL" or "JUDET";

        private static bool IsRomanianCountyWord(string key) => key is "JUD" or "JUDET" or "JUDETUL";

        private static bool IsRomanianPostcode(string s) => s.Length == 6 && s.All(char.IsAsciiDigit) || s.Length == 9 && s.StartsWith("RO-", StringComparison.OrdinalIgnoreCase) && s[3..].All(char.IsAsciiDigit);

        // Romanian pages label the postcode ("cod poștal 400001", "cod 400001", "Cod: 400001"). The label is not part of the address, so it is left out of every field and the
        // postcode is read as it is without the label; a label that is not followed by a postcode stays where it is.
        private static void DropRomanianPostcodeLabels(List<Unit> units)
        {
            foreach (Unit u in units)
            {
                for (int i = 0; i < u.Toks.Count - 1; i++)
                {
                    if (T2Rules.Key(u.Toks[i].Text.TrimEnd(':')) is not ("COD" or "CODUL"))
                    {
                        continue;
                    }

                    int next = i + 1;
                    if (next < u.Toks.Count - 1 && T2Rules.Key(u.Toks[next].Text.TrimEnd(':')) is "POSTAL" or "POSTALA")
                    {
                        next++;
                    }

                    if (next < u.Toks.Count && IsRomanianPostcode(u.Toks[next].Text))
                    {
                        u.Toks = [.. u.Toks.Take(i), .. u.Toks.Skip(next)];
                    }
                }
            }
        }

        private static bool RomanianTrailingLocality(List<Tok> toks, out int streetEnd)
        {
            streetEnd = toks.FindLastIndex(t => t.Text.Any(char.IsAsciiDigit)) + 1;
            int words = toks.Count - streetEnd;
            return streetEnd >= 2 && words is >= 1 and <= 3
                && toks.Skip(streetEnd).All(t => !t.Text.EndsWith('.') && !T2Rules.UnitWords.Contains(t.Key) && !IsRomanianSectorWord(t.Key) && char.IsLetter(t.Text[0]));
        }

        // UPU Romania sheet: "200716 CRAIOVA", the postcode of six digits left of the locality. Romanians also write the sector of Bucharest or the county after the locality
        // ("011371 Bucureşti, Sector 1", which on one line is a unit of its own after the postcode) and put the postcode last ("Pitești, 110040"; "București, Sector 3, 032391").
        // A sector or county unit after a unit with the postcode is joined to it; a postcode that ends the text is moved in front of the locality before it.
        private static bool FindRomanian(List<Unit> units, string? known, out int unitIndex, out int tokenIndex, out PostcodeMatch match)
        {
            unitIndex = tokenIndex = 0;
            match = null!;
            if (known is not null and not "RO" || units.Count == 0)
            {
                return false;
            }

            DropRomanianPostcodeLabels(units);

            // A postcode written before the locality but after the street ("Str. X nr. 5, cod poștal 400001, Cluj-Napoca") is the postcode line of the UPU sheet, in one piece.
            if (units.Count >= 2 && units[^2].Toks.Count == 1 && IsRomanianPostcode(units[^2].Toks[0].Text) && units[^1].Toks.Count <= 3
                && units[^1].Toks.All(t => !t.Text.Any(char.IsAsciiDigit)) && !IsRomanianSectorWord(units[^1].Toks[0].Key))
            {
                units[^2] = new Unit([.. units[^2].Toks, .. units[^1].Toks]);
                units.RemoveAt(units.Count - 1);
            }

            while (units.Count >= 2 && units[^1].Toks.Count > 0 && IsRomanianSectorWord(units[^1].Toks[0].Key) && units[^2].Toks.Any(t => IsRomanianPostcode(t.Text)))
            {
                units[^2] = new Unit([.. units[^2].Toks, .. units[^1].Toks]);
                units.RemoveAt(units.Count - 1);
            }

            List<Tok> last = units[^1].Toks;
            if (last.Count == 0 || !IsRomanianPostcode(last[^1].Text))
            {
                return false;
            }

            // The locality: the words before the postcode on its line, or the unit before a lone postcode, with the sector or county unit in front of it.
            List<Tok> locality = [];
            int keep = units.Count - 1;
            if (last.Count > 1)
            {
                locality = last.GetRange(0, last.Count - 1);
            }
            else if (units.Count >= 2)
            {
                keep--;
                locality = units[keep].Toks;
                while (keep > 0 && IsRomanianSectorWord(locality[0].Key) && units[keep - 1].Toks.Count > 0 && !units[keep - 1].Toks.Any(t => t.Text.Any(char.IsAsciiDigit)))
                {
                    keep--;
                    locality = [.. units[keep].Toks, .. locality];
                }

                // The locality written on the street's line without a comma ("Strada Frumoasă Nr. 51 București, Sector 1, 010986"): the words after the last number are the locality.
                // Only plain words qualify, so a building or apartment tail ("bl. A1", "ap. 4") is never taken for one.
                int target = IsRomanianSectorWord(locality[0].Key) ? keep - 1 : keep;
                if (target >= 0 && RomanianTrailingLocality(units[target].Toks, out int streetEnd))
                {
                    List<Tok> all = units[target].Toks;
                    List<Tok> tail = all.GetRange(streetEnd, all.Count - streetEnd);
                    units[target] = new Unit(all.GetRange(0, streetEnd));
                    if (target == keep)
                    {
                        units.Insert(keep + 1, new Unit(tail));
                        keep++;
                        locality = tail;
                    }
                    else
                    {
                        locality = [.. tail, .. locality];
                    }
                }
            }

            // Digits belong to a locality only as the number of a sector ("Sector 3"); anything else with digits is a street line ("nr. 41").
            if (locality.Count == 0 || IsRomanianSectorWord(locality[0].Key) && locality.Count <= 2 || locality.Where((t, i) => t.Text.Any(char.IsAsciiDigit) && !(i > 0 && IsRomanianSectorWord(locality[i - 1].Key))).Any())
            {
                return false;
            }

            Tok pc = last[^1];
            units.RemoveRange(keep, units.Count - keep);
            units.Add(new Unit([pc, .. locality]));
            unitIndex = units.Count - 1;
            match = new PostcodeMatch(1, pc.Text[^6..], ["RO"], null);
            return true;
        }

        // UPU Mexico sheet: "02860 MEXICO, CDMX": the state abbreviation follows the locality. Written on one line, the comma puts it in a unit of its own after the postcode line.
        private bool FindStateLine(List<Unit> units, string? known, out int unitIndex, out int tokenIndex, out PostcodeMatch match)
        {
            unitIndex = tokenIndex = 0;
            match = null!;
            if (units.Count < 2 || known is not null and not "MX" || units[^1].Toks.Count is 0 or > 3)
            {
                return false;
            }

            if (!StateTail("MX", units[^1].Toks, out int words, out string abbreviation) || words != units[^1].Toks.Count)
            {
                return false;
            }

            Unit stateUnit = units[^1];
            units.RemoveAt(units.Count - 1);
            if (FindLastLine(units, "MX", out unitIndex, out tokenIndex, out match) && match.Candidates.Contains("MX"))
            {
                _state = stateUnit.Toks;
                _stateAbbreviation = abbreviation;
                return true;
            }

            units.Add(stateUnit);
            return false;
        }

        // Mexican writers put the postcode after "C.P." or "CP" (código postal), often after the locality: "León, C.P. 37240", "C.P. 38500, Apaseo el Alto, Guanajuato".
        // UPU Mexico sheet: postcode + locality + state abbreviation. When the code does not stand in front of its locality, the units are rearranged to read code, locality, state,
        // as the other countries' last lines do; the spans still point at the words in the input. Only for text that is Mexican by hint or has no hint, and only with the "C.P." word.
        private static bool FindMexicanPostcodeWord(List<Unit> units, string? known, out int unitIndex, out int tokenIndex, out PostcodeMatch match)
        {
            unitIndex = tokenIndex = 0;
            match = null!;
            if (known is not null and not "MX")
            {
                return false;
            }

            int u = units.FindLastIndex(x => x.Toks.Count >= 2 && IsPostalCodeWord(x.Toks[^2]) && IsMexicanCode(x.Toks[^1].Text));
            if (u < 0 || units.Count - 1 - u > 2)
            {
                return false;
            }

            T2Country mx = T2Rules.Countries["MX"];
            List<Tok> unit = units[u].Toks;
            Tok[] pair = [unit[^2], unit[^1]];
            List<Tok> head = unit.GetRange(0, unit.Count - 2);
            List<Tok> locality;
            int from = u;
            Unit? headUnit = null;
            if (u < units.Count - 1)
            {
                // The locality, and after it the state, follow the code as units of their own; what stands in front of the code in its unit is a line of its own.
                List<Unit> after = units.GetRange(u + 1, units.Count - u - 1);
                if (after.Any(x => x.Toks.Count > 5 || x.Toks.Any(t => t.Text.Any(char.IsAsciiDigit))) || StartsWithSpatial(after[0].Toks))
                {
                    return false;
                }

                if (after.Count == 2 && !(StateTail("MX", after[1].Toks, out int w, out _) && w == after[1].Toks.Count))
                {
                    return false;
                }

                locality = [.. after.SelectMany(x => x.Toks)];
                headUnit = head.Count > 0 ? new Unit(head) : null;
            }
            else if (head.Count > 0)
            {
                // Written in lines, "Pénjamo, C.P. 36900" is one line with the locality in front of the code. A line with digits or a street type in front of it is not a locality.
                if (head.Count > 5 || head.Any(t => t.Text.Any(char.IsAsciiDigit)) || StartsWithSpatial(head) || T2Rules.FindType(mx, head[0].Key) is not null)
                {
                    return false;
                }

                locality = head;
            }
            else if (u > 0)
            {
                // On one line the locality, or the locality and the state, are the units in front of the code.
                int l = u - 1;
                if (units[l].Toks is [{ Key: "MEXICO" }])
                {
                    // "Ecatepec de Morelos, México, C.P. 55418": México is the state or the country; the rules do not choose.
                    return false;
                }

                bool stateOnly = StateTail("MX", units[l].Toks, out int sw, out _) && sw == units[l].Toks.Count;
                if (stateOnly && l > 0)
                {
                    l--;
                }

                List<Tok> place = units[l].Toks;
                if (place.Count > 5 || place.Any(t => t.Text.Any(char.IsAsciiDigit)) || StartsWithSpatial(place) || T2Rules.FindType(mx, place[0].Key) is not null && place.Count > 1)
                {
                    return false;
                }

                locality = [.. units.GetRange(l, u - l).SelectMany(x => x.Toks)];
                from = l;
            }
            else
            {
                return false;
            }

            List<Unit> rearranged = units.GetRange(0, from);
            if (headUnit is not null)
            {
                rearranged.Add(headUnit);
            }

            rearranged.Add(new Unit([.. pair, .. locality]));
            units.Clear();
            units.AddRange(rearranged);
            unitIndex = units.Count - 1;
            match = new PostcodeMatch(2, pair[1].Text, ["MX", "ES"], null);
            return true;
        }

        private static bool IsPostalCodeWord(Tok t) => t.Key == "CP";

        private static bool IsMexicanCode(string s) => s.Length == 5 && s.All(char.IsAsciiDigit);

        private static bool StateTail(string country, List<Tok> toks, out int words, out string abbreviation)
        {
            for (int n = Math.Min(3, toks.Count); n >= 1; n--)
            {
                string key = T2Rules.Key(Join(toks.GetRange(toks.Count - n, n)));
                if (T2Rules.States.TryGetValue((country, key), out string? printed) && !(n == 1 && toks.Count > 1 && toks[^2].Key is "DE" or "DEL" or "LA" or "LOS" or "LAS" or "EL" or "Y"))
                {
                    words = n;
                    abbreviation = printed;
                    return true;
                }
            }

            words = 0;
            abbreviation = string.Empty;
            return false;
        }

        // ---- Hungary -----------------------------------------------------------------------------------------------------------------------------------

        // Magyar Posta's order for letters inside Hungary and the UPU Hungary sheet: recipient, locality, street line, postcode alone on the last line. Hungarians also write
        // "1037 Budapest, Virág tér 3. IV. 61"; both are read and printed in the authority's order. The units are rearranged so that the last one reads postcode, locality.
        private bool FindHungarian(List<Unit> units, string? known, out int unitIndex, out int tokenIndex, out PostcodeMatch match)
        {
            unitIndex = tokenIndex = 0;
            match = null!;
            if (known is not null and not "HU" || units.Count < 2)
            {
                return false;
            }

            T2Country hu = T2Rules.Countries["HU"];
            int n = units.Count;
            if (n >= 3 && units[n - 1].Toks.Count == 1 && HungarianPostcode(units[n - 1].Toks[0].Text, out string digits, out string? prefix))
            {
                Line? street = ParseDeliveryLine(units[n - 2].Toks, "HU", hu, allowStreet: true);
                if (street is not null && HungarianLocality(units[n - 3].Toks) && (known == "HU" || HungarianEvidence(street)))
                {
                    List<Tok> pc = units[n - 1].Toks;
                    List<Tok> locality = units[n - 3].Toks;
                    Unit streetUnit = units[n - 2];
                    units.RemoveRange(n - 3, 3);
                    units.Add(streetUnit);
                    units.Add(new Unit([.. pc, .. locality]));
                    _huLocalityIndex = n - 3;
                    unitIndex = units.Count - 1;
                    match = new PostcodeMatch(1, digits, ["HU"], prefix);
                    return true;
                }
            }

            for (int p = 0; p < n - 1; p++)
            {
                List<Tok> t = units[p].Toks;
                if (t.Count < 2 || !HungarianPostcode(t[0].Text, out string d, out string? pre) || !HungarianLocality(t.GetRange(1, t.Count - 1)))
                {
                    continue;
                }

                List<Line?> rest = [.. units.GetRange(p + 1, n - p - 1).Select(u => ParseDeliveryLine(u.Toks, "HU", hu, allowStreet: true))];
                if (rest.All(l => l is not null) && (known == "HU" || rest.Any(l => HungarianEvidence(l!))))
                {
                    Unit moved = units[p];
                    units.RemoveAt(p);
                    units.Add(moved);
                    _huLocalityIndex = p;
                    unitIndex = units.Count - 1;
                    match = new PostcodeMatch(1, d, ["HU"], pre);
                    return true;
                }
            }

            return false;
        }

        // Postcode, locality and street on one line without a comma ("1068 Budapest Király utca 80."). The locality is the word after the postcode, with a district number if one
        // follows (Magyar Posta: "BUDAPEST XIII."); words before the postcode are a line of their own. What is left must read as a street line with a Hungarian street type or a box
        // (or the text is known to be Hungarian); a locality of several words is not guessed. This runs before the general last-line search, which would take every word after
        // the postcode for the locality.
        private bool FindHungarianOneLine(List<Unit> units, string? known, out int unitIndex, out int tokenIndex, out PostcodeMatch match)
        {
            unitIndex = tokenIndex = 0;
            match = null!;
            if (known is not null and not "HU")
            {
                return false;
            }

            T2Country hu = T2Rules.Countries["HU"];
            int n = units.Count;
            for (int p = 0; p < n; p++)
            {
                List<Tok> t = units[p].Toks;
                int q = t.FindIndex(x => HungarianPostcode(x.Text, out _, out _));
                if (q < 0 || t.Count - q < 4 || t.Take(q).Any(x => x.Text.Any(char.IsAsciiDigit)))
                {
                    continue;
                }

                HungarianPostcode(t[q].Text, out string digits, out string? prefix);
                int k = q + 2;
                if (IsRoman(t[k].Text) && t[k].Text.EndsWith('.') && k + 2 < t.Count)
                {
                    k++;
                    if (t[k].Key == "KER" && k + 2 < t.Count)
                    {
                        k++;
                    }
                }

                if (t[q + 1].Text.Any(char.IsAsciiDigit) || !HungarianLocality(t.GetRange(q + 1, k - q - 1)))
                {
                    continue;
                }

                List<Line?> after = [ParseDeliveryLine(t.GetRange(k, t.Count - k), "HU", hu, allowStreet: true), .. units.GetRange(p + 1, n - p - 1).Select(u => ParseDeliveryLine(u.Toks, "HU", hu, allowStreet: true))];
                if (!after.All(l => l is not null) || known != "HU" && !after.Any(l => HungarianWordEvidence(l!)))
                {
                    continue;
                }

                Unit localityUnit = new(t.GetRange(q, k - q));
                units[p] = new Unit(t.GetRange(k, t.Count - k));
                if (q > 0)
                {
                    units.Insert(p, new Unit(t.GetRange(0, q)));
                }

                units.Add(localityUnit);
                _huLocalityIndex = q > 0 ? p + 1 : p;
                unitIndex = units.Count - 1;
                match = new PostcodeMatch(1, digits, ["HU"], prefix);
                return true;
            }

            return false;
        }

        private static bool HungarianPostcode(string text, out string digits, out string? prefix)
        {
            digits = text;
            prefix = null;
            int dash = text.IndexOf('-', StringComparison.Ordinal);
            if (dash > 0)
            {
                string p = text[..dash].ToUpperInvariant();
                if (p is not ("H" or "HU"))
                {
                    return false;
                }

                prefix = "HU";
                digits = text[(dash + 1)..];
            }

            return digits.Length == 4 && digits.All(char.IsAsciiDigit) && digits[0] != '0';
        }

        // A locality line has no number; a district may follow ("BUDAPEST XIII.", Magyar Posta: the locality's district number may be written after the name).
        private static bool HungarianLocality(List<Tok> toks)
        {
            if (toks.Count is 0 or > 4 || StartsWithSpatial(toks))
            {
                return false;
            }

            for (int i = 0; i < toks.Count; i++)
            {
                string t = toks[i].Text;
                if (t.Any(char.IsAsciiDigit) && !(i == toks.Count - 1 && i > 0 && t.TrimEnd('.').All(char.IsAsciiDigit) && t.TrimEnd('.').Length <= 2))
                {
                    return false;
                }
            }

            return true;
        }

        // "Pf." is also how German-speaking countries abbreviate Postfach, so only a street type or the full word "postafiók" tells Hungarian text without a hint.
        private static bool HungarianWordEvidence(Line street) =>
            street.Pieces.Any(p => p.Role == StreetPartRole.StreetType || p.Role == StreetPartRole.BoxDesignator && T2Rules.Key(p.Text) == "POSTAFIOK");

        private static bool HungarianEvidence(Line street) =>
            street.Pieces.Any(p => p.Role is StreetPartRole.StreetType or StreetPartRole.BoxDesignator or StreetPartRole.GeneralDelivery);

        // ---- country evidence ----------------------------------------------------------------------------------------------------------------------------

        // Shared forms of five and four digits are read as one of this group's countries only on words that belong to it alone: a Mexican state abbreviation or colonia line,
        // Croatian and Slovenian street words. Spanish or Italian street words are never evidence, because American addresses use them too.
        private string? CentralCue(PostcodeMatch match, List<Unit> before, List<Tok> lastToks)
        {
            List<string> found = [];
            IEnumerable<Tok> all = lastToks.Concat(before.SelectMany(u => u.Toks));
            if (match.Candidates.Contains("MX"))
            {
                List<Tok> tail = lastToks.GetRange(match.Length, lastToks.Count - match.Length);
                bool state = _state is not null || tail.Count >= 2 && StateTail("MX", tail, out int w, out _) && w < tail.Count;
                bool colonia = before.Any(u => u.Toks.Count > 0 && u.Toks[0].Key is "COL" or "COLONIA" or "FRACC" or "FRACCIONAMIENTO");
                if (state || colonia)
                {
                    found.Add("MX");
                }
            }

            foreach (string code in new[] { "HR", "SI" })
            {
                if (match.Candidates.Contains(code) && all.Any(t => T2Rules.FindType(T2Rules.Countries[code], t.Key) is { } type && type.Full is "ULICA" or "TRG" or "CESTA" or "OBALA"))
                {
                    found.Add(code);
                }
            }

            // Magyar Posta: "utca" (street) and "postafiók" (box) are Hungarian words that no other country of the shared four-digit form writes.
            if (match.Candidates.Contains("HU") && all.Any(t => t.Key is "UTCA" or "POSTAFIOK"))
            {
                found.Add("HU");
            }

            return found.Count == 1 ? found[0] : null;
        }

        // ---- delivery lines ----------------------------------------------------------------------------------------------------------------------------

        // Box numbers of this group: "115", "70-123" (Apartado Postal), "B/52" (Czech Post, dodávací schrána), "2344T" (GPO Box).
        private static bool IsCentralBoxNumber(string s) => s.Length is >= 1 and <= 10 && s.Any(char.IsAsciiDigit) && s.All(c => char.IsLetterOrDigit(c) || c is '-' or '/');

        private static bool IsRoman(string s)
        {
            string t = s.TrimEnd('.');
            return t.Length is >= 1 and <= 4 && t.All(c => c is 'I' or 'V' or 'X');
        }

        // A house number as these countries write it: "15m", "320/62", "17/I", "6-8", "6–8" (UPU Romania sheet, en dash), Hungary's "3." with the period after the number.
        private static bool IsNum(string s, bool hungarian)
        {
            string t = hungarian ? s.TrimEnd('.') : s;
            t = t.Replace('\u2013', '-').Replace('\u2014', '-');
            return t.Length > 0 && IsHouseNumber(t);
        }

        // What may follow a house number and still belong to the street line: a flat or floor and door, a conscription number (Czech Post: "č. p. 8"), Romanian bloc, scara, etaj,
        // apartament (UPU Romania sheet), Mexico's "Piso 10". Short words, numbers, Roman numerals, a dash or slash and the unit words of the data table.
        private static bool TailOkC(List<Tok> toks, int from)
        {
            if (toks.Count - from > 8)
            {
                return false;
            }

            for (int i = from; i < toks.Count; i++)
            {
                string t = toks[i].Text;
                bool ok = t.Any(char.IsAsciiDigit) || t.Count(char.IsLetter) <= 3 && t.Length <= 5 || T2Rules.UnitWords.Contains(toks[i].Key) || IsRoman(t) || t is "\u2013" or "\u2014" or "-" or "/";
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        private string Separator(int previousEnd, int nextStart) =>
            previousEnd < 0 ? " " : nextStart <= previousEnd ? string.Empty : text.AsSpan(previousEnd, nextStart - previousEnd).Contains(',') ? ", " : " ";

        private string Slice(List<Tok> toks)
        {
            SourceSpan span = Span(toks);
            return string.Join(' ', text.Substring(span.Start, span.Length).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        private Line? ParseCentral(List<Tok> toks, T2Country country)
        {
            if (country.Profile == "au")
            {
                return ParseAustralian(toks, country);
            }

            int lead = 0;
            for (int words = Math.Min(2, toks.Count - 1); words >= 1; words--)
            {
                if (T2Rules.FindType(country, string.Join(' ', toks.Take(words).Select(t => t.Key))) is not null)
                {
                    lead = words;
                    break;
                }
            }

            // A line may be nothing but a type and a number ("Rynek 12"): the type is then the name.
            return (lead > 0 ? ParseCentral(toks, country, lead) : null) ?? ParseCentral(toks, country, 0);
        }

        private Line? ParseCentral(List<Tok> toks, T2Country country, int typeWords)
        {
            bool hu = country.Profile == "hu";

            if (country.Profile == "mx" && IsMexicanSettlementLine(toks))
            {
                return null;
            }

            // A box word inside the line ("Závodie P.O.BOX 1970") means this is not a plain street line; the rules leave it to the model.
            if (country.Profile is "cz" or "sk" or "si" && toks.Skip(1).Any(t => T2Rules.FindDelivery(country.Code, t.Key) is { Role: "box" }))
            {
                return null;
            }

            int j = -1;

            // Mexican streets without a number end in "S/N" (sin número); the word stands where the number would.
            if (country.Profile == "mx" && toks.Count - 1 > typeWords && toks[^1].Key == "S/N")
            {
                j = toks.Count - 1;
            }

            for (int k = typeWords + 1; j < 0 && k < toks.Count; k++)
            {
                // "#456" and "No. 456": Mexican and Romanian writers put a marker before the number.
                string numberText = toks[k].Text.StartsWith('#') ? toks[k].Text[1..] : toks[k].Text;
                if (IsNum(numberText, hu) && TailOkC(toks, k + 1))
                {
                    j = k;
                    break;
                }
            }

            if (j < 0)
            {
                return null;
            }

            Tok house = toks[j];
            Tok? marker = null;
            int nameEnd = j;
            if (house.Text.StartsWith('#'))
            {
                marker = new Tok("#", house.Start);
                house = new Tok(house.Text[1..], house.Start + 1);
            }
            else if (j - 1 > typeWords && toks[j - 1].Key is "NR" or "NO" or "NUM" or "NUMAR" or "NUMERO")
            {
                marker = toks[j - 1];
                nameEnd = j - 1;
            }

            List<Tok> nameToks = toks.GetRange(typeWords, nameEnd - typeWords);
            if (nameToks.Count == 0)
            {
                return null;
            }

            // A date of birth, an ordinal in the middle of an organization line ("28. 2. 1956") is not a street name; only the first word of a name may be a number with a period (Czech "28. října").
            if (!hu && nameToks.Skip(1).Any(t => t.Text.Length > 1 && t.Text[^1] == '.' && t.Text[..^1].All(char.IsAsciiDigit)))
            {
                return null;
            }

            // The Czech and Slovak conscription number: "č. p. 8" stands where a street name would.
            bool conscription = nameToks.Count == 2 && nameToks[0].Key == "C" && nameToks[1].Key is "P" or "EV";

            // The type comes before the name in Poland, Romania and Mexico and after it in Hungary, Slovenia and Croatia and in the names of Czech and Slovak squares.
            Tok? trailing = null;
            if (typeWords == 0 && nameToks.Count >= 2 && !conscription && T2Rules.FindType(country, nameToks[^1].Key) is not null)
            {
                trailing = nameToks[^1];
                nameToks = nameToks.GetRange(0, nameToks.Count - 1);
            }

            List<Piece> pieces = [];
            int previousEnd = -1;
            void Add(StreetPartRole role, string written, string standard, string display, SourceSpan span, string rule)
            {
                pieces.Add(new Piece(role, written, standard, display, span, rule, Separator(previousEnd, span.Start)));
                previousEnd = span.End;
            }

            if (typeWords > 0)
            {
                List<Tok> typeToks = toks.GetRange(0, typeWords);
                string typeWritten = Join(typeToks);
                Add(StreetPartRole.StreetType, typeWritten, typeWritten, T2Text.Title(typeWritten, capitalsKept: true), Span(typeToks), "t2-street-type");
            }

            string nameWritten = Join(nameToks);
            if (conscription)
            {
                Add(StreetPartRole.Other, nameWritten, nameWritten, nameWritten, Span(nameToks), "czech-post-conscription-number");
            }
            else
            {
                Add(StreetPartRole.StreetName, nameWritten, nameWritten, DisplayName(nameWritten, afterType: typeWords > 0), Span(nameToks), "t2-street-name");
            }

            if (trailing is { } tt)
            {
                // Hungarian street types are written in lower case inside a name (Virág tér); the other languages keep the writer's case.
                Add(StreetPartRole.StreetType, tt.Text, tt.Text, hu ? tt.Text.ToLowerInvariant() : T2Text.Title(tt.Text, capitalsKept: true, firstIsInitial: false), tt.Span, "t2-street-type");
            }

            if (marker is { } m)
            {
                Add(StreetPartRole.Other, m.Text, m.Text, m.Text, m.Span, "t2-number-marker");
            }

            int tailFrom = j + 1;
            string houseStandard = house.Text;
            if (country.Profile == "si" && tailFrom < toks.Count && toks[tailFrom].Text.Length == 1 && char.IsAsciiLetter(toks[tailFrom].Text[0]) && char.IsAsciiDigit(house.Text[^1]))
            {
                // Slovenian house numbers carry a letter that may stand after a space ("213 a"); the letter is part of the number.
                Tok letter = toks[tailFrom];
                house = new Tok(text.Substring(house.Start, letter.End - house.Start), house.Start);
                houseStandard = toks[j].Text + " " + letter.Text;
                tailFrom++;
            }

            int slash = country.Profile == "pl" ? house.Text.IndexOf('/', StringComparison.Ordinal) : -1;
            if (slash > 0 && slash == house.Text.LastIndexOf('/') && IsHouseNumber(house.Text[..slash]) && IsHouseNumber(house.Text[(slash + 1)..]))
            {
                // Poczta Polska, Zasady poprawnego adresowania 1.2: the number of the building and, after it, that of the flat or premises; "9/93" is both, written together.
                string building = house.Text[..slash];
                string flat = house.Text[(slash + 1)..];
                Add(StreetPartRole.HouseNumber, building, building, building, new SourceSpan(house.Start, building.Length), "t2-house-number");
                pieces.Add(new Piece(StreetPartRole.UnitNumber, flat, flat, flat, new SourceSpan(house.Start + slash + 1, flat.Length), "poczta-polska-flat", "/"));
                previousEnd = house.End;
            }
            else
            {
                Add(StreetPartRole.HouseNumber, house.Text, houseStandard, houseStandard, house.Span, "t2-house-number");
            }

            if (tailFrom < toks.Count)
            {
                List<Tok> tail = toks.GetRange(tailFrom, toks.Count - tailFrom);

                // Poland: "m. 22" (mieszkanie), "lok. 5" (lokal) - the word is the designator and what follows it the number.
                List<Tok> flats = [];
                int at = 0;
                while (at + 1 < tail.Count && T2Rules.IsUnitDesignator(country.Code, tail[at].Key) && IsUnitNumber(tail[at + 1].Text))
                {
                    flats.Add(tail[at]);
                    flats.Add(tail[at + 1]);
                    at += 2;
                }

                if (at == tail.Count && at > 0)
                {
                    for (int f = 0; f < flats.Count; f += 2)
                    {
                        Add(StreetPartRole.UnitDesignator, flats[f].Text, flats[f].Text, flats[f].Text, flats[f].Span, "poczta-polska-flat");
                        Add(StreetPartRole.UnitNumber, flats[f + 1].Text, flats[f + 1].Text, flats[f + 1].Text, flats[f + 1].Span, "poczta-polska-flat");
                    }
                }
                else
                {
                    string tailText = Slice(tail);
                    bool numberOnly = tail.Count == 1 && tail[0].Text.Any(char.IsAsciiDigit);
                    Add(numberOnly ? StreetPartRole.UnitNumber : StreetPartRole.UnitDesignator, tailText, tailText, tailText, Span(tail), "t2-floor-and-door");
                }
            }

            return new Line(StreetLineKind.Street, pieces);
        }

        // ---- Australia ---------------------------------------------------------------------------------------------------------------------------------

        private static readonly HashSet<string> AuSubUnits = new(StringComparer.Ordinal)
        {
            "UNIT", "U", "FLAT", "F", "APT", "APARTMENT", "SHOP", "SUITE", "SE", "ROOM", "RM", "OFFICE", "OFF", "SITE", "SHED", "STALL", "SL", "VILLA", "VLLA", "WAREHOUSE", "WE", "FACTORY", "FY",
        };

        private static readonly HashSet<string> AuLevels = new(StringComparer.Ordinal) { "LEVEL", "L", "FLOOR", "FL" };

        private static readonly HashSet<string> AuDirectionals = new(StringComparer.Ordinal) { "N", "S", "E", "W", "NE", "NW", "SE", "SW", "NORTH", "SOUTH", "EAST", "WEST" };

        private static bool IsUnitSlashNumber(string s)
        {
            int slash = s.IndexOf('/', StringComparison.Ordinal);
            return slash > 0 && slash == s.LastIndexOf('/') && IsUnitId(s[..slash]) && IsHouseNumber(s[(slash + 1)..]);
        }

        private static bool IsUnitId(string s) => s.Length is >= 1 and <= 5 && (s.Any(char.IsAsciiDigit) || s.Length == 1 && char.IsLetter(s[0])) && s.All(char.IsLetterOrDigit);

        // Australia Post (Address Presentation Standards p. 14-20): sub-unit, then floor or level, then the house number and the street name and type, all on one line;
        // a slash only separates a unit number from the street number ("4/12 Smith St"). Punctuation is avoided in the address proper and the street and locality lines
        // are capitals (UPU Australia sheet), so Postal drops periods and commas and prints capitals; Display keeps the same tokens in title case.
        private static string AuText(string s) => s.Replace(".", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal);

        private static Line? ParseAustralian(List<Tok> toks, T2Country country)
        {
            int i = 0;
            List<Piece> pieces = [];
            void Add(StreetPartRole role, Tok t, string rule)
            {
                string standard = AuText(t.Text).ToUpperInvariant();

                // A sub-unit or level abbreviation of up to three capitals (FL, APT, SE) is kept, as Australia Post prints them ("FL 7 17 Jones St").
                bool abbreviation = role == StreetPartRole.UnitDesignator && standard.Length <= 3 && standard == AuText(t.Text);
                pieces.Add(new Piece(role, t.Text, standard, abbreviation ? standard : T2Text.TitleWord(AuText(t.Text), first: pieces.Count == 0), t.Span, rule));
            }

            // "Unit 3/12 Smith St", "Shop 4/100 George St": the designator, then the unit number and the street number in one word (Address Presentation Standards p. 15).
            if (i + 2 < toks.Count && AuSubUnits.Contains(toks[i].Key) && IsUnitSlashNumber(toks[i + 1].Text))
            {
                Add(StreetPartRole.UnitDesignator, toks[i], "australia-post-sub-unit");
                i++;
            }
            else if (i + 1 < toks.Count && AuSubUnits.Contains(toks[i].Key) && IsUnitId(toks[i + 1].Text))
            {
                Add(StreetPartRole.UnitDesignator, toks[i], "australia-post-sub-unit");
                Add(StreetPartRole.UnitNumber, toks[i + 1], "australia-post-sub-unit");
                i += 2;
            }

            if (i + 1 < toks.Count && AuLevels.Contains(toks[i].Key) && IsUnitId(toks[i + 1].Text))
            {
                Add(StreetPartRole.UnitDesignator, toks[i], "australia-post-level");
                Add(StreetPartRole.UnitNumber, toks[i + 1], "australia-post-level");
                i += 2;
            }

            if (i + 1 < toks.Count && toks[i].Key == "LOT" && IsHouseNumber(toks[i + 1].Text))
            {
                Add(StreetPartRole.Other, toks[i], "australia-post-lot");
                i++;
            }

            if (i >= toks.Count - 1 || !IsHouseNumber(toks[i].Text))
            {
                return null;
            }

            // "4/12 Smith St": the unit number, a slash and the street number (Australia Post, Address Presentation Standards p. 15); two parts that print as written.
            int slash = toks[i].Text.IndexOf('/', StringComparison.Ordinal);
            if (slash > 0 && slash == toks[i].Text.LastIndexOf('/') && IsUnitId(toks[i].Text[..slash]) && IsHouseNumber(toks[i].Text[(slash + 1)..]))
            {
                Tok unit = new(toks[i].Text[..slash], toks[i].Start);
                Tok number = new(toks[i].Text[(slash + 1)..], toks[i].Start + slash + 1);
                Add(StreetPartRole.UnitNumber, unit, "australia-post-unit-slash");
                Add(StreetPartRole.HouseNumber, number, "australia-post-house-number");
                pieces[^1] = pieces[^1] with { Before = "/" };
            }
            else
            {
                Add(StreetPartRole.HouseNumber, toks[i], "australia-post-house-number");
            }

            i++;
            int typeAt = -1;
            for (int k = toks.Count - 1; k > i; k--)
            {
                if (T2Rules.FindType(country, toks[k].Key) is not null && toks.Count - 1 - k <= 1 && (k == toks.Count - 1 || AuDirectionals.Contains(toks[k + 1].Key)))
                {
                    typeAt = k;
                    break;
                }
            }

            int nameEnd = typeAt < 0 ? toks.Count : typeAt;
            List<Tok> nameToks = toks.GetRange(i, nameEnd - i);
            string written = Join(nameToks);
            string standardName = AuText(written).ToUpperInvariant();
            pieces.Add(new Piece(StreetPartRole.StreetName, written, standardName, T2Text.Title(AuText(written), capitalsKept: true, firstIsInitial: false), Span(nameToks), "australia-post-street-name"));
            if (typeAt >= 0)
            {
                Add(StreetPartRole.StreetType, toks[typeAt], "australia-post-street-type");
                if (typeAt + 1 < toks.Count)
                {
                    Add(StreetPartRole.PostDirectional, toks[typeAt + 1], "australia-post-street-suffix");
                }
            }

            return new Line(StreetLineKind.Street, pieces);
        }

        // ---- single-line text -----------------------------------------------------------------------------------------------------------------------

        // Written on one line, a comma puts the flat, the floor and door or a Romanian "nr." in a unit of its own: those units belong to the street line before them.
        private static void MergeCentral(List<Unit> before, T2Country country)
        {
            bool hu = country.Profile == "hu";
            for (int i = 1; i < before.Count; i++)
            {
                List<Tok> t = before[i].Toks;
                List<Tok> prev = before[i - 1].Toks;
                if (t.Count == 0 || country.Profile != "au" && StartsWithSpatial(prev))
                {
                    continue;
                }

                bool merge;
                if (country.Profile == "au")
                {
                    // "Unit 4, 12 Smith St": the sub-unit and level words stand alone before the street number.
                    merge = prev.Count is 2 or 4 && (AuSubUnits.Contains(prev[0].Key) || AuLevels.Contains(prev[0].Key)) && IsUnitId(prev[1].Text)
                        && (IsHouseNumber(t[0].Text) || t.Count >= 2 && (AuSubUnits.Contains(t[0].Key) || AuLevels.Contains(t[0].Key)) && IsUnitId(t[1].Text));
                }
                else
                {
                    bool prevHasNumber = prev.Any(x => IsNum(x.Text, hu));
                    bool marker = t.Count >= 2 && t[0].Key is "NR" or "NO" or "NUM" && !prevHasNumber;
                    bool tail = prevHasNumber && TailOkC(t, 0) && (t[0].Text.Any(char.IsAsciiDigit) || T2Rules.UnitWords.Contains(t[0].Key) || IsRoman(t[0].Text) || t.Count >= 2 && T2Rules.IsUnitDesignator(country.Code, t[0].Key));
                    merge = marker || tail;
                }

                if (merge)
                {
                    before[i - 1].Toks = [.. prev, .. t];
                    before.RemoveAt(i);
                    i--;
                }
            }
        }

        // UPU Mexico sheet: the line "Col. Tlatilco" is the name of the quarter (colonia); a housing estate (fraccionamiento), a barrio, a pueblo, an ejido or a residencial is the
        // same kind of line. The marker word makes the line certain; a quarter named without a marker word stays a line as written.
        private static bool IsMexicanSettlementLine(List<Tok> toks) =>
            toks.Count >= 2 && (toks[0].Key is "COL" or "COLONIA" or "FRACC" or "FRACCIONAMIENTO" or "BARRIO" or "PUEBLO" or "EJIDO" or "RESIDENCIAL" || toks[0].Key == "UNIDAD" && toks[1].Key == "HABITACIONAL");

        private static Piece CentralOtherPiece(List<Tok> toks, T2Country country)
        {
            Piece piece = OtherPiece(toks, country, afterStreet: true);
            return country.Profile == "mx" && IsMexicanSettlementLine(toks) ? piece with { Rule = "t2-dependent-locality" } : piece;
        }

        // Written on one line, the colonia follows the street number without a comma ("Calle X 12 Col. Y"); it is a line of its own.
        private static void SplitColoniaWord(List<Unit> before)
        {
            for (int i = 0; i < before.Count; i++)
            {
                List<Tok> toks = before[i].Toks;
                int at = toks.FindIndex(1, t => t.Key is "COL" or "COLONIA" or "FRACC" or "FRACCIONAMIENTO");
                if (at > 0 && at < toks.Count - 1 && toks.Take(at).Any(t => t.Text.Any(char.IsAsciiDigit) || t.Key == "S/N"))
                {
                    before[i] = new Unit(toks.GetRange(at, toks.Count - at));
                    before.Insert(i, new Unit(toks.GetRange(0, at)));
                    i++;
                }
            }
        }

        // On one line the legal form of a company name stands after a comma ("Servicios Díaz, S.C., Calle X 10"; "Mikuš a spol., v.o.s., Neded 931"): it belongs to the name before it.
        private static void MergeLegalSuffix(List<Unit> before, T2Country country)
        {
            for (int i = 1; i < before.Count; i++)
            {
                string key = string.Join(' ', before[i].Toks.Select(t => t.Key));
                bool legal = country.Profile switch
                {
                    "mx" => key is "SC" or "AC" or "SA" or "SA DE CV" or "S DE RL DE CV" or "SAPI DE CV" or "SAB DE CV" or "SRL DE CV" or "SAS",
                    "sk" or "cz" => key is "SRO" or "VOS" or "AS" or "KS" or "SPOL SRO",
                    _ => false,
                };
                if (legal && before[i - 1].Toks.Count > 0 && !before[i - 1].Toks.Any(x => x.Text.Any(char.IsAsciiDigit)))
                {
                    before[i - 1].Toks = [.. before[i - 1].Toks, .. before[i].Toks];
                    before.RemoveAt(i);
                    i--;
                }
            }
        }

        // Writes the last line of a country of this group; returns false for the other countries.
        internal static bool CentralLast(T2Country country, string postcode, string localityPostal, string localityDisplay, string adminInline, string adminInlineDisplay, out string postal, out string display)
        {
            switch (country.Profile)
            {
                case "au":
                    // UPU Australia sheet: locality, state, postcode, all in capitals, one space between them (Australia Post prefers two; its 2022 brochure prints one).
                    postal = localityPostal + " " + adminInline + " " + postcode;
                    display = localityDisplay + " " + adminInline + " " + postcode;
                    return true;
                case "ro":
                    // UPU Romania sheet: postcode and locality; a county the writer gave follows the locality after a comma.
                    postal = country.PrintedPrefix + postcode + " " + localityPostal + (adminInline.Length > 0 ? ", " + adminInline : string.Empty);
                    display = country.PrintedPrefix + postcode + " " + localityDisplay + (adminInlineDisplay.Length > 0 ? ", " + adminInlineDisplay : string.Empty);
                    return true;
                case "hu":
                    // UPU Hungary sheet: the postcode alone on the last line; the locality is printed above the street line.
                    postal = postcode;
                    display = postcode;
                    return true;
                case "mx":
                    // UPU Mexico sheet: "02860 MEXICO, CDMX".
                    postal = country.PrintedPrefix + postcode + " " + localityPostal + (adminInline.Length > 0 ? ", " + adminInline : string.Empty);
                    display = country.PrintedPrefix + postcode + " " + localityDisplay + (adminInline.Length > 0 ? ", " + adminInline : string.Empty);
                    return true;
                default:
                    postal = display = string.Empty;
                    return false;
            }
        }
    }
}
