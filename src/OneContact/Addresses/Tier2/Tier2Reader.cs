using System.Buffers;
using System.Text;
using RecursiveAI.OneContact.Addresses.Expansion;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Addresses.Streets;

namespace RecursiveAI.OneContact.Addresses.Tier2;

/// <summary>
/// Reads free-text addresses of France with its overseas departments, Monaco, Belgium, the Netherlands, Luxembourg, Germany, Austria, Switzerland and Liechtenstein,
/// and of the United Kingdom with Jersey, Guernsey and the Isle of Man, Ireland, Spain, Portugal, Italy, San Marino, the Vatican, Malta and Andorra,
/// by rules, from the postal authorities' free guides (La Poste, bpost, PostNL, Deutsche Post, Österreichische Post, Swiss Post and the UPU country sheets).
/// The rules read only what is certain: a postcode followed by the locality ends the address, the delivery line has its number where the country puts it, and a
/// box line starts with the authority's box word. Anything else is refused with <see cref="ReasonCode.AddressModelRequired"/>. Nothing is invented: no locality
/// from a postcode, no country when the content and the hint are silent.
/// </summary>
internal static partial class Tier2Reader
{
    internal static bool Handles(Region region) => T2Rules.Handles(region);

    internal static OperationStatus TryParse(string text, Region hint, out ParsedAddress? address, out ParseDetail detail)
    {
        address = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            detail = ParseDetail.Failure(ReasonCode.Empty, new SourceSpan(0, text.Length));
            return OperationStatus.InvalidData;
        }

        Reader r = new(text, hint);
        OperationStatus status = r.Run(out address, out detail);
        if (address is not null)
        {
            address.SourceText = text;
            address.Hint = hint;
        }

        return status;
    }

    private readonly record struct Tok(string Text, int Start)
    {
        internal int End => Start + Text.Length;

        internal string Key => T2Rules.Key(Text);

        internal SourceSpan Span => new(Start, Text.Length);
    }

    private sealed class Unit(List<Tok> toks)
    {
        internal List<Tok> Toks { get; set; } = toks;
    }

    // One piece of a delivery line with both renderings.
    // Before is what separates the piece from the one before it: a space, or a comma and a space where the authority keeps the comma (Correos before the floor and door).
    private sealed record Piece(StreetPartRole Role, string Text, string Standard, string Display, SourceSpan Span, string Rule, string Before = " ");

    private sealed record Line(StreetLineKind Kind, List<Piece> Pieces)
    {
        internal string Postal => Compose(p => p.Standard);

        internal string Display => Compose(p => p.Display);

        private string Compose(Func<Piece, string> text)
        {
            StringBuilder sb = new();
            foreach (Piece p in Pieces)
            {
                string t = text(p);
                if (t.Length == 0)
                {
                    continue;
                }

                if (sb.Length > 0)
                {
                    sb.Append(p.Before);
                }

                sb.Append(t);
            }

            return sb.ToString();
        }
    }

    private static readonly HashSet<string> RoadTypes = new(StringComparer.Ordinal)
    {
        "ALLEE", "AVENUE", "BOULEVARD", "CHEMIN", "CHAUSSEE", "COURS", "IMPASSE", "PASSAGE", "PLACE", "QUAI", "ROUTE", "RUE", "VOIE", "SENTIER", "PROMENADE", "ESPLANADE", "FAUBOURG", "MONTEE", "TRAVERSE", "GRAND RUE", "SQUARE",
    };

    private sealed partial class Reader(string text, Region hint)
    {
        private readonly List<ParseIssue> _issues = [];

        internal OperationStatus Run(out ParsedAddress? address, out ParseDetail detail)
        {
            address = null;
            detail = _failure;
            bool multiLine = text.Contains('\n', StringComparison.Ordinal);
            _multiLine = multiLine;
            List<Unit> units = Units(multiLine);
            if (units.Count == 0)
            {
                detail = ParseDetail.Failure(ReasonCode.Empty, new SourceSpan(0, text.Length));
                return OperationStatus.InvalidData;
            }

            // First try with a trailing country name taken off, then without: "98000 MONACO" ends in the name of a country that is also its locality.
            List<Unit> stripped = [.. units.Select(u => new Unit([.. u.Toks]))];
            (string Code, SourceSpan Span, string Written)? named = StripCountryName(stripped);
            if (named is not null && TryBuild(stripped, named, out address, out detail))
            {
                return OperationStatus.Done;
            }

            ParseDetail firstFailure = detail;
            if (TryBuild([.. units.Select(u => new Unit([.. u.Toks]))], null, out address, out detail))
            {
                return OperationStatus.Done;
            }

            if (named is not null && detail.PrimaryReason.Equals(ReasonCode.AddressNoLastLine))
            {
                detail = firstFailure;
            }

            return OperationStatus.InvalidData;
        }

        private bool _multiLine;

        // Group B: the county line of an Irish address and the province line of a Spanish one, taken off the units before the last line is read.
        private List<Tok>? _county;
        private List<Tok>? _province;

        private ParseDetail _failure = ParseDetail.Failure(ReasonCode.AddressNoLastLine, default);

        private bool TryBuild(List<Unit> units, (string Code, SourceSpan Span, string Written)? named, out ParsedAddress? address, out ParseDetail detail)
        {
            address = null;
            _issues.Clear();
            _failure = ParseDetail.Failure(ReasonCode.AddressNoLastLine, new SourceSpan(0, text.Length));
            detail = _failure;

            // The postcode and the locality after it end the address.
            // Two letters after four digits are a Dutch postcode unless the country is known to be another one ("9170 De Klinge" in Belgium); without any hint they must be capitals.
            string? known = named?.Code ?? (!hint.IsUnknown && T2Rules.Handles(hint) ? hint.Code : null);
            _county = null;
            _province = null;
            int unitIndex;
            int tokenIndex;
            PostcodeMatch match;
            if (!FindLastLine(units, known, out unitIndex, out tokenIndex, out match)
                && !FindPostcodeLast(units, known, out unitIndex, out tokenIndex, out match)
                && !FindProvinceLine(units, known, out unitIndex, out tokenIndex, out match))
            {
                detail = ParseDetail.Failure(ReasonCode.AddressNoLastLine, new SourceSpan(0, text.Length));
                return false;
            }

            // Words before the postcode on the same line are a line of their own: "12 rue X 75011 PARIS".
            Unit lastUnit = units[unitIndex];
            if (tokenIndex > 0)
            {
                units[unitIndex] = new Unit(lastUnit.Toks.GetRange(tokenIndex, lastUnit.Toks.Count - tokenIndex));
                units.Insert(unitIndex, new Unit(lastUnit.Toks.GetRange(0, tokenIndex)));
                unitIndex++;
                tokenIndex = 0;
            }

            List<Tok> lastToks = units[unitIndex].Toks;
            List<Tok> localityToks = lastToks.GetRange(match.Length, lastToks.Count - match.Length);
            List<Unit> before = units.GetRange(0, unitIndex);

            // The country: the explicit name or prefix, then the hint, then a postcode that fits one country only, then language cues.
            // The Channel Islands line names Jersey and Guernsey alike (UPU Jersey and Guernsey sheets): the postcode area says which.
            string? namedCode = named?.Code;
            if (namedCode == "CI")
            {
                namedCode = match.Candidates.FirstOrDefault(c => c is "JE" or "GG");
                if (namedCode is null)
                {
                    _failure = ParseDetail.Failure(ReasonCode.AddressCountryConflict, new SourceSpan(0, text.Length));
                    detail = _failure;
                    return false;
                }
            }

            if (!DecideCountry(match, namedCode, before, lastToks, out string code, out float confidence, out bool inferred))
            {
                detail = _failure;
                return false;
            }

            T2Country country = T2Rules.Countries[code];
            string ruleCode = code;

            // BE: a box literal that a comma set apart belongs to the street line before it ("Kortijkstraat 37, bus 1").
            for (int i = 1; i < before.Count; i++)
            {
                if (!country.IsNorth && before[i].Toks.Count == 2 && before[i].Toks[0].Key is "BUS" or "BTE" or "BOX" && IsBoxNumber(before[i].Toks[1].Text))
                {
                    before[i - 1].Toks.AddRange(before[i].Toks);
                    before.RemoveAt(i);
                    i--;
                }
            }

            // "Rheinstr., 53, 52306 Hagen" and "8, VIESSE": a house number set apart by commas belongs to the street name next to it.
            if (!_multiLine)
            {
                MergeLoneNumbers(before, country);
                if (country.Profile is "es" or "pt" or "it" or "sm" or "va" or "ad")
                {
                    MergeFloorAndDoor(before);
                }
                else if (country.IsNorth)
                {
                    MergeNorthFloorAndDoor(before);
                }
            }


            // The delivery lines start at the first box or street line that is not a place inside a building.
            int first = -1;
            List<Line?> parsed = [];
            for (int i = 0; i < before.Count; i++)
            {
                Line? line = ParseDeliveryLine(before[i].Toks, ruleCode, country, allowStreet: true);
                parsed.Add(line);
                if (first < 0 && line is not null && line.Kind != StreetLineKind.SecondaryOnly)
                {
                    first = i;
                }
            }

            // A hamlet line without a number ("LE VILLAGE") stands alone above the last line.
            if (first < 0 && country.NumberFirst && country.Profile.Length == 0 && before.Count >= 2 && !StartsWithSpatial(before[^1].Toks))
            {
                first = before.Count - 1;
                parsed[first] = new Line(StreetLineKind.Street, [NamePiece(StreetPartRole.StreetName, before[first].Toks, country, "t2-name-only")]);
            }

            // A bulk receiver has no delivery line: its name lines and the postcode with the locality are the whole address (UPU Luxembourg and Germany sheets).
            // Only name lines without digits qualify; anything else the rules cannot place goes to the model.
            // Britain and Ireland name properties: a house name is a whole delivery point, so more lines qualify there and there is nothing to warn about.
            bool named2 = country.Profile is "uk" or "ie" || country.IsNorth;
            bool bulk = first < 0 && before.Count >= 1 && before.Count <= (named2 ? 6 : 3) && before.All(u => !u.Toks.Any(t => t.Text.Any(char.IsAsciiDigit)));
            if (first < 0 && !bulk)
            {
                detail = ParseDetail.Failure(before.Count > 0 ? ReasonCode.AddressModelRequired : ReasonCode.AddressNoDeliveryLine, new SourceSpan(0, text.Length));
                return false;
            }

            if (bulk)
            {
                first = before.Count;
                if (!named2)
                {
                    _issues.Add(new ParseIssue(ReasonCode.AddressNoDeliveryLine, new SourceSpan(0, text.Length), IssueSeverity.Warning));
                }
            }

            List<ParsedAddressText> leading = [];
            List<(string Postal, string Display)> leadingLayout = [];
            for (int i = 0; i < first; i++)
            {
                List<Tok> toks = before[i].Toks;
                string written = Join(toks);
                string standard = country.Afnor ? T2Text.Afnor(written) : country.Profile is "pt" or "it" ? string.Join(' ', toks.Select(t => t.Text)).ToUpperInvariant() : string.Join(' ', toks.Select(t => t.Text));
                leading.Add(new ParsedAddressText(Slice(Span(toks)), Span(toks), standard));
                leadingLayout.Add((standard, T2Text.Title(written, capitalsKept: true)));
            }

            List<StreetLine> delivery = [];
            List<(string Postal, string Display)> deliveryLayout = [];
            AddressFacts facts = inferred ? AddressFacts.CountryInferred : AddressFacts.None;
            for (int i = first; i < before.Count; i++)
            {
                // A line after the first delivery line is another delivery line (a box, a locality name, a village); what the rules cannot place is kept as written.
                Line line = parsed[i] ?? new Line(StreetLineKind.Street, [OtherPiece(before[i].Toks, country, afterStreet: true)]);
                delivery.Add(new StreetLine(line.Kind, [.. line.Pieces.Where(p => p.Standard.Length > 0).Select(p => new StreetPart(p.Role, Slice(p.Span), p.Standard, p.Span, p.Rule))]));
                deliveryLayout.Add((line.Postal, line.Display));
                facts |= line.Kind == StreetLineKind.PostOfficeBox ? AddressFacts.PostOfficeBox : AddressFacts.None;
                facts |= line.Pieces.Any(p => p.Role == StreetPartRole.UnitDesignator) ? AddressFacts.HasUnit : AddressFacts.None;
            }

            // The last line: postcode, locality, and the admin area the authority prints with them: the Swiss canton abbreviation that tells places of one name apart,
            // the Italian province code ("00122 ROMA RM"), the Spanish province on its own line or in parentheses, the Irish county on its own line.
            (string Written, string Standard, SourceSpan Span, string Rule)? adminInfo = null;
            string adminInline = string.Empty;
            string adminLine = string.Empty;
            string adminWords = string.Empty;
            if (code is "CH" or "LI" && localityToks.Count >= 2 && localityToks[^1].Text.Length == 2 && localityToks[^1].Text.All(char.IsUpper) && T2Rules.Cantons.Contains(localityToks[^1].Text))
            {
                Tok canton = localityToks[^1];
                localityToks.RemoveAt(localityToks.Count - 1);
                adminInfo = (canton.Text, canton.Text, canton.Span, "upu-switzerland-canton");
                adminInline = canton.Text;
            }
            else if (code == "IT" && localityToks.Count >= 2 && ItProvinceCode(localityToks[^1]) is { } province)
            {
                // UPU Italy sheet: "postcode + locality + province code".
                Tok last = localityToks[^1];
                localityToks.RemoveAt(localityToks.Count - 1);
                adminInfo = (last.Text, province, last.Span, "upu-italy-province");
                adminInline = province;
            }
            else if (code == "ES" && localityToks.Count >= 2 && ParenthesizedProvince(localityToks) is { } inner)
            {
                localityToks.RemoveRange(localityToks.Count - inner.Count, inner.Count);
                string name = Join(inner).Trim('(', ')');
                adminInfo = (Slice(Span(inner)), name.ToUpperInvariant(), Span(inner), "upu-spain-province");
                adminLine = name.ToUpperInvariant();
                adminWords = name;
            }
            else if (code == "ES" && _province is not null)
            {
                adminInfo = (Slice(Span(_province)), Join(_province).ToUpperInvariant(), Span(_province), "upu-spain-province");
                adminLine = Join(_province).ToUpperInvariant();
                adminWords = Join(_province);
            }
            else if (code == "IE" && _county is not null)
            {
                adminInfo = (Slice(Span(_county)), Join(_county).ToUpperInvariant(), Span(_county), "upu-ireland-county");
                adminLine = Join(_county).ToUpperInvariant();
                adminWords = Join(_county);
            }

            string localityWritten = Join(localityToks);
            string localityPostal = country.Afnor ? T2Text.SaintAbbreviated(T2Text.Afnor(localityWritten)) : country.Profile == "lt" ? localityWritten : localityWritten.ToUpperInvariant();
            string localityDisplay = T2Text.Title(localityWritten, capitalsKept: true);
            string postcodeStd = match.Standard;
            string printed = country.PrintedPrefix + postcodeStd;
            string separator = new(' ', country.LocalitySpaces);
            string adminLineDisplay = adminLine.Length == 0 ? string.Empty : T2Text.Title(adminWords, capitalsKept: true);
            string lastPostal;
            string lastDisplay;
            if (country.PostcodeLast)
            {
                // UPU United Kingdom, Ireland and Malta sheets: the post town, the county where there is one, then the postcode on a line of its own.
                lastPostal = localityPostal + (adminLine.Length > 0 ? "\n" + adminLine : string.Empty) + "\n" + printed;
                lastDisplay = localityDisplay + (adminLine.Length > 0 ? "\n" + adminLineDisplay : string.Empty) + "\n" + printed;
                if (country.Profile == "lv")
                {
                    // UPU Latvia sheet: the locality, a comma, then LV- and the four digits on the same line.
                    lastPostal = localityPostal + ", " + printed;
                    lastDisplay = localityDisplay + ", " + printed;
                }
            }
            else
            {
                lastPostal = printed + separator + localityPostal + (adminInline.Length > 0 ? " " + adminInline : string.Empty) + (adminLine.Length > 0 ? "\n" + adminLine : string.Empty);
                lastDisplay = printed + " " + localityDisplay + (adminInline.Length > 0 ? " " + adminInline : string.Empty) + (adminLine.Length > 0 ? "\n" + adminLineDisplay : string.Empty);
            }

            // Fields in input order, every one with its offsets and the rule that decided it.
            List<ParsedAddressField> fields = [];
            for (int i = 0; i < leading.Count; i++)
            {
                fields.Add(new ParsedAddressField("leading-line", leading[i].Text, leading[i].Standard, leading[i].Span, 1f, DecisionSource.Rule, "line-structure"));
            }

            foreach (StreetLine line in delivery)
            {
                foreach (StreetPart p in line.Parts)
                {
                    fields.Add(new ParsedAddressField("street." + RoleName(p.Role), p.Text, p.Standard, p.Span, 1f, DecisionSource.Rule, p.Rule));
                }
            }

            ParsedAddressField? locality = new("locality", Slice(Span(localityToks)), localityPostal, Span(localityToks), 1f, DecisionSource.Rule, "last-line");
            fields.Add(locality.Value);
            ParsedAddressField? admin = null;
            if (adminInfo is { } info)
            {
                admin = new ParsedAddressField("admin-area", info.Written, info.Standard, info.Span, 1f, DecisionSource.Rule, info.Rule);
                fields.Add(admin.Value);
            }

            List<Tok> postcodeToks = lastToks.GetRange(0, match.Length);
            ParsedAddressField? postal = new("postal-code", Slice(Span(postcodeToks)), postcodeStd, Span(postcodeToks), 1f, DecisionSource.Rule, "postcode-" + country.Code.ToLowerInvariant());
            fields.Add(postal.Value);
            ParsedAddressField? countryName = null;
            if (named is { } n)
            {
                countryName = new ParsedAddressField("country", Slice(n.Span), country.CountryLine, n.Span, 1f, DecisionSource.Rule, "country-name");
                fields.Add(countryName.Value);
            }

            fields = [.. fields.OrderBy(f => f.Span.Start)];
            int start = fields.Min(f => f.Span.Start);
            int end = fields.Max(f => f.Span.End);
            ParsedAddress result = new(Region.Parse(code), confidence, inferred, leading, delivery, locality, admin, postal, countryName, fields, facts, new SourceSpan(start, end - start))
            {
                Tier2 = new T2Layout(country, leadingLayout, deliveryLayout, (lastPostal, lastDisplay)),
            };
            address = result;
            detail = new ParseDetail(_issues, fields.Select(f => new ParsedField(f.Name, f.Span, f.Confidence, f.Source)));
            return true;
        }

        // ---- units -------------------------------------------------------------------------------------------------------------------------------------

        private string Slice(SourceSpan span) => text.Substring(span.Start, span.Length);

        private List<Unit> Units(bool multiLine)
        {
            List<Unit> units = [];
            int lineStart = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                bool lineEnd = i == text.Length || text[i] == '\n' || text[i] == '\r';
                bool comma = !multiLine && i < text.Length && text[i] is ',' or ';';
                if (!lineEnd && !comma)
                {
                    continue;
                }

                List<Tok> toks = Tokenize(lineStart, i);

                // On one line a lone slash, bar, dash or bullet between words stands for a line break ("Rue du Vivier 7 / 1000 Bruxelles").
                List<Tok> piece = [];
                foreach (Tok t in toks)
                {
                    if (!multiLine && t.Text is "/" or "|" or "-" or "–" or "—" or "·" or "•")
                    {
                        if (piece.Count > 0)
                        {
                            units.Add(new Unit(piece));
                        }

                        piece = [];
                    }
                    else
                    {
                        piece.Add(t);
                    }
                }

                if (piece.Count > 0)
                {
                    units.Add(new Unit(piece));
                }

                lineStart = i + 1;
            }

            return units;
        }

        // Words are separated by white space; commas and semicolons around a word are not part of it. Periods stay: "Str." is how Deutsche Post writes it.
        private List<Tok> Tokenize(int start, int end)
        {
            List<Tok> toks = [];
            int i = start;
            while (i < end)
            {
                while (i < end && (char.IsWhiteSpace(text[i]) || text[i] is ',' or ';'))
                {
                    i++;
                }

                int s = i;
                while (i < end && !char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                int e = i;
                while (e > s && text[e - 1] is ',' or ';')
                {
                    e--;
                }

                if (e > s)
                {
                    toks.Add(new Tok(text.Substring(s, e - s), s));
                }
            }

            return toks;
        }

        private static (string Code, SourceSpan Span, string Written)? StripCountryName(List<Unit> units)
        {
            List<Tok> last = units[^1].Toks;
            for (int words = Math.Min(T2Rules.MaxCountryWords, last.Count); words >= 1; words--)
            {
                string key = string.Join(' ', last.Skip(last.Count - words).Select(t => t.Key));
                if (T2Rules.CountryNames.TryGetValue(key, out string? code) && (words < last.Count || units.Count > 1))
                {
                    Tok first = last[last.Count - words];
                    Tok lastTok = last[^1];
                    string written = Join(last.GetRange(last.Count - words, words));
                    last.RemoveRange(last.Count - words, words);
                    if (last.Count == 0)
                    {
                        units.RemoveAt(units.Count - 1);
                    }

                    return (code, new SourceSpan(first.Start, lastTok.End - first.Start), written);
                }
            }

            return null;
        }

        // ---- postcodes ---------------------------------------------------------------------------------------------------------------------------------

        private sealed record PostcodeMatch(int Length, string Standard, List<string> Candidates, string? PrefixCountry);

        // The last postcode that has a locality after it. The postcode forms are in the country sheets: five digits (France, Monaco, Germany), four (Belgium, Austria,
        // Switzerland, Liechtenstein, Luxembourg) and the Dutch four digits and two letters.
        private static bool FindLastLine(List<Unit> units, string? known, out int unitIndex, out int tokenIndex, out PostcodeMatch match)
        {
            int u = units.Count - 1;
            List<Tok> toks = units[u].Toks;
            for (int i = toks.Count - 2; i >= 0; i--)
            {
                PostcodeMatch? m = MatchAt(toks, i, known);
                if (m is not null && i + m.Length < toks.Count)
                {
                    unitIndex = u;
                    tokenIndex = i;
                    match = m;
                    return true;
                }
            }

            unitIndex = tokenIndex = 0;
            match = null!;
            return false;
        }

        // Britain, Ireland and Malta put the postcode after the locality: "London SW1A 2AA", or the post town on one line and the postcode alone on the next
        // (UPU United Kingdom, Ireland and Malta sheets). The units are rearranged so that the last one reads postcode first, as for the other countries; the spans
        // still point at the words in the input.
        private bool FindPostcodeLast(List<Unit> units, string? known, out int unitIndex, out int tokenIndex, out PostcodeMatch match)
        {
            unitIndex = tokenIndex = 0;
            match = null!;
            int u = units.Count - 1;
            List<Tok> toks = units[u].Toks;
            PostcodeMatch? m = TrailingAt(toks);
            if (m is null || known is not null && T2Rules.Countries.TryGetValue(known, out T2Country? k) && !k.PostcodeLast)
            {
                return false;
            }

            int pcStart = toks.Count - m.Length;
            List<Tok> pc = toks.GetRange(pcStart, m.Length);
            List<Tok> locality = toks.GetRange(0, pcStart);
            List<Tok>? county = null;
            List<int> taken = [];
            bool ireland = m.Candidates[0] == "IE";
            if (ireland)
            {
                int co = locality.FindIndex(IsCountyWord);
                if (co >= 0 && co < locality.Count - 1)
                {
                    county = locality.GetRange(co, locality.Count - co);
                    locality = locality.GetRange(0, co);
                }
            }

            int prev = u - 1;
            if (locality.Count == 0)
            {
                if (prev < 0)
                {
                    return false;
                }

                locality = units[prev].Toks;
                taken.Add(prev);
                prev--;
                if (ireland && county is null && IsCountyWord(locality[0]) && locality.Count > 1)
                {
                    county = locality;
                    if (prev < 0)
                    {
                        return false;
                    }

                    locality = units[prev].Toks;
                    taken.Add(prev);
                }
            }

            if (!LocalityFits(locality, ireland))
            {
                return false;
            }

            units[u] = new Unit([.. pc, .. locality]);
            foreach (int index in taken.OrderByDescending(x => x))
            {
                units.RemoveAt(index);
            }

            _county = county;
            unitIndex = u - taken.Count;
            match = m;
            return true;
        }

        private static bool IsCountyWord(Tok t) => t.Key is "CO" or "COUNTY";

        // A post town has no digits (Dublin postal districts have a number: "DUBLIN 24"), is not a street or box line and does not start a place inside a building.
        private static bool LocalityFits(List<Tok> locality, bool ireland)
        {
            if (locality.Count is 0 or > 6 || StartsWithSpatial(locality))
            {
                return false;
            }

            for (int i = 0; i < locality.Count; i++)
            {
                string t = locality[i].Text;
                if (!t.Any(char.IsAsciiDigit))
                {
                    continue;
                }

                bool district = ireland && i == locality.Count - 1 && i > 0 && t.Length <= 3 && t.TrimEnd('W', 'w').All(char.IsAsciiDigit);
                if (!district)
                {
                    return false;
                }
            }

            return true;
        }

        private static PostcodeMatch? TrailingAt(List<Tok> toks)
        {
            if (LatvianPostcodeAt(toks) is { } latvian)
            {
                return latvian;
            }

            if (toks.Count >= 2)
            {
                string a = toks[^2].Text.ToUpperInvariant();
                string b = toks[^1].Text.ToUpperInvariant();
                string? area = UkOutward(a);
                if (area is not null && IsUkInward(b) && T2Rules.UkAreas.TryGetValue(area, out string? ukCountry))
                {
                    return new PostcodeMatch(2, a + " " + b, [ukCountry], null);
                }

                if (a == "GIR" && b == "0AA")
                {
                    return new PostcodeMatch(2, "GIR 0AA", ["GB"], null);
                }

                if (IsEircodeRouting(a) && IsEircodeUnique(b))
                {
                    return new PostcodeMatch(2, a + " " + b, ["IE"], null);
                }

                if (a.Length == 3 && a.All(char.IsAsciiLetterUpper) && b.Length == 4 && b.All(char.IsAsciiDigit))
                {
                    return new PostcodeMatch(2, a + " " + b, ["MT"], null);
                }
            }

            {
                string g = toks[^1].Text.ToUpperInvariant();
                if (g.Length == 7 && IsEircodeRouting(g[..3]) && IsEircodeUnique(g[3..]))
                {
                    return new PostcodeMatch(1, g[..3] + " " + g[3..], ["IE"], null);
                }

                if (g.Length == 7 && g[..3].All(char.IsAsciiLetterUpper) && g[3..].All(char.IsAsciiDigit))
                {
                    return new PostcodeMatch(1, g[..3] + " " + g[3..], ["MT"], null);
                }
            }

            return null;
        }

        // UPU United Kingdom sheet: the outward code is one of AN, ANN, AAN, AANN, ANA, AANA; the inward code is a digit and two letters. Returns the postcode area.
        private static string? UkOutward(string s)
        {
            if (s.Length is < 2 or > 4)
            {
                return null;
            }

            int letters = 0;
            while (letters < s.Length && char.IsAsciiLetterUpper(s[letters]))
            {
                letters++;
            }

            if (letters is 0 or > 2 || letters >= s.Length || !char.IsAsciiDigit(s[letters]))
            {
                return null;
            }

            string rest = s[(letters + 1)..];
            bool ok = rest.Length == 0 || rest.Length == 1 && (char.IsAsciiDigit(rest[0]) || char.IsAsciiLetterUpper(rest[0]));
            return ok ? s[..letters] : null;
        }

        private static bool IsUkInward(string s) => s.Length == 3 && char.IsAsciiDigit(s[0]) && char.IsAsciiLetterUpper(s[1]) && char.IsAsciiLetterUpper(s[2]);

        // UPU Ireland sheet: seven characters, a space after the first three. A routing key is a letter and two characters (D6W is the one with a letter last).
        private static bool IsEircodeRouting(string s) => s.Length == 3 && char.IsAsciiLetterUpper(s[0]) && char.IsAsciiDigit(s[1]) && (char.IsAsciiDigit(s[2]) || s == "D6W");

        private static bool IsEircodeUnique(string s) => s.Length == 4 && s.All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c));

        // A Spanish province alone on the last line, after "45012 TOLEDO" (UPU Spain sheet: "province" is a line of its own). It is taken off only when what is left
        // reads as a Spanish last line whose postcode starts with the province's own two digits.
        private bool FindProvinceLine(List<Unit> units, string? known, out int unitIndex, out int tokenIndex, out PostcodeMatch match)
        {
            unitIndex = tokenIndex = 0;
            match = null!;
            if (units.Count < 2 || known is not null and not "ES" || units[^1].Toks.Count > 5)
            {
                return false;
            }

            if (!T2Rules.EsProvinces.TryGetValue(T2Rules.Key(Join(units[^1].Toks)), out string? prefix))
            {
                return false;
            }

            Unit province = units[^1];
            units.RemoveAt(units.Count - 1);
            if (FindLastLine(units, "ES", out unitIndex, out tokenIndex, out match) && match.Candidates.Contains("ES") && match.Standard.StartsWith(prefix, StringComparison.Ordinal))
            {
                _province = province.Toks;
                return true;
            }

            units.Add(province);
            return false;
        }

        private static string? ItProvinceCode(Tok t)
        {
            string code = t.Text.Trim('(', ')');
            return code.Length == 2 && code.All(char.IsAsciiLetterUpper) && T2Rules.ItProvinces.Contains(code) ? code : null;
        }

        // A province in parentheses ends the locality: "Madrid (Madrid)", "Santa Cruz (Santa Cruz de Tenerife)". Returns its tokens.
        private static List<Tok>? ParenthesizedProvince(List<Tok> toks)
        {
            if (!toks[^1].Text.EndsWith(')'))
            {
                return null;
            }

            for (int i = toks.Count - 1; i >= 1; i--)
            {
                if (toks[i].Text.StartsWith('('))
                {
                    List<Tok> inner = toks.GetRange(i, toks.Count - i);
                    string name = Join(inner).Trim('(', ')');
                    return T2Rules.EsProvinces.ContainsKey(T2Rules.Key(name)) ? inner : null;
                }
            }

            return null;
        }

        private static PostcodeMatch? MatchAt(List<Tok> toks, int i, string? known)
        {
            string t = toks[i].Text;

            // The Baltic posts print the country code with a dash that is often typeset as an en dash (UPU Latvia and Lithuania sheets: LT–04340).
            int typeset = t.IndexOfAny(['–', '—']);
            if (typeset is 1 or 2 && t.Length > typeset + 1 && t[..typeset].All(char.IsLetter))
            {
                t = t[..typeset] + "-" + t[(typeset + 1)..];
            }

            PostcodeMatch? north = NorthMatchAt(toks, i, t, known);
            if (north is not null)
            {
                return north;
            }

            // A country letter prefix: L-1234, D-26133, CH-3000, FL-9490, MC-98000.
            string? prefixCountry = null;
            string digits = t;
            int dash = t.IndexOf('-', StringComparison.Ordinal);
            if (dash is > 0 and <= 2 && t[..dash].All(char.IsLetter))
            {
                string prefix = t[..dash].ToUpperInvariant();
                foreach (T2Country c in T2Rules.Countries.Values)
                {
                    if (Array.IndexOf(c.Prefixes, prefix) >= 0 && c.Prefixes[0].Length > 0)
                    {
                        prefixCountry = c.Code;
                        break;
                    }
                }

                if (prefixCountry is null)
                {
                    return null;
                }

                digits = t[(dash + 1)..];
            }

            // UPU Portugal sheet: four digits, a dash, three digits. UPU Andorra sheet: AD and three digits ("AD500").
            if (prefixCountry is null && t.Length == 8 && t[..4].All(char.IsAsciiDigit) && t[4] is '-' or '\u2013' or '\u2212' && t[5..].All(char.IsAsciiDigit))
            {
                return new PostcodeMatch(1, t[..4] + "-" + t[5..], ["PT"], null);
            }

            if (prefixCountry is null && t.Length == 5 && t[..2].Equals("AD", StringComparison.OrdinalIgnoreCase) && t[2..].All(char.IsAsciiDigit))
            {
                return new PostcodeMatch(1, "AD" + t[2..], ["AD"], null);
            }

            // The Dutch form: four digits, then two letters in the same word or the next.
            if (digits.Length == 4 && digits.All(char.IsAsciiDigit) && digits[0] != '0')
            {
                if (i + 1 < toks.Count && toks[i + 1].Text.Length == 2 && toks[i + 1].Text.All(char.IsAsciiLetter) && prefixCountry is null or "NL"
                    && (known == "NL" || known is null && toks[i + 1].Text.All(char.IsAsciiLetterUpper)))
                {
                    return new PostcodeMatch(2, digits + " " + toks[i + 1].Text.ToUpperInvariant(), ["NL"], "NL");
                }
            }

            if (digits.Length == 6 && digits[..4].All(char.IsAsciiDigit) && digits[4..].All(char.IsAsciiLetter) && digits[0] != '0' && prefixCountry is null or "NL"
                && (known is null or "NL"))
            {
                return new PostcodeMatch(1, digits[..4] + " " + digits[4..].ToUpperInvariant(), ["NL"], "NL");
            }

            if (!digits.All(char.IsAsciiDigit))
            {
                return null;
            }

            if (digits.Length == 5)
            {
                // Finland, Estonia, Lithuania and Sweden share the five-digit form with France, Germany, Spain and Italy: a hint or a country name decides (00120 is Helsinki, and 97xxx and 98xxx
                // exist in Finland and Estonia too). Sweden writes a space after the third digit (UPU Sweden sheet).
                if (prefixCountry is null && known is "FI" or "EE" or "LT" or "SE")
                {
                    return new PostcodeMatch(1, known == "SE" ? digits[..3] + " " + digits[3..] : digits, [known], null);
                }

                // 00120 is the Vatican's (UPU Vatican sheet); San Marino uses 47890 to 47899 (UPU San Marino sheet, list of postcodes per locality).
                List<string> candidates = prefixCountry is not null
                    ? (prefixCountry is "FR" or "DE" or "MC" or "ES" or "IT" or "SM" or "VA" or "FI" or "EE" or "LT" ? [prefixCountry] : [])
                    : digits == "00120" ? ["VA"]
                    : digits.StartsWith("4789", StringComparison.Ordinal) ? ["SM"]
                    : digits.StartsWith("980", StringComparison.Ordinal) ? ["MC"]
                    : digits.StartsWith("97", StringComparison.Ordinal) ? ["FR"]
                    : ["FR", "DE", "ES", "IT"];
                return candidates.Count == 0 ? null : new PostcodeMatch(1, digits, candidates, prefixCountry);
            }

            if (digits.Length == 4)
            {
                List<string> candidates = prefixCountry is not null
                    ? (prefixCountry is "BE" or "AT" or "CH" or "LI" or "LU" or "DK" or "NO" ? [prefixCountry] : [])
                    : ["BE", "AT", "CH", "LI", "LU", "DK", "NO"];
                return candidates.Count == 0 ? null : new PostcodeMatch(1, digits, candidates, prefixCountry);
            }

            return null;
        }

        private static string Family(string code) => code is "GP" or "MQ" or "GF" or "RE" or "YT" ? "FR" : code;

        private bool DecideCountry(PostcodeMatch match, string? named, List<Unit> before, List<Tok> lastToks, out string code, out float confidence, out bool inferred)
        {
            code = string.Empty;
            confidence = 0.9f;
            inferred = true;
            string? explicitCode = named ?? match.PrefixCountry;
            string? hintCode = !hint.IsUnknown && T2Rules.Handles(hint) ? hint.Code : null;

            bool Fits(string c) => match.Candidates.Contains(c) || match.Candidates.Contains(Family(c));

            if (named is not null && !Fits(named))
            {
                _failure = ParseDetail.Failure(ReasonCode.AddressCountryConflict, new SourceSpan(0, text.Length));
                return false;
            }

            if (hintCode is not null && !Fits(hintCode))
            {
                // A hint that the postcode cannot belong to: the content decides when it names one country, otherwise the two disagree.
                if (explicitCode is null && match.Candidates.Count > 1)
                {
                    _failure = ParseDetail.Failure(ReasonCode.AddressCountryConflict, new SourceSpan(0, text.Length));
                    return false;
                }

                _issues.Add(new ParseIssue(ReasonCode.AddressCountryConflict, new SourceSpan(0, text.Length), IssueSeverity.Warning));
                hintCode = null;
            }

            if (named is not null)
            {
                code = named;
                confidence = hintCode is not null && Family(hintCode) != Family(named) ? 0.7f : 0.99f;
                inferred = false;
                return true;
            }

            if (hintCode is not null)
            {
                code = hintCode;
                confidence = 0.99f;
                inferred = false;
                return true;
            }

            string? only = match.Candidates.Count == 1 ? match.Candidates[0] : CountryFromCues(match, before, lastToks);
            if (only is null && _province is not null && match.Candidates.Contains("ES"))
            {
                // A Spanish province on its own line, whose number is the postcode's prefix, is Spanish evidence (FindProvinceLine checked the prefix).
                only = "ES";
            }
            if (only is null)
            {
                _failure = ParseDetail.Failure(ReasonCode.AddressCountryUndetermined, new SourceSpan(0, text.Length));
                return false;
            }

            code = only;
            confidence = match.PrefixCountry is not null ? 0.97f : match.Candidates.Count == 1 ? 0.93f : 0.8f;
            return true;
        }

        // Language cues that separate countries sharing a postcode form: CEDEX, BP and French street types point to France; Straße, Weg, Postfach to Germany.
        private static string? CountryFromCues(PostcodeMatch match, List<Unit> before, List<Tok> lastToks)
        {
            int french = 0;
            int german = 0;
            bool canton = lastToks.Count >= 3 && lastToks[^1].Text.Length == 2 && T2Rules.Cantons.Contains(lastToks[^1].Text) && lastToks[^1].Text.All(char.IsUpper);
            foreach (Tok t in lastToks.Concat(before.SelectMany(u => u.Toks)))
            {
                string k = t.Key;
                if (k is "CEDEX" or "BP" or "CS" or "TSA" or "RUE" or "AVENUE" or "BOULEVARD" or "CHEMIN" or "IMPASSE" or "QUAI" or "BOITE")
                {
                    french++;
                }
                else if (k is "POSTFACH" or "PACKSTATION" or "POSTLAGERND" || k.EndsWith("STRASSE", StringComparison.Ordinal) || k.EndsWith("WEG", StringComparison.Ordinal)
                    || k.EndsWith("GASSE", StringComparison.Ordinal) || k.EndsWith("PLATZ", StringComparison.Ordinal) || k.EndsWith("UFER", StringComparison.Ordinal))
                {
                    german++;
                }
            }

            if (match.Candidates.Contains("FR"))
            {
                // An Italian province code after the locality ("00122 ROMA RM") is Italian evidence; the United States and Spanish street words are not used,
                // because they would read American addresses without a state as Spanish or Italian.
                bool province = lastToks.Count >= 3 && ItProvinceCode(lastToks[^1]) is { } pc && !T2Rules.Countries.ContainsKey(pc) && !lastToks[^1].Text.StartsWith('(');
                if (province && french == 0 && german == 0)
                {
                    return "IT";
                }

                return french > 0 && german == 0 && !province ? "FR" : german > 0 && french == 0 && !province ? "DE" : null;
            }

            return canton ? "CH" : null;
        }

        // ---- delivery lines ----------------------------------------------------------------------------------------------------------------------------

        private static bool IsLoneNumber(Unit u) =>
            u.Toks.Count is >= 1 and <= 2 && IsHouseNumber(u.Toks[0].Text) && (u.Toks.Count == 1 || IsAddition(u.Toks[1].Text));

        private static void MergeLoneNumbers(List<Unit> before, T2Country country)
        {
            for (int i = 0; i < before.Count; i++)
            {
                if (!IsLoneNumber(before[i]) || before.Count < 2)
                {
                    continue;
                }

                bool prevHasNumber = i > 0 && before[i - 1].Toks.Any(t => t.Text.Any(char.IsAsciiDigit));
                if (country.NumberFirst && i + 1 < before.Count && !before[i + 1].Toks.Any(t => t.Text.Any(char.IsAsciiDigit)) && (!country.NumberLast || ParseNumberFirst([.. before[i].Toks, .. before[i + 1].Toks], country) is not null))
                {
                    before[i + 1].Toks = [.. before[i].Toks, .. before[i + 1].Toks];
                    before.RemoveAt(i);
                    i--;
                }
                else if (country.NumberLast && i > 0 && !prevHasNumber)
                {
                    before[i - 1].Toks = [.. before[i - 1].Toks, .. before[i].Toks];
                    before.RemoveAt(i);
                    i--;
                }
            }
        }

        private static bool StartsWithSpatial(List<Tok> toks) => toks.Count > 0 && T2Rules.SpatialWords.Contains(toks[0].Key);

        // A short addition after a house number: a letter, two letters, or a letter with digits ("A", "hs", "g9", "b-12"; PostNL writes the addition after the number).
        private static bool IsAddition(string s) =>
            s.Length is >= 1 and <= 5 && char.IsLetter(s[0]) && s.Skip(1).All(c => char.IsLetterOrDigit(c) || c == '-') && (s.Length <= 2 || s.Any(char.IsAsciiDigit) || s.All(char.IsLetter) && s.Length <= 3);

        private static bool IsBoxNumber(string s) => s.Length is >= 1 and <= 8 && s.Any(char.IsAsciiDigit) && s.All(char.IsLetterOrDigit);

        private static bool IsHouseNumber(string s)
        {
            // 25, 7C, 52a, 81-83, 62/3, 25/2/15, 5-1: digits with at most two letters, joined by a dash or a slash.
            if (s.Length == 0 || !char.IsAsciiDigit(s[0]))
            {
                return false;
            }

            int i = 0;
            while (i < s.Length)
            {
                int d = i;
                while (i < s.Length && char.IsAsciiDigit(s[i]))
                {
                    i++;
                }

                if (i == d)
                {
                    return false;
                }

                int l = i;
                while (i < s.Length && char.IsAsciiLetter(s[i]))
                {
                    i++;
                }

                if (i - l > 3)
                {
                    return false;
                }

                if (i < s.Length)
                {
                    if (s[i] is not ('-' or '/'))
                    {
                        return false;
                    }

                    i++;
                    if (i == s.Length)
                    {
                        return false;
                    }

                    // 16-A: a letter after the dash closes the number.
                    if (s.Length - i <= 2 && s[i..].All(char.IsAsciiLetter))
                    {
                        return true;
                    }
                }
            }

            return true;
        }

        private Line? ParseDeliveryLine(List<Tok> toks, string code, T2Country country, bool allowStreet)
        {
            if (toks.Count == 0 || StartsWithSpatial(toks))
            {
                return null;
            }

            Line? box = ParseBoxLine(toks, code, country);
            if (box is not null)
            {
                return box;
            }

            if (!allowStreet)
            {
                return null;
            }

            if (country.IsNorth)
            {
                return ParseNorth(toks);
            }

            if (country.NumberFirst)
            {
                Line? first = ParseNumberFirst(toks, country);
                if (first is not null)
                {
                    return first;
                }
            }

            if (!country.NumberLast)
            {
                return null;
            }

            return country.Profile.Length > 0 ? ParseNameThenNumber(toks, country) : ParseNumberLast(toks, country, code);
        }

        private static Line? ParseBoxLine(List<Tok> toks, string code, T2Country country)
        {
            for (int words = Math.Min(T2Rules.MaxDeliveryWords, toks.Count); words >= 1; words--)
            {
                string key = string.Join(' ', toks.Take(words).Select(t => t.Key));
                T2Delivery? d = T2Rules.FindDelivery(code, key) ?? (code is "GP" or "MQ" or "GF" or "RE" or "YT" ? T2Rules.FindDelivery("FR", key) : null);
                if (d is null)
                {
                    continue;
                }

                List<Piece> pieces = [];
                string printed = country.Afnor ? T2Text.Afnor(d.Printed) : Fmt(country, d.Printed);
                if (d.Role == "general")
                {
                    pieces.Add(new Piece(StreetPartRole.GeneralDelivery, Join(toks.Take(words)), printed, T2Text.Title(d.Printed, capitalsKept: true), Span(toks.Take(words)), "t2-poste-restante"));
                    if (toks.Count > words)
                    {
                        pieces.Add(OtherPiece(toks.GetRange(words, toks.Count - words), country, afterStreet: true));
                    }

                    return new Line(StreetLineKind.GeneralDelivery, pieces);
                }

                // The box number: digits, in groups for Deutsche Post (Postfach 3 41 41) and without spaces for bpost (Postbus 12000, not 12 000).
                int n = words;
                while (n < toks.Count && IsBoxNumber(toks[n].Text) && (n == words || toks[n].Text.All(char.IsAsciiDigit)))
                {
                    n++;
                }

                if (n == words && d.Role != "station")
                {
                    return null;
                }

                // A pick-up point named instead of numbered (bpack 24/7, bpost template 3): the words after it are its name.
                if (n == words)
                {
                    pieces.Add(new Piece(StreetPartRole.BoxDesignator, Join(toks.Take(words)), printed, T2Text.Title(d.Printed, capitalsKept: true), Span(toks.Take(words)), "t2-pickup-point"));
                    if (toks.Count > words)
                    {
                        pieces.Add(OtherPiece(toks.GetRange(words, toks.Count - words), country, afterStreet: true));
                    }

                    return new Line(StreetLineKind.PostOfficeBox, pieces);
                }

                List<Tok> number = toks.GetRange(words, n - words);
                string numberStandard = country.Code == "BE" ? string.Concat(number.Select(t => t.Text)) : Fmt(country, string.Join(' ', number.Select(t => t.Text)));
                pieces.Add(new Piece(StreetPartRole.BoxDesignator, Join(toks.Take(words)), printed, T2Text.Title(d.Printed, capitalsKept: true), Span(toks.Take(words)), "t2-box-designator"));
                pieces.Add(new Piece(StreetPartRole.BoxNumber, Join(number), numberStandard, numberStandard, Span(number), "t2-box-number"));
                if (n < toks.Count)
                {
                    pieces.Add(OtherPiece(toks.GetRange(n, toks.Count - n), country, afterStreet: true));
                }

                return new Line(StreetLineKind.PostOfficeBox, pieces);
            }

            return null;
        }

        // French style: number, optional BIS/TER, type, name ("25 RUE DE L EGLISE"; La Poste guide p. 19, line 4).
        private static Line? ParseNumberFirst(List<Tok> toks, T2Country country)
        {
            int i = 0;
            List<Piece> pieces = [];
            string first = toks[0].Text;
            bool numbered = first.Length > 0 && char.IsAsciiDigit(first[0]) && IsHouseNumber(first);
            if (numbered)
            {
                int end = 1;
                if (toks.Count > 2 && toks[1].Key is "BIS" or "TER" or "QUATER" or "QUINQUIES")
                {
                    end = 2;
                }

                List<Tok> number = toks.GetRange(0, end);
                string written = Join(number);
                string standard = country.Afnor ? T2Text.Afnor(written) : Fmt(country, string.Join(' ', number.Select(t => t.Text)));
                pieces.Add(new Piece(StreetPartRole.HouseNumber, written, standard, written, Span(number), "t2-house-number"));
                i = end;
            }

            T2Type? type = null;
            int typeWords = 0;
            for (int words = Math.Min(4, toks.Count - i); words >= 1; words--)
            {
                string key = string.Join(' ', toks.Skip(i).Take(words).Select(t => t.Key));
                type = T2Rules.FindType(country, key);
                if (type is not null)
                {
                    typeWords = words;
                    break;
                }
            }

            // Without a number only a road type starts a street line; a residence, a zone or a lieu-dit is a complement line (La Poste guide p. 19, line 3).
            // The road-* sets of the later groups hold road types only.
            if (!numbered && (type is null || !(RoadTypes.Contains(type.Full) || type.Set.StartsWith("road-", StringComparison.Ordinal))))
            {
                return null;
            }

            if (numbered && toks.Count - i == 0)
            {
                return null;
            }

            List<Tok> typeToks = type is null ? [] : toks.GetRange(i, typeWords);
            List<Tok> nameToks = toks.GetRange(i + typeWords, toks.Count - i - typeWords);

            // Andorra writes the number before the name at the French post office and after it at the Spanish one (UPU Andorra sheet): a number that ends the name belongs
            // to the name-then-number reading.
            if (!numbered && country.Profile.Length > 0 && country.NumberLast && nameToks.Count > 0 && IsNumberToken(nameToks[^1].Text))
            {
                return null;
            }
            if (type is not null)
            {
                string typeWritten = Join(typeToks);
                string full = country.Afnor ? type.Full : Fmt(country, string.Join(' ', typeToks.Select(t => t.Text)));
                pieces.Add(new Piece(StreetPartRole.StreetType, typeWritten, full, country.Afnor ? T2Text.Title(full, capitalsKept: true) : full, Span(typeToks), "t2-street-type"));
            }

            if (nameToks.Count > 0)
            {
                string nameWritten = Join(nameToks);
                string nameStandard = country.Afnor ? T2Text.Afnor(nameWritten) : Fmt(country, nameWritten);
                pieces.Add(new Piece(StreetPartRole.StreetName, nameWritten, nameStandard, DisplayName(nameWritten, afterType: type is not null), Span(nameToks), "t2-street-name"));
            }

            if (pieces.Count == 0 || (!numbered && nameToks.Count == 0))
            {
                return null;
            }

            // A type is written in full unless the line is longer than La Poste's limit of 38 characters, then in its abbreviation (SP 8855 volume 2, 3.1 and 3.3).
            if (country.Afnor && country.MaxLine > 0 && new Line(StreetLineKind.Street, pieces).Postal.Length > country.MaxLine && type is { Abbreviation.Length: > 0 })
            {
                Piece t = pieces.First(p => p.Role == StreetPartRole.StreetType);
                string abbreviation = T2Text.Afnor(type.Abbreviation);
                pieces[pieces.IndexOf(t)] = t with { Standard = abbreviation, Display = T2Text.Title(abbreviation, capitalsKept: false) };
            }

            return new Line(StreetLineKind.Street, pieces);
        }

        // German, Dutch and Belgian style: name, number, optional box ("Volklorenlaan 81 bus 15"; bpost template 3).
        private static Line? ParseNumberLast(List<Tok> toks, T2Country country, string code)
        {
            // A second part after "//" or "/" standing alone is the apartment (UPU Germany sheet: "Rhondorfer Str. 666 // Appartment 47").
            int slash = toks.FindIndex(t => t.Text is "//" or "/");
            List<Tok> core = slash > 0 ? toks.GetRange(0, slash) : toks;
            List<Tok> tail = slash > 0 ? toks.GetRange(slash + 1, toks.Count - slash - 1) : [];
            if (slash == 0 || slash == toks.Count - 1 || core.Count < 2)
            {
                return null;
            }

            // The box literal and its number close the line: bus, bte or box (bpost template 3); b, bt, # and the like are rewritten, never kept.
            int k = -1;
            string? literalWritten = null;
            int last = core.Count - 1;
            if (last >= 2 && IsBoxNumber(core[last].Text))
            {
                string lk = core[last - 1].Key;
                if (lk is "BUS" or "BTE" or "BOX" || (code == "BE" && (lk is "B" or "BT" or "NR" or "N" || core[last - 1].Text is "#" or "-" or "/")))
                {
                    k = last - 1;
                    literalWritten = core[k].Text;
                }
            }

            int h = k >= 0 ? k - 1 : core.Count - 1;
            if (h < 1)
            {
                return null;
            }

            // A letter or short word after the number belongs to it ("20 a", "5 hs").
            int houseStart = h;
            if (!IsHouseNumber(core[h].Text))
            {
                if (IsAddition(core[h].Text) && h >= 2 && IsHouseNumber(core[h - 1].Text))
                {
                    houseStart = h - 1;
                }
                else
                {
                    return null;
                }
            }

            List<Tok> nameToks = core.GetRange(0, houseStart);
            while (nameToks.Count > 0 && nameToks[^1].Key is "NR" or "N" or "NO" or "NUMMER")
            {
                nameToks.RemoveAt(nameToks.Count - 1);
            }

            if (nameToks.Count == 0 || nameToks.All(t => t.Text.All(char.IsAsciiDigit)))
            {
                return null;
            }

            List<Tok> houseToks = core.GetRange(houseStart, h - houseStart + 1);
            List<Piece> pieces = [];
            string nameWritten = Join(nameToks);
            pieces.Add(new Piece(StreetPartRole.StreetName, nameWritten, nameWritten, DisplayName(nameWritten), Span(nameToks), "t2-street-name"));
            string houseWritten = Join(houseToks);
            pieces.Add(new Piece(StreetPartRole.HouseNumber, houseWritten, houseWritten, houseWritten, Span(houseToks), "t2-house-number"));
            if (k >= 0)
            {
                string literal = literalWritten!.ToLowerInvariant() is "bus" or "bte" or "box" ? literalWritten : BoxLiteral(nameToks);
                pieces.Add(new Piece(StreetPartRole.UnitDesignator, literalWritten, literal, literal, core[k].Span, "bpost-box-literal"));
                pieces.Add(new Piece(StreetPartRole.UnitNumber, core[last].Text, core[last].Text, core[last].Text, core[last].Span, "bpost-box-number"));
            }

            if (tail.Count > 0)
            {
                pieces.Add(new Piece(StreetPartRole.UnitDesignator, "//", "//", "//", toks[slash].Span, "upu-germany-apartment"));
                pieces.Add(new Piece(StreetPartRole.UnitDesignator, tail[0].Text, tail[0].Text, T2Text.TitleWord(tail[0].Text, first: true), tail[0].Span, "upu-germany-apartment"));
                if (tail.Count > 1)
                {
                    List<Tok> rest = tail.GetRange(1, tail.Count - 1);
                    pieces.Add(new Piece(StreetPartRole.UnitNumber, Join(rest), Join(rest), Join(rest), Span(rest), "upu-germany-apartment"));
                }
            }

            // bpost: a street type is abbreviated only when the line is longer than 50 characters.
            Line line = new(StreetLineKind.Street, pieces);
            if (country.MaxLine > 0 && line.Postal.Length > country.MaxLine)
            {
                AbbreviateName(pieces, country);
            }

            return new Line(StreetLineKind.Street, pieces);
        }

        // bpost template 3: the box literal is bus in Dutch, bte in French; the name tells which.
        private static string BoxLiteral(List<Tok> nameToks)
        {
            T2Country be = T2Rules.Countries["BE"];
            if (nameToks.Count > 0 && T2Rules.FindType(new T2Country(be.Code, be.CountryLine, false, be.Languages, be.Postcode, be.Prefixes, be.PrintedPrefix, true, true, 0, 1, ["afnor"]), nameToks[0].Key) is not null)
            {
                return "bte";
            }

            string lastKey = nameToks.Count > 0 ? nameToks[^1].Key.Replace(" ", string.Empty, StringComparison.Ordinal) : string.Empty;
            return T2Rules.FindSuffixType(be, lastKey) is not null ? "bus" : "box";
        }

        private static void AbbreviateName(List<Piece> pieces, T2Country country)
        {
            int index = pieces.FindIndex(p => p.Role == StreetPartRole.StreetName);
            Piece name = pieces[index];
            string[] words = name.Standard.Split(' ');
            T2Type? lead = T2Rules.FindType(country, T2Rules.Key(words[0]));
            if (lead is { Abbreviation.Length: > 0 } && lead.Set == "bpost-fr")
            {
                words[0] = lead.Abbreviation;
            }
            else
            {
                string lastKey = T2Rules.Key(words[^1]).Replace(" ", string.Empty, StringComparison.Ordinal);
                T2Type? tail = T2Rules.FindSuffixType(country, lastKey);
                if (tail is { Abbreviation.Length: > 0 })
                {
                    string word = words[^1];
                    words[^1] = word[..(word.Length - tail.Full.Length)] + tail.Abbreviation;
                }
            }

            string standard = string.Join(' ', words);
            pieces[index] = name with { Standard = standard, Display = standard };
        }

        private static Piece OtherPiece(List<Tok> toks, T2Country country, bool afterStreet)
        {
            string written = Join(toks);
            string standard = country.Afnor ? T2Text.SaintAbbreviated(T2Text.Afnor(written)) : Fmt(country, written);
            return new Piece(StreetPartRole.Other, written, standard, T2Text.Title(written, capitalsKept: true), Span(toks), afterStreet ? "t2-line-5" : "t2-line");
        }

        private static Piece NamePiece(StreetPartRole role, List<Tok> toks, T2Country country, string rule)
        {
            string written = Join(toks);
            string standard = country.Afnor ? T2Text.Afnor(written) : written;
            return new Piece(role, written, standard, DisplayName(written), Span(toks), rule);
        }

        private static string DisplayName(string written, bool afterType = false) => T2Text.Title(written, capitalsKept: true, firstIsInitial: !afterType);

        // The Postal form of a delivery or locality text for the countries whose authority asks for capitals (Ireland's lines, Portugal) and for no punctuation from the
        // street line on (Italy: "Avoid using punctuation marks ... especially in lines 4, 5 and 6"; a period or comma between digits is part of the number).
        private static string Fmt(T2Country country, string text)
        {
            switch (country.Profile)
            {
                case "ie":
                case "pt":
                    return text.ToUpperInvariant();
                case "it":
                    StringBuilder sb = new(text.Length);
                    for (int i = 0; i < text.Length; i++)
                    {
                        char c = text[i];
                        bool between = i > 0 && i < text.Length - 1 && char.IsAsciiDigit(text[i - 1]) && char.IsAsciiDigit(text[i + 1]);
                        if (c is ',' or ';' or '.' && !between)
                        {
                            sb.Append(c == '.' ? string.Empty : " ");
                        }
                        else
                        {
                            sb.Append(c);
                        }
                    }

                    return string.Join(' ', sb.ToString().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
                default:
                    return text;
            }
        }

        // A house number, or the words that stand for "no number": s/n (sin numero), snc (senza numero civico).
        private static bool IsNumberToken(string s) => IsHouseNumber(s) || T2Rules.Key(s) is "S/N" or "SN" or "SNC";

        // What may follow a house number and still belong to the street line: staircase, floor and door words, numbers and ordinals, one or two letters (UPU Spain sheet:
        // "27, Esc 2, 3º, C"; UPU Portugal sheet: "31 RC ESQ"; UPU Italy sheet).
        private static bool TailOk(List<Tok> toks, int from)
        {
            if (toks.Count - from > 7)
            {
                return false;
            }

            for (int i = from; i < toks.Count; i++)
            {
                string t = toks[i].Text;
                bool ok = t.Any(char.IsAsciiDigit) || t.Count(char.IsLetter) <= 2 && t.Length <= 4 || T2Rules.UnitWords.Contains(toks[i].Key);
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        // Spanish, Portuguese and Italian single-line text splits the floor and door at commas ("Calle Huertas 18, 4º, C"): those pieces belong to the street line before them.
        private static void MergeFloorAndDoor(List<Unit> before)
        {
            for (int i = 1; i < before.Count; i++)
            {
                List<Tok> t = before[i].Toks;
                bool tail = t.Count > 0 && TailOk(t, 0) && (t[0].Text.Any(char.IsAsciiDigit) || T2Rules.UnitWords.Contains(t[0].Key) || t.Count == 1 && t[0].Text.Length <= 2)
                    && before[i - 1].Toks.Any(x => IsNumberToken(x.Text)) && !StartsWithSpatial(before[i - 1].Toks);
                if (tail)
                {
                    before[i - 1].Toks = [.. before[i - 1].Toks, .. t];
                    before.RemoveAt(i);
                    i--;
                }
            }
        }

        // Name then number, with the floor and door after it: "Plaza de las Descalzas 27, Esc 2, 3º, C" (UPU Spain sheet), "R LEAL DA CAMARA 31 RC ESQ" (UPU Portugal sheet),
        // "VIALE EUROPA 22" (UPU Italy sheet). The street line starts with a road type; without one the rules leave the line alone.
        private Line? ParseNameThenNumber(List<Tok> toks, T2Country country)
        {
            T2Type? type = null;
            int typeWords = 0;
            for (int words = Math.Min(3, toks.Count); words >= 1; words--)
            {
                type = T2Rules.FindType(country, string.Join(' ', toks.Take(words).Select(t => t.Key)));
                if (type is not null)
                {
                    typeWords = words;
                    break;
                }
            }

            if (type is null || toks.Count <= typeWords)
            {
                return null;
            }

            // The house number is the first number after at least one name word that leaves only floor and door words behind it: "Rua 25 de Abril 14" has its number last.
            int k = -1;
            for (int j = typeWords + 1; j < toks.Count; j++)
            {
                if (IsNumberToken(toks[j].Text) && TailOk(toks, j + 1))
                {
                    k = j;
                    break;
                }
            }

            List<Tok> nameToks = toks.GetRange(typeWords, (k < 0 ? toks.Count : k) - typeWords);
            if (nameToks.Count == 0)
            {
                return null;
            }

            List<Tok> typeToks = toks.GetRange(0, typeWords);
            List<Piece> pieces = [];
            string typeWritten = Join(typeToks);
            pieces.Add(new Piece(StreetPartRole.StreetType, typeWritten, Fmt(country, typeWritten), T2Text.Title(typeWritten, capitalsKept: true), Span(typeToks), "t2-street-type"));
            string nameWritten = Join(nameToks);
            pieces.Add(new Piece(StreetPartRole.StreetName, nameWritten, Fmt(country, nameWritten), DisplayName(nameWritten, afterType: true), Span(nameToks), "t2-street-name"));
            if (k >= 0)
            {
                pieces.Add(new Piece(StreetPartRole.HouseNumber, toks[k].Text, Fmt(country, toks[k].Text), toks[k].Text, toks[k].Span, "t2-house-number"));
                if (k < toks.Count - 1)
                {
                    List<Tok> tail = toks.GetRange(k + 1, toks.Count - k - 1);
                    SourceSpan span = Span(tail);

                    // Correos keeps the commas before the floor and door; the other authorities' lines have none.
                    bool commas = country.Profile == "es";
                    string tailText = commas
                        ? string.Join(' ', text.Substring(span.Start, span.Length).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                        : Join(tail);
                    pieces.Add(new Piece(StreetPartRole.UnitDesignator, tailText, Fmt(country, tailText), tailText, span, "t2-floor-and-door", commas ? ", " : " "));
                }
            }

            return new Line(StreetLineKind.Street, pieces);
        }

        private static string RoleName(StreetPartRole role) => role switch
        {
            StreetPartRole.HouseNumber => "house-number",
            StreetPartRole.StreetName => "name",
            StreetPartRole.StreetType => "type",
            StreetPartRole.UnitDesignator => "unit-designator",
            StreetPartRole.UnitNumber => "unit-number",
            StreetPartRole.BoxDesignator => "box-designator",
            StreetPartRole.BoxNumber => "box-number",
            StreetPartRole.GeneralDelivery => "general-delivery",
            _ => "other",
        };

        private static string Join(IEnumerable<Tok> toks) => string.Join(' ', toks.Select(t => t.Text));

        private static SourceSpan Span(IEnumerable<Tok> toks)
        {
            Tok[] a = [.. toks];
            return a.Length == 0 ? default : new SourceSpan(a[0].Start, a[^1].End - a[0].Start);
        }
    }
}
