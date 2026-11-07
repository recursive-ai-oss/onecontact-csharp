using RecursiveAI.OneContact.Addresses.Streets;

namespace RecursiveAI.OneContact.Addresses.Tier2;

// Orders of the last group that web pages use and the authorities' sheets do not print: the postcode in front of its locality and the street after them
// ("00-590 Warszawa, ul. Marszałkowska 10"; "28013 Madrid, Calle Mayor 5"), the locality in front of the street and the postcode last
// ("Warszawa, ul. Złota 59, 00-120"), the locality and then the postcode set apart behind the street ("Fő utca 12., Debrecen, 4024"), and the country name between the
// post town and a British or Irish postcode ("Belfast, UK BT1 1GH"). The units are rearranged so that the last one reads postcode, locality, as every other country's
// last line does; the spans still point at the words in the input. These readings need the country (a hint or a name): the same digits are a postcode in several countries.
internal static partial class Tier2Reader
{
    private sealed partial class Reader
    {
        // Royal Mail and the UPU Ireland sheet print the country name on a line of its own; written between the post town and the postcode on one line it is still the country
        // ("Belfast, UK BT1 1GH"), and so is a name on the unit above a postcode that stands alone ("Surrey, England, GU1 3AA"). The postcode forms of the Crown dependencies belong to
        // one island each (UPU Isle of Man, Jersey and Guernsey sheets), so the same holds there: "Douglas, Isle of Man (UK) IM1 1AA", "St Helier, Jersey, Channel Islands JE2 4AB".
        // "(UK)" beside the postcode names no country the postcode does not already name; it is taken as the country text and the postcode decides.
        private static void StripCountryBeforePostcode(List<Unit> units, ref (string Code, SourceSpan Span, string Written)? named)
        {
            if (named is not null || units.Count == 0)
            {
                return;
            }

            List<Tok> toks = units[^1].Toks;
            PostcodeMatch? m = TrailingAt(toks);
            if (m is null || m.Candidates.Count != 1 || m.Candidates[0] is not ("GB" or "IE" or "IM" or "JE" or "GG"))
            {
                return;
            }

            string own = m.Candidates[0];
            int pcStart = toks.Count - m.Length;
            Unit holder = units[^1];
            if (pcStart == 0)
            {
                // The postcode alone on its unit: the words before it are the end of the unit above.
                if (units.Count < 2)
                {
                    return;
                }

                holder = units[^2];
            }

            List<Tok> words = holder.Toks;
            int end = pcStart == 0 ? words.Count : pcStart;
            bool paren = end > 0 && words[end - 1].Text.Replace(".", string.Empty, StringComparison.Ordinal).Equals("(UK)", StringComparison.OrdinalIgnoreCase);
            int nameEnd = paren ? end - 1 : end;
            bool Alone(int n) => nameEnd > n || units.Count > 1 || pcStart == 0 && paren;
            for (int n = Math.Min(T2Rules.MaxCountryWords, nameEnd); n >= 1; n--)
            {
                string key = string.Join(' ', words.Skip(nameEnd - n).Take(n).Select(t => t.Key));
                if (!T2Rules.CountryNames.TryGetValue(key, out string? code) || !Alone(n))
                {
                    continue;
                }

                // A nation on a unit of its own above the postcode is the admin area ("Bournemouth / ENGLAND / BH1 1AA", Jersey Post): it is the country only when a county stands above it
                // ("Guildford, Surrey, England, GU1 3AA").
                if (pcStart == 0 && code == own && own == "GB" && !(units.Count >= 3 && IsBareCounty("GB", units[^3].Toks)))
                {
                    continue;
                }

                int from = nameEnd - n;
                string? resolved = code == own || code == "BI" || code == "CI" && own is "JE" or "GG" ? own : null;
                if (resolved is null || code == own && own is "IM" or "JE" or "GG" && !paren)
                {
                    // An island's own name beside its postcode is read by the island rules (the name above the postcode is the country, the line above it the place).
                    continue;
                }

                // "Jersey, Channel Islands": the island named just before the group is the country.
                if (code == "CI" && from > 0 && words[from - 1].Key == (own == "JE" ? "JERSEY" : "GUERNSEY"))
                {
                    from--;
                }

                int stop = paren ? end : nameEnd;
                List<Tok> name = words.GetRange(from, stop - from);
                named = (resolved, Span(name), Join(name));
                words.RemoveRange(from, stop - from);
                if (words.Count == 0 && pcStart == 0)
                {
                    units.Remove(holder);
                }

                return;
            }

            if (paren)
            {
                List<Tok> name = [words[end - 1]];
                named = (own, Span(name), Join(name));
                words.RemoveAt(end - 1);
                if (words.Count == 0 && pcStart == 0)
                {
                    units.Remove(holder);
                }
            }
        }

        private bool FindPostcodeFirst(List<Unit> units, string? known, out int unitIndex, out int tokenIndex, out PostcodeMatch match)
        {
            unitIndex = tokenIndex = 0;
            match = null!;
            if (known is null or "VA" or "SM" or "MC" || units.Count < 2 || !T2Rules.Countries.TryGetValue(known, out T2Country? country))
            {
                return false;
            }

            int n = units.Count;

            // The locality and then the postcode end a line ("Gdynia 81-001"): the line is rewritten to read postcode, locality. Delivery lines stand before it or after it.
            for (int p = 0; p < n; p++)
            {
                List<Tok> t = units[p].Toks;
                PostcodeMatch? trailing = t.Count >= 2 ? MatchAt(t, t.Count - 1, known) : null;
                if (trailing is { Length: 1 } && (trailing.Candidates.Contains(Family(known)) || trailing.Candidates.Contains(known)) && !(Inferring && trailing.Candidates.Count != 1)
                    && PlainPlace(t.GetRange(0, t.Count - 1)) && (units.Take(p).Any(u => IsDeliveryLine(u, country)) || units.Skip(p + 1).Any(u => IsDeliveryLine(u, country))))
                {
                    Unit rewritten = new([t[^1], .. t.GetRange(0, t.Count - 1)]);
                    units.RemoveAt(p);
                    units.Add(rewritten);
                    unitIndex = units.Count - 1;
                    match = trailing;
                    return true;
                }

                if (p == n - 1)
                {
                    break;
                }

                // The postcode and the locality open a line, delivery lines follow.
                PostcodeMatch? m = t.Count >= 2 ? MatchAt(t, 0, known) : null;
                // A trial read of country inference takes only a postcode form of one country: four or five digits at the start of a line are as often a house number.
                if (m is null || m.Length >= t.Count || !m.Candidates.Contains(Family(known)) && !m.Candidates.Contains(known) || Inferring && m.Candidates.Count != 1)
                {
                    continue;
                }

                List<Tok> locality = t.GetRange(m.Length, t.Count - m.Length);
                if (!PlainPlace(locality) || !units.Skip(p + 1).Any(u => IsDeliveryLine(u, country)))
                {
                    continue;
                }

                Unit moved = units[p];
                units.RemoveAt(p);
                units.Add(moved);
                unitIndex = units.Count - 1;
                match = m;
                return true;
            }

            // The postcode alone on the last line, after a locality that stands before or after the street line.
            List<Tok> last = units[n - 1].Toks;
            PostcodeMatch? alone = last.Count == 1 ? MatchAt(last, 0, known) : null;
            if (alone is null || alone.Length != 1 || !alone.Candidates.Contains(Family(known)) && !alone.Candidates.Contains(known) || Inferring && alone.Candidates.Count != 1)
            {
                return false;
            }

            if (n >= 3 && !_multiLine && IsDeliveryLine(units[n - 2], country) && PlainPlace(units[n - 3].Toks) && !IsDeliveryLine(units[n - 3], country))
            {
                // "Warszawa, ul. Złota 59, 00-120". Only on one line: written in lines, a name line above the street is a recipient, not a locality.
                // (The city-states print their name where the locality would be, so their postcode alone is never preceded by a locality.)
                Unit street = units[n - 2];
                List<Tok> joined = [.. last, .. units[n - 3].Toks];
                units.RemoveRange(n - 3, 3);
                units.Add(street);
                units.Add(new Unit(joined));
            }
            else if (n >= 3 && PlainPlace(units[n - 2].Toks) && !IsDeliveryLine(units[n - 2], country) && units.Take(n - 2).Any(u => IsDeliveryLine(u, country)))
            {
                // "Fő utca 12., Debrecen, 4024"
                List<Tok> joined = [.. last, .. units[n - 2].Toks];
                units.RemoveRange(n - 2, 2);
                units.Add(new Unit(joined));
            }
            else
            {
                return false;
            }

            unitIndex = units.Count - 1;
            match = alone;
            return true;
        }

        // A locality: a few words without digits that does not open a place inside a building or a box line.
        private static bool PlainPlace(List<Tok> toks) =>
            toks.Count is >= 1 and <= 4 && !StartsWithSpatial(toks)
            && toks.All(x => !x.Text.Any(char.IsAsciiDigit) && x.Text.Any(char.IsLetter) && x.Key is not ("CP" or "PLZ" or "CAP" or "ZIP" or "KOD" or "COD" or "PSC" or "CODIGO" or "POSTCODE"));

        // Spanish streets such as "Gran Vía" and "Mayor" are known by their name alone: the sign and the address carry no type word (UPU Spain sheet, street names are
        // printed as the municipality registers them). A name of capitalized words followed by the house number and what may follow it is read as a street line; a line
        // that starts with a unit word or a number is not one.
        private Line? ParseUntypedStreet(List<Tok> toks, T2Country country)
        {
            int k = toks.FindIndex(1, t => IsNumberToken(t.Text));
            if (k < 1 || k > 4 || !TailOk(toks, k + 1))
            {
                return null;
            }

            for (int i = 0; i < k; i++)
            {
                string w = toks[i].Text;
                bool connective = i > 0 && T2Rules.Key(w) is "DE" or "DEL" or "LA" or "LAS" or "LOS" or "EL" or "Y";
                if (!w.All(c => char.IsLetter(c) || c is '-' or '\'' or '.') || !(char.IsUpper(w[0]) || connective) || T2Rules.UnitWords.Contains(toks[i].Key))
                {
                    return null;
                }
            }

            List<Tok> nameToks = toks.GetRange(0, k);
            string nameWritten = Join(nameToks);
            List<Piece> pieces = [new Piece(StreetPartRole.StreetName, nameWritten, Fmt(country, nameWritten), DisplayName(nameWritten), Span(nameToks), "t2-street-name")];
            return AppendNumber(pieces, toks, k, country) ? new Line(StreetLineKind.Street, pieces) : null;
        }
    }
}
