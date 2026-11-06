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

        // Whether the post town stood on the postcode's own line ("Birmingham B6 5BA"); then no line above it can be taken for it.
        private bool _localityInline;

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
            _localityInline = false;
            _state = null;
            _stateAbbreviation = string.Empty;
            _huLocalityIndex = -1;
            int unitIndex;
            int tokenIndex;
            PostcodeMatch match;
            if (!FindAustralian(units, known, out unitIndex, out tokenIndex, out match)
                && !FindLastLine(units, known, out unitIndex, out tokenIndex, out match)
                && !FindPostcodeLast(units, known, out unitIndex, out tokenIndex, out match)
                && !FindProvinceLine(units, known, out unitIndex, out tokenIndex, out match)
                && !FindHungarian(units, known, out unitIndex, out tokenIndex, out match)
                && !FindStateLine(units, known, out unitIndex, out tokenIndex, out match))
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
            if (code == "IT" && localityToks.Count == 1 && localityToks[0].Text.StartsWith('(') && ItProvinceCode(localityToks[0]) is not null)
            {
                // "33070 (pn)": a province code and no locality; nothing is taken for the locality.
                detail = ParseDetail.Failure(ReasonCode.AddressNoLastLine, new SourceSpan(0, text.Length));
                return false;
            }

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
                    MergeFloorAndDoor(before, country);
                }
                else if (country.IsCentral)
                {
                    MergeCentral(before, country);
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

            // A unit line right above the street line ("Flat 30", "17º B") belongs with it.
            while (first > 0 && parsed[first - 1] is { Kind: StreetLineKind.SecondaryOnly })
            {
                first--;
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

            // Britain and Ireland: with two lines between the street and the postcode, either may be the post town (a dependent locality above it, or a county below it).
            // The Royal Mail convention that the post town is in capitals settles it when the other line is not; anything else is left to the model.
            if (country.Code is "GB" or "IE" && !bulk && _county is null && !_localityInline)
            {
                List<Tok>[] extra = [.. Enumerable.Range(first + 1, Math.Max(0, before.Count - first - 1)).Where(i => parsed[i] is null).Select(i => before[i].Toks)];
                string loc = Join(localityToks);
                bool capitals = loc.Any(char.IsLetter) && !loc.Any(char.IsLower);
                if (extra.Length > 0 && !(capitals && extra.All(e => e.Any(t => t.Text.Any(char.IsLower)))))
                {
                    detail = ParseDetail.Failure(ReasonCode.AddressModelRequired, new SourceSpan(0, text.Length));
                    return false;
                }
            }

            List<ParsedAddressText> leading = [];
            List<(string Postal, string Display)> leadingLayout = [];
            for (int i = 0; i < first; i++)
            {
                List<Tok> toks = before[i].Toks;
                // The last group's lines keep the commas the writer put in them ("MOTOSPORT, a. s.").
                string written = country.IsCentral ? Slice(toks) : Join(toks);
                string standard = country.Afnor ? T2Text.Afnor(written)
                    : country.Profile is "pt" or "it" ? string.Join(' ', toks.Select(t => t.Text)).ToUpperInvariant()
                    : country.IsCentral ? written
                    : country.Profile.Length > 0 && !country.IsNorth ? Slice(Span(toks))
                    : string.Join(' ', toks.Select(t => t.Text));
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
                int houseAt = line.Pieces.FindLastIndex(p => p.Role == StreetPartRole.HouseNumber);
                if (country.Profile is "it" or "sm" or "va" && line.Kind == StreetLineKind.Street && houseAt >= 0 && houseAt < line.Pieces.Count - 1
                    && line.Pieces.Skip(houseAt + 1).All(p => p.Role is StreetPartRole.UnitDesignator or StreetPartRole.UnitNumber))
                {
                    // Poste Italiane, Standard di composizione indirizzi, 1.1 and 3.1: nothing follows the civic number; the unit (interno, piano, scala) goes on the building line above.
                    Line unitLine = new(StreetLineKind.SecondaryOnly, line.Pieces.Skip(houseAt + 1).ToList());
                    Line streetLine = new(line.Kind, line.Pieces.Take(houseAt + 1).ToList());
                    deliveryLayout.Add((unitLine.Postal, unitLine.Display));
                    deliveryLayout.Add((streetLine.Postal, streetLine.Display));
                }
                else
                {
                    deliveryLayout.Add((line.Postal, line.Display));
                }
                facts |= line.Kind == StreetLineKind.PostOfficeBox ? AddressFacts.PostOfficeBox : AddressFacts.None;
                facts |= line.Pieces.Any(p => p.Role is StreetPartRole.UnitDesignator or StreetPartRole.UnitNumber) ? AddressFacts.HasUnit : AddressFacts.None;
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
                // UPU Italy sheet: "postcode + locality + province code". The code may stand in parentheses; the field is the letters.
                Tok last = localityToks[^1];
                localityToks.RemoveAt(localityToks.Count - 1);
                SourceSpan inner = InnerSpan([last]);
                adminInfo = (Slice(inner), province, inner, "upu-italy-province");
                adminInline = province;
            }
            else if (code == "ES" && localityToks.Count >= 2 && ProvinceAtEnd(localityToks, match.Standard) is { } inner)
            {
                localityToks.RemoveRange(localityToks.Count - inner.Count, inner.Count);
                SourceSpan span = InnerSpan(inner);
                string name = Slice(span);
                adminInfo = (name, name.ToUpperInvariant(), span, "upu-spain-province");
                adminLine = name.ToUpperInvariant();
                adminWords = name;
            }
            else if (code == "ES" && _province is not null)
            {
                adminInfo = (Slice(Span(_province)), Join(_province).ToUpperInvariant(), Span(_province), "upu-spain-province");
                adminLine = Join(_province).ToUpperInvariant();
                adminWords = Join(_province);
            }
            else if (code == "AU" && localityToks.Count >= 2 && T2Rules.States.ContainsKey(("AU", localityToks[0].Key)))
            {
                // Rearranged by FindAustralian: the state stands first, before the locality.
                Tok state = localityToks[0];
                localityToks.RemoveAt(0);
                adminInfo = (state.Text, state.Text, state.Span, "upu-australia-state");
                adminInline = state.Text;
            }
            else if (code == "MX" && _state is not null)
            {
                adminInfo = (Slice(Span(_state)), _stateAbbreviation, Span(_state), "upu-mexico-state");
                adminInline = _stateAbbreviation;
            }
            else if (code == "MX" && localityToks.Count >= 2 && StateTail("MX", localityToks, out int stateWords, out string stateAbbreviation) && stateWords < localityToks.Count)
            {
                List<Tok> stateToks = localityToks.GetRange(localityToks.Count - stateWords, stateWords);
                localityToks.RemoveRange(localityToks.Count - stateWords, stateWords);
                adminInfo = (Slice(Span(stateToks)), stateAbbreviation, Span(stateToks), "upu-mexico-state");
                adminInline = stateAbbreviation;
            }
            else if (code == "IE" && _county is not null)
            {
                adminInfo = (Slice(Span(_county)), Join(_county).ToUpperInvariant(), Span(_county), "upu-ireland-county");
                adminLine = Join(_county).ToUpperInvariant();
                adminWords = Join(_county);
            }

            string localityWritten = Join(localityToks);
            string localityPostal = country.Afnor ? T2Text.SaintAbbreviated(T2Text.Afnor(localityWritten)) : country.Profile == "lt" || country.LocalityAsWritten ? localityWritten : localityWritten.ToUpperInvariant();
            string localityDisplay = T2Text.Title(localityWritten, capitalsKept: true);
            string postcodeStd = match.Standard;
            string printed = country.PrintedPrefix + postcodeStd;
            string separator = new(' ', country.LocalitySpaces);
            string adminLineDisplay = adminLine.Length == 0 ? string.Empty : T2Text.Title(adminWords, capitalsKept: true);
            string lastPostal;
            string lastDisplay;
            if (country.IsCentral && CentralLast(country, printed, localityPostal, localityDisplay, adminInline, out string centralPostal, out string centralDisplay))
            {
                lastPostal = centralPostal;
                lastDisplay = centralDisplay;
            }
            else if (country.PostcodeLast)
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
                Tier2 = new T2Layout(country, leadingLayout, deliveryLayout, (lastPostal, lastDisplay))
                {
                    // UPU Hungary sheet: the locality is its own line above the street line, the postcode alone below it.
                    LocalityLine = country.Profile == "hu" ? (localityPostal, localityDisplay) : null,
                    LocalityAfter = _huLocalityIndex >= 0 ? Math.Min(_huLocalityIndex, leadingLayout.Count) : leadingLayout.Count,
                },
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
                    // Spanish "C/Botanico": the type and the name are written together; they are two words (Correos' usual abbreviation of Calle).
                    if (e - s > 4 && text[s] is 'C' or 'c' && text[s + 1] == '/' && char.IsLetter(text[s + 2]))
                    {
                        toks.Add(new Tok(text.Substring(s, 2), s));
                        toks.Add(new Tok(text.Substring(s + 2, e - s - 2), s + 2));
                    }
                    else
                    {
                        toks.Add(new Tok(text.Substring(s, e - s), s));
                    }
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

            _localityInline = locality.Count > 0;
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

            if (!LocalityFits(locality, ireland) || T2Rules.CountryNames.TryGetValue(T2Rules.Key(Join(locality)), out string? nation) && nation is not ("JE" or "GG"))
            {
                // A country or nation name above the postcode (Jersey Post writes "ENGLAND" there for addresses in Britain) is not a post town.
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

                if (a.Length == 3 && a.All(char.IsAsciiLetterUpper) && b.Length == 4 && b.All(char.IsAsciiDigit) && !T2Rules.States.ContainsKey(("AU", a)))
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
            bool paren = t.Text.StartsWith('(') && t.Text.EndsWith(')');
            string code = t.Text.Trim('(', ')');
            if (code.Length != 2 || !(paren ? code.All(char.IsAsciiLetter) : code.All(char.IsAsciiLetterUpper)))
            {
                return null;
            }

            code = code.ToUpperInvariant();
            return T2Rules.ItProvinces.Contains(code) ? code : null;
        }

        // A province ends the locality of a Spanish last line when it stands in parentheses ("Madrid (Madrid)") or is set apart by a comma ("26130 Ribafrecha, La Rioja"),
        // and its own two digits are the postcode's first two (UPU Spain sheet: province list). Returns its tokens.
        private List<Tok>? ProvinceAtEnd(List<Tok> toks, string postcode)
        {
            for (int n = 1; n <= 4 && n < toks.Count; n++)
            {
                List<Tok> candidate = toks.GetRange(toks.Count - n, n);
                bool paren = candidate[0].Text.StartsWith('(') && candidate[^1].Text.EndsWith(')');
                bool comma = false;
                for (int i = candidate[0].Start - 1; i >= 0 && !comma; i--)
                {
                    if (text[i] == ',')
                    {
                        comma = true;
                    }
                    else if (!char.IsWhiteSpace(text[i]))
                    {
                        break;
                    }
                }

                if ((paren || comma) && T2Rules.EsProvinces.TryGetValue(T2Rules.Key(InnerText(candidate)), out string? prefix) && postcode.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string InnerText(List<Tok> toks) => Join(toks).Trim('(', ')');

        // The words of a field written in parentheses, without the parentheses.
        private static SourceSpan InnerSpan(List<Tok> toks)
        {
            int start = toks[0].Start + (toks[0].Text.StartsWith('(') ? 1 : 0);
            int end = toks[^1].End - (toks[^1].Text.EndsWith(')') ? 1 : 0);
            return new SourceSpan(start, end - start);
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
                // Three digits, a space and two digits are Swedish and also Czech and Slovak (UPU sheets): without a prefix or a known country all three stay candidates.
                if (known is null && north.PrefixCountry is null && north.Candidates is ["SE"] && MatchCentralForm(toks, i, t, null, null) is { Candidates: ["CZ", "SK"] })
                {
                    return north with { Candidates = ["SE", "CZ", "SK"] };
                }

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

            if (MatchCentralForm(toks, i, digits, prefixCountry, known) is { } central)
            {
                return central;
            }

            // "AD-500" is how Andorra's own post offices often write it; the postcode is AD500.
            if (t.Length == 6 && t[..3].Equals("AD-", StringComparison.OrdinalIgnoreCase) && t[3..].All(char.IsAsciiDigit))
            {
                return new PostcodeMatch(1, "AD" + t[3..], ["AD"], null);
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
                    ? (prefixCountry is "FR" or "DE" or "MC" or "ES" or "IT" or "SM" or "VA" or "FI" or "EE" or "LT" or "HR" or "MX" ? [prefixCountry] : [])
                    : digits == "00120" ? ["VA"]
                    : digits.StartsWith("4789", StringComparison.Ordinal) ? ["SM"]
                    : known == "IT" ? ["IT"]
                    : digits.StartsWith("980", StringComparison.Ordinal) ? ["MC"]
                    : digits.StartsWith("97", StringComparison.Ordinal) ? ["FR"]
                    : ["FR", "DE", "ES", "IT", "HR", "MX"];
                return candidates.Count == 0 ? null : new PostcodeMatch(1, digits, candidates, prefixCountry);
            }

            if (digits.Length == 4)
            {
                List<string> candidates = prefixCountry is not null
                    ? (prefixCountry is "BE" or "AT" or "CH" or "LI" or "LU" or "DK" or "NO" or "SI" or "HU" ? [prefixCountry] : [])
                    : ["BE", "AT", "CH", "LI", "LU", "DK", "NO", "SI", "HU"];
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
            only ??= CentralCue(match, before, lastToks);
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
                bool province = lastToks.Count >= 3 && ItProvinceCode(lastToks[^1]) is { } pc && !T2Rules.Countries.ContainsKey(pc);
                if (province && french == 0 && german == 0)
                {
                    return "IT";
                }

                return french > 0 && german == 0 && !province ? "FR" : german > 0 && french == 0 && !province ? "DE" : null;
            }

            return canton ? "CH" : null;
        }

        // ---- delivery lines ----------------------------------------------------------------------------------------------------------------------------

        private static bool IsLoneNumber(Unit u, T2Country country) =>
            u.Toks.Count is >= 1 and <= 2 && IsHouseNumber(u.Toks[0].Text) && (u.Toks.Count == 1 || IsAddition(u.Toks[1].Text))
            || country.Profile is "es" or "pt" or "it" or "sm" or "va" or "ad" && u.Toks.Count > 1 && IsNumberToken(u.Toks[0].Text) && TailOk(u.Toks, 1)
            || country.Profile is "uk" or "ie" or "mt" && u.Toks.Count == 3 && T2Rules.IsUnitDesignator(country.Code, u.Toks[0].Key) && IsUnitNumber(u.Toks[1].Text) && IsHouseNumber(u.Toks[2].Text);

        // Whether a unit already holds a complete street line with its house number (the Iberian and Italian profiles), or any number at all (the others).
        private bool HasHouseNumber(Unit u, T2Country country) =>
            country.Profile is "es" or "pt" or "it" or "sm" or "va" or "ad"
                ? ParseNameThenNumber(u.Toks, country) is { } line && line.Pieces.Any(p => p.Role == StreetPartRole.HouseNumber)
                : u.Toks.Any(t => t.Text.Any(char.IsAsciiDigit));

        private void MergeLoneNumbers(List<Unit> before, T2Country country)
        {
            for (int i = 0; i < before.Count; i++)
            {
                if (!IsLoneNumber(before[i], country) || before.Count < 2)
                {
                    continue;
                }

                bool prevHasNumber = i > 0 && HasHouseNumber(before[i - 1], country);
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
            if (toks.Count == 0)
            {
                return null;
            }

            Line? unit = country.Profile == "au" ? null : ParseUnitLine(toks, code, country, allowStreet);
            if (unit is not null)
            {
                return unit;
            }

            // Australia: "Flat 2 17 Jones St" and "Unit 4 28 Paxton Ct" are street lines (Australia Post, Address Presentation Standards p. 14), so the place-in-building words do not apply.
            if (country.Profile != "au" && StartsWithSpatial(toks))
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

            if (country.IsCentral)
            {
                return ParseCentral(toks, country);
            }

            if (country.IsNorth)
            {
                return ParseNorth(toks);
            }

            // Malta and Andorra write the number on either side of the name: "27 Andilina" or "Triq San Guzepp 27". A type first and a house number after the name settle it.
            if (country.NumberFirst && country.NumberLast && country.Profile.Length > 0 && !IsHouseNumber(toks[0].Text) && ParseNameThenNumber(toks, country) is { } nameFirst
                && nameFirst.Pieces.Any(p => p.Role == StreetPartRole.HouseNumber))
            {
                return nameFirst;
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

        // "Flat 30", "Apt 6 123 Etive Rd", "Interno 2", "17º B": a unit inside the building, alone on its line or in front of the street line. A designator word is
        // followed by one number or letter (UPU Italy sheet: "INTERNO 12 PIANO 10"; UPU Andorra sheet: "Apartament 2"); the Spanish and Portuguese floor and door
        // ("3º, C", "R/C Esq.") stand for a unit number as they are.
        private Line? ParseUnitLine(List<Tok> toks, string code, T2Country country, bool allowStreet)
        {
            if (country.Profile.Length == 0)
            {
                return null;
            }

            string codeKey = code;
            if (T2Rules.IsUnitDesignator(codeKey, toks[0].Key) && toks.Count >= 2 && IsUnitNumber(toks[1].Text))
            {
                List<Piece> pieces = UnitPieces(toks, 0, country, out int used);
                while (used < toks.Count && T2Rules.IsUnitDesignator(codeKey, toks[used].Key) && used + 1 < toks.Count && IsUnitNumber(toks[used + 1].Text))
                {
                    pieces.AddRange(UnitPieces(toks, used, country, out int more));
                    used += more;
                }

                if (used == toks.Count)
                {
                    return new Line(StreetLineKind.SecondaryOnly, pieces);
                }

                if (allowStreet)
                {
                    List<Tok> rest = toks.GetRange(used, toks.Count - used);
                    Line? street = ParseDeliveryLine(rest, code, country, allowStreet: true);
                    if (street is { Kind: StreetLineKind.Street or StreetLineKind.PostOfficeBox })
                    {
                        return new Line(street.Kind, [.. pieces, .. street.Pieces]);
                    }
                }

                return null;
            }

            // Floor and door alone: "17º B", "9º E", "R/C Esq."
            if (country.Profile is "es" or "pt" or "ad" && toks.Count <= 3 && IsFloorOrDoorFirst(toks[0]) && TailOk(toks, 1))
            {
                SourceSpan span = Span(toks);
                string written = Join(toks);
                return new Line(StreetLineKind.SecondaryOnly, [new Piece(StreetPartRole.UnitNumber, written, Fmt(country, written), written, span, "t2-floor-and-door")]);
            }

            return null;
        }

        private static List<Piece> UnitPieces(List<Tok> toks, int at, T2Country country, out int used)
        {
            used = 2;
            Tok d = toks[at];
            Tok n = toks[at + 1];
            return
            [
                new Piece(StreetPartRole.UnitDesignator, d.Text, Fmt(country, d.Text), T2Text.TitleWord(d.Text, first: true), d.Span, "t2-unit-designator"),
                new Piece(StreetPartRole.UnitNumber, n.Text, Fmt(country, n.Text), n.Text, n.Span, "t2-unit-number"),
            ];
        }

        // A unit number or letter: one to four letters and digits with at least one digit, or a single letter.
        private static bool IsUnitNumber(string s) =>
            s.Length is >= 1 and <= 4 && s.All(char.IsLetterOrDigit) && (s.Any(char.IsAsciiDigit) || s.Length == 1);

        private static bool IsFloorOrDoorFirst(Tok t) =>
            t.Text.Any(char.IsAsciiDigit) && t.Text.Any(c => c is '\u00BA' or '\u00AA' or '\u00B0') || t.Key is "BAJO" or "ENTRESUELO" or "ATICO" or "SOTANO" or "R/C" or "RC";

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
                // Royal Mail and Jersey Post print "PO Box 61" as the writer wrote it (the UPU sheets print "P.O. Box 177"), so these countries keep the writer's form.
                string printed = country.Afnor ? T2Text.Afnor(d.Printed) : country.Profile is "uk" or "ie" or "mt" ? Join(toks.Take(words)) : Fmt(country, d.Printed);
                string displayPrinted = T2Text.Title(d.Printed, capitalsKept: true);
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
                // Poland writes "skr. poczt. Nr 115" (Poczta Polska); Mexico and the Czech Republic print box numbers like "70-123" and "B/52" (UPU Mexico sheet, Czech Post).
                int first = words + (country.IsCentral && words < toks.Count - 1 && toks[words].Key is "NR" or "NO" ? 1 : 0);
                int n = first;
                while (n < toks.Count && (IsBoxNumber(toks[n].Text) || country.IsCentral && IsCentralBoxNumber(toks[n].Text)) && (n == first || toks[n].Text.All(char.IsAsciiDigit)))
                {
                    n++;
                }

                if (n == first && d.Role != "station")
                {
                    return null;
                }

                // The marker belongs to the designator and is printed as written ("skr. poczt. Nr 115").
                if (first > words)
                {
                    printed += " " + toks[words].Text;
                    displayPrinted += " " + toks[words].Text;
                    words = first;
                }

                // A pick-up point named instead of numbered (bpack 24/7, bpost template 3): the words after it are its name.
                if (n == words)
                {
                    pieces.Add(new Piece(StreetPartRole.BoxDesignator, Join(toks.Take(words)), printed, displayPrinted, Span(toks.Take(words)), "t2-pickup-point"));
                    if (toks.Count > words)
                    {
                        pieces.Add(OtherPiece(toks.GetRange(words, toks.Count - words), country, afterStreet: true));
                    }

                    return new Line(StreetLineKind.PostOfficeBox, pieces);
                }

                List<Tok> number = toks.GetRange(words, n - words);
                string numberStandard = country.Code == "BE" ? string.Concat(number.Select(t => t.Text)) : Fmt(country, string.Join(' ', number.Select(t => t.Text)));
                pieces.Add(new Piece(StreetPartRole.BoxDesignator, Join(toks.Take(words)), printed, displayPrinted, Span(toks.Take(words)), "t2-box-designator"));
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

            // The United Kingdom, Ireland and Malta write the English type last: "49 Featherstone Street", "142B Mount Vernon Rd". With a number any listed type will do;
            // without one only a plain road type is taken, because "Rose Hill" may be the name of a house.
            T2Type? tailType = null;
            if (type is null && country.Profile is "uk" or "ie" or "mt" && toks.Count - i >= 2 && T2Rules.FindTailType(toks[^1].Key) is { } found
                && (numbered || found.Full is "ROAD" or "STREET" or "LANE" or "AVENUE" or "DRIVE" or "CLOSE" or "CRESCENT" or "TERRACE" or "GARDENS"))
            {
                tailType = found;
            }

            if (tailType is not null)
            {
                List<Tok> tailName = toks.GetRange(i, toks.Count - 1 - i);
                string tailNameWritten = Join(tailName);
                pieces.Add(new Piece(StreetPartRole.StreetName, tailNameWritten, Fmt(country, tailNameWritten), DisplayName(tailNameWritten), Span(tailName), "t2-street-name"));
                string tailTypeWritten = toks[^1].Text;
                pieces.Add(new Piece(StreetPartRole.StreetType, tailTypeWritten, Fmt(country, tailTypeWritten), T2Text.Title(tailTypeWritten, capitalsKept: true), TypeSpan([toks[^1]]), "t2-street-type"));
                return new Line(StreetLineKind.Street, pieces);
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
                pieces.Add(new Piece(StreetPartRole.StreetType, typeWritten, full, country.Afnor ? T2Text.Title(full, capitalsKept: true) : full, TypeSpan(typeToks), "t2-street-type"));
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

        // The Postal form of a delivery or locality text for the countries whose authority asks for capitals (Portugal) and for no punctuation from the
        // street line on (Italy: "Avoid using punctuation marks ... especially in lines 4, 5 and 6"; a period or comma between digits is part of the number).
        private static string Fmt(T2Country country, string text)
        {
            switch (country.Profile)
            {
                case "pt":
                    // CTT, Manual de Endereçamento p. 7: no period closes an abbreviation; words are separated by a space.
                    return string.Join(' ', text.ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Length > 1 && w.EndsWith('.') ? w[..^1] : w));
                case "au":
                    // Australia Post: the address proper has no punctuation, and the street and locality lines are capitals.
                    return AuText(text).ToUpperInvariant();
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

        // What may follow a house number and still belong to the street line: staircase, floor and door words, a floor written with an ordinal sign ("3º", "6.º"),
        // a single letter (the door), and a plain number only straight after a unit word ("Piso 4", "Esc 2"). UPU Spain sheet: "27, Esc 2, 3º, C"; UPU Portugal sheet:
        // "31 RC ESQ"; UPU Italy sheet. A bare number after the house number is not one of these ("Nacional 1, 105": 105 is the house number).
        private static bool TailOk(List<Tok> toks, int from)
        {
            if (toks.Count - from > 7)
            {
                return false;
            }

            bool afterUnitWord = false;
            for (int i = from; i < toks.Count; i++)
            {
                string t = toks[i].Text;
                bool unitWord = T2Rules.UnitWords.Contains(toks[i].Key);
                bool ordinal = t.Any(char.IsAsciiDigit) && t.Any(c => c is '\u00BA' or '\u00AA' or '\u00B0');
                bool letter = t.Length == 1 && char.IsLetter(t[0]);
                bool plain = afterUnitWord && t.Length <= 4 && t.All(char.IsLetterOrDigit);
                if (!(unitWord || ordinal || letter || plain))
                {
                    return false;
                }

                afterUnitWord = unitWord;
            }

            return true;
        }

        // Spanish, Portuguese and Italian single-line text splits the floor and door at commas ("Calle Huertas 18, 4º, C"): those pieces belong to the street line before them.
        private void MergeFloorAndDoor(List<Unit> before, T2Country country)
        {
            for (int i = 1; i < before.Count; i++)
            {
                List<Tok> t = before[i].Toks;
                bool tail = t.Count > 0 && TailOk(t, 0) && (t[0].Text.Any(char.IsAsciiDigit) || T2Rules.UnitWords.Contains(t[0].Key) || t.Count == 1 && t[0].Text.Length <= 2)
                    && HasHouseNumber(before[i - 1], country) && !StartsWithSpatial(before[i - 1].Toks);
                if (tail)
                {
                    before[i - 1].Toks = [.. before[i - 1].Toks, .. t];
                    before.RemoveAt(i);
                    i--;
                }
            }
        }

        // The span of a type word: a period that closes an abbreviation ("Av.", "Rd.") is not part of the field.
        private static SourceSpan TypeSpan(IEnumerable<Tok> toks)
        {
            Tok[] a = [.. toks];
            SourceSpan span = Span(a);
            return a.Length > 0 && a[^1].Text.Length > 1 && a[^1].Text.EndsWith('.') && a[^1].Text.IndexOf('.', StringComparison.Ordinal) == a[^1].Text.Length - 1
                ? new SourceSpan(span.Start, span.Length - 1)
                : span;
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
            pieces.Add(new Piece(StreetPartRole.StreetType, typeWritten, Fmt(country, typeWritten), T2Text.Title(typeWritten, capitalsKept: true), TypeSpan(typeToks), "t2-street-type"));
            string nameWritten = Join(nameToks);
            pieces.Add(new Piece(StreetPartRole.StreetName, nameWritten, Fmt(country, nameWritten), DisplayName(nameWritten, afterType: true), Span(nameToks), "t2-street-name"));
            if (k >= 0)
            {
                // Italy writes "25 D" for number 25, letter D (the letter is part of the civic number); Spain and Portugal write the door after the number.
                int numberEnd = k;
                if (country.Profile is "it" or "sm" or "va" && k + 1 < toks.Count && toks[k + 1].Text.Length == 1 && char.IsLetter(toks[k + 1].Text[0]))
                {
                    numberEnd = k + 1;
                }

                List<Tok> numberToks = toks.GetRange(k, numberEnd - k + 1);
                string numberWritten = Join(numberToks);
                pieces.Add(new Piece(StreetPartRole.HouseNumber, numberWritten, Fmt(country, numberWritten), numberWritten, Span(numberToks), "t2-house-number"));
                k = numberEnd;
                if (k < toks.Count - 1)
                {
                    List<Tok> tail = toks.GetRange(k + 1, toks.Count - k - 1);
                    SourceSpan span = Span(tail);

                    // Correos keeps the commas before the floor and door; the other authorities' lines have none.
                    bool commas = country.Profile == "es";
                    string before = commas ? ", " : " ";
                    if (tail.Count >= 2 && T2Rules.IsUnitDesignator(country.Code, tail[0].Key) && IsUnitNumber(tail[1].Text) && tail.Count == 2)
                    {
                        pieces.Add(new Piece(StreetPartRole.UnitDesignator, tail[0].Text, Fmt(country, tail[0].Text), tail[0].Text, tail[0].Span, "t2-unit-designator", before));
                        pieces.Add(new Piece(StreetPartRole.UnitNumber, tail[1].Text, Fmt(country, tail[1].Text), tail[1].Text, tail[1].Span, "t2-unit-number"));
                    }
                    else if (tail.Count > 2 && T2Rules.IsUnitDesignator(country.Code, tail[0].Key))
                    {
                        // "Piano 6 Scala B": each designator takes the word after it.
                        int at = 0;
                        while (at + 1 < tail.Count && T2Rules.IsUnitDesignator(country.Code, tail[at].Key) && IsUnitNumber(tail[at + 1].Text))
                        {
                            pieces.Add(new Piece(StreetPartRole.UnitDesignator, tail[at].Text, Fmt(country, tail[at].Text), tail[at].Text, tail[at].Span, "t2-unit-designator", at == 0 ? before : " "));
                            pieces.Add(new Piece(StreetPartRole.UnitNumber, tail[at + 1].Text, Fmt(country, tail[at + 1].Text), tail[at + 1].Text, tail[at + 1].Span, "t2-unit-number"));
                            at += 2;
                        }

                        if (at < tail.Count)
                        {
                            return null;
                        }
                    }
                    else
                    {
                        string tailText = commas
                            ? string.Join(' ', text.Substring(span.Start, span.Length).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                            : Join(tail);
                        pieces.Add(new Piece(StreetPartRole.UnitNumber, tailText, Fmt(country, tailText), tailText, span, "t2-floor-and-door", before));
                    }
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
