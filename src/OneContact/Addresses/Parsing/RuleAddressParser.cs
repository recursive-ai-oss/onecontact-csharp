using System.Buffers;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Addresses.Streets;

namespace RecursiveAI.OneContact.Addresses.Parsing;

/// <summary>
/// Reads free-text addresses of the United States (with Puerto Rico and the territories USPS serves) and Canada by rules, with offsets for every field
/// and the rule that decided each one. Input the rules cannot decide goes to the address model when a model package is referenced (it labels the
/// fields, and a field a rule found is never replaced); without one it returns an issue whose reason names the package to add.
/// </summary>
/// <remarks>
/// <para>Input is one line (with commas between the parts, or none) or several lines. The last line is found from the right: a ZIP or ZIP+4 or a Canadian postal
/// code, then a state, territory or province (a code or a name), then the city. The country comes from that content (a ZIP, a postal code, a state or province, an
/// explicit country name) or from the hint. Lines above the delivery line (recipient, organization, attention) are kept as written.</para>
/// <para>Nothing is invented: no city from a postal code, no province from a forward sortation area, no country when the content and the hint are silent.</para>
/// </remarks>
public static class RuleAddressParser
{
    internal static readonly HashSet<string> UsCountryNames = new(StringComparer.Ordinal) { "USA", "US", "UNITED STATES", "UNITED STATES OF AMERICA", "U S A" };
    internal static readonly HashSet<string> CaCountryNames = new(StringComparer.Ordinal) { "CANADA", "CAN" };

    /// <summary>Reads an address.</summary>
    /// <param name="input">The text: one or several lines. Offsets in the result index this span.</param>
    /// <param name="hint">The country to assume when the content does not say; <see cref="Region.Unknown"/> for none. A hint outside the United States, Canada and the Tier 2 countries (France with its overseas departments, Monaco, Belgium, the Netherlands, Luxembourg, Germany, Austria, Switzerland, Liechtenstein; the United Kingdom with Jersey, Guernsey and the Isle of Man, Ireland, Spain, Portugal, Italy, San Marino, the Vatican, Malta, Andorra) is refused.</param>
    /// <param name="address">The parsed address, or <see langword="null"/> when the status is not <see cref="OperationStatus.Done"/>.</param>
    /// <param name="detail">Issues, and per field its offsets and decision source.</param>
    /// <returns><see cref="OperationStatus.Done"/>, or <see cref="OperationStatus.InvalidData"/> with the reason in <paramref name="detail"/>.</returns>
    public static OperationStatus TryParse(ReadOnlySpan<char> input, Region hint, out ParsedAddress? address, out ParseDetail detail)
    {
        // A Tier 2 country is read by its own rules. Without a hint the United States and Canada are tried first, and text they cannot read is
        // offered to the Tier 2 rules, which read it only when the postcode and the content point to one country.
        if (Tier2.Tier2Reader.Handles(hint))
        {
            string hinted = input.ToString();
            OperationStatus hintedStatus = Tier2.Tier2Reader.TryParse(hinted, hint, out address, out detail, out Tier2.T2Country? hintedCountry);
            return hintedStatus == OperationStatus.Done ? hintedStatus : ReadTier2WithModel(hinted, hint, hintedCountry, hintedStatus, ref address, ref detail);
        }

        // A country name, a country letter before the postcode (L-1234) or a Dutch postcode names the country outright, so it is read before the United States rules
        // could take the five digits for a ZIP.
        ParsedAddress? other = null;
        ParseDetail otherDetail = ParseDetail.Failure(ReasonCode.AddressNoLastLine, default);
        OperationStatus otherStatus = OperationStatus.InvalidData;
        if (hint.IsUnknown)
        {
            otherStatus = Tier2.Tier2Reader.TryParse(input.ToString(), hint, out other, out otherDetail, out Tier2.T2Country? otherCountry);
            if (otherStatus == OperationStatus.Done && other is { CountryConfidence: >= 0.97f })
            {
                address = other;
                detail = otherDetail;
                return OperationStatus.Done;
            }

            // The Tier 2 rules decided the country and then refused the address: the Tier 2 model reads it, before the United States and Canada rules
            // could take its postcode for a ZIP.
            if (otherStatus != OperationStatus.Done && otherCountry is not null)
            {
                ParsedAddress? modelRead = null;
                ParseDetail modelDetail = otherDetail;
                if (ReadTier2WithModel(input.ToString(), hint, otherCountry, otherStatus, ref modelRead, ref modelDetail) == OperationStatus.Done)
                {
                    address = modelRead;
                    detail = modelDetail;
                    return OperationStatus.Done;
                }
            }
        }

        OperationStatus status = TryParseUsCa(input, hint, out address, out detail);
        if (status != OperationStatus.Done && otherStatus == OperationStatus.Done)
        {
            address = other;
            detail = otherDetail;
            return OperationStatus.Done;
        }

        // Text that has a postcode and a locality of a Tier 2 country but no way to tell which: the reason is more useful than "no last line".
        if (status != OperationStatus.Done && hint.IsUnknown && detail.PrimaryReason.Equals(ReasonCode.AddressNoLastLine)
            && (otherDetail.PrimaryReason.Equals(ReasonCode.AddressCountryUndetermined) || otherDetail.PrimaryReason.Equals(ReasonCode.AddressCountryConflict)))
        {
            detail = otherDetail;
        }

        return status;
    }

    // Input the Tier 2 rules refused for want of a decision is read from the model's labels, when the registered package carries Tier 2 models.
    // The Tier 1 package does not (its weights know the United States and Canada only), so it never reads a Tier 2 address; the refusal stays.
    private static OperationStatus ReadTier2WithModel(string text, Region hint, Tier2.T2Country? decided, OperationStatus status, ref ParsedAddress? address, ref ParseDetail detail)
    {
        Models.IAddressFieldModel? model = Models.ContactModels.Address;
        string? package = Models.ContactModels.PackageId;
        if (model is null || package is null || package == Models.ContactModels.Tier1PackageId)
        {
            return status;
        }

        ReasonCode reason = detail.PrimaryReason;
        if (!(reason.Equals(ReasonCode.AddressModelRequired) || reason.Equals(ReasonCode.AddressNoLastLine) || reason.Equals(ReasonCode.AddressNoDeliveryLine)))
        {
            return status;
        }

        if (Tier2.Tier2Reader.TryReadModel(model, text, hint, decided, out ParsedAddress? read, out ParseDetail modelDetail) == OperationStatus.Done)
        {
            address = read;
            detail = modelDetail;
            return OperationStatus.Done;
        }

        return status;
    }

    private static OperationStatus TryParseUsCa(ReadOnlySpan<char> input, Region hint, out ParsedAddress? address, out ParseDetail detail)
    {
        address = null;
        string text = input.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            detail = ParseDetail.Failure(ReasonCode.Empty, new SourceSpan(0, input.Length));
            return OperationStatus.InvalidData;
        }

        if (!hint.IsUnknown && hint != Region.US && hint != Region.CA)
        {
            detail = ParseDetail.Failure(ReasonCode.AddressUnsupportedCountry, default);
            return OperationStatus.InvalidData;
        }

        OperationStatus status = new Reader(text, hint).Run(out address, out detail);
        if (address is not null)
        {
            address.SourceText = text;
            address.Hint = hint;
        }

        Models.IAddressFieldModel? model = Models.ContactModels.Address;
        if (model is null)
        {
            return status;
        }

        // Rules first: a parse the rules made keeps every field they found; the model only labels the lines the rules keep as written.
        if (status == OperationStatus.Done && address is not null)
        {
            address = ModelAddressReader.Refine(model, text, address, detail.Issues, out detail);
            address.SourceText = text;
            address.Hint = hint;
            return status;
        }

        // Input the rules refused for want of a decision is read from the model's labels.
        ReasonCode reason = detail.PrimaryReason;
        if (reason.Equals(ReasonCode.AddressModelRequired) || reason.Equals(ReasonCode.AddressNoLastLine) || reason.Equals(ReasonCode.AddressNoDeliveryLine))
        {
            if (ModelAddressReader.TryRead(model, text, hint, out ParsedAddress? read, out ParseDetail modelDetail) == OperationStatus.Done)
            {
                address = read;
                address!.SourceText = text;
                address.Hint = hint;
                detail = modelDetail;
                return OperationStatus.Done;
            }
        }

        return status;
    }

    private sealed record Unit(List<AddressToken> Tokens);

    private sealed class LastLine
    {
        internal List<AddressToken> Locality { get; set; } = [];

        internal (int First, int Last)? Admin { get; set; }

        internal string AdminCode { get; set; } = string.Empty;

        internal bool AdminIsCa { get; set; }

        internal (int First, int Last)? Postal { get; set; }

        internal string PostalStandard { get; set; } = string.Empty;

        internal bool PostalIsCa { get; set; }

        internal List<AddressToken> All { get; set; } = [];
    }

    private sealed class Reader(string text, Region hint)
    {
        private readonly List<ParseIssue> _issues = [];

        internal OperationStatus Run(out ParsedAddress? address, out ParseDetail detail)
        {
            address = null;
            List<Unit> units = SplitUnits();
            if (units.Count == 0)
            {
                detail = ParseDetail.Failure(ReasonCode.Empty, new SourceSpan(0, text.Length));
                return OperationStatus.InvalidData;
            }

            // A trailing country name is a field of its own.
            ParsedAddressField? countryName = null;
            Region namedCountry = Region.Unknown;
            List<AddressToken> lastTokens = units[^1].Tokens;
            for (int words = Math.Min(4, lastTokens.Count); words >= 1; words--)
            {
                string key = string.Join(' ', lastTokens.Skip(lastTokens.Count - words).Select(t => t.Key));
                Region r = UsCountryNames.Contains(key) ? Region.US : (CaCountryNames.Contains(key) ? Region.CA : Region.Unknown);
                if (!r.IsUnknown && (words < lastTokens.Count || units.Count > 1))
                {
                    AddressToken first = lastTokens[lastTokens.Count - words];
                    AddressToken last = lastTokens[^1];
                    string written = text.Substring(first.Start, last.Start + last.Length - first.Start);
                    // The last period of an abbreviation such as U.S.A. belongs to the name; the word reader drops periods at the end of a word.
                    if (written.Contains('.', StringComparison.Ordinal) && first.Start + written.Length < text.Length && text[first.Start + written.Length] == '.')
                    {
                        written += ".";
                    }

                    countryName = new ParsedAddressField("country", written, r == Region.US ? "USA" : "CANADA", new SourceSpan(first.Start, written.Length), 1f, DecisionSource.Rule, "country-name");
                    namedCountry = r;
                    lastTokens.RemoveRange(lastTokens.Count - words, words);
                    if (lastTokens.Count == 0)
                    {
                        units.RemoveAt(units.Count - 1);
                    }

                    break;
                }
            }

            if (units.Count == 0)
            {
                detail = ParseDetail.Failure(ReasonCode.AddressNoLastLine, default);
                return OperationStatus.InvalidData;
            }

            // The last line, from the right: postal code, state or province, then the city.
            LastLine? ll = ParseLastLine(units[^1].Tokens, hint);
            if (ll is null && units.Count >= 2)
            {
                List<AddressToken> merged = [.. units[^2].Tokens, .. units[^1].Tokens];
                ll = ParseLastLine(merged, hint);
                if (ll is not null)
                {
                    units.RemoveAt(units.Count - 1);
                    units[^1] = new Unit(merged);
                }
            }

            if (ll is null)
            {
                detail = ParseDetail.Failure(ReasonCode.AddressNoLastLine, new SourceSpan(0, text.Length));
                return OperationStatus.InvalidData;
            }

            // "RIVERVIEW NB, E1B 4E2": the postal code stands alone after a comma and the province ends the segment before it.
            if (ll.Admin is null && ll.Postal is not null && units.Count >= 2)
            {
                List<AddressToken> withProvince = [.. units[^2].Tokens, .. units[^1].Tokens];
                LastLine? joined = ParseLastLine(withProvince, hint);
                if (joined?.Admin is not null && joined.Postal is not null)
                {
                    ll = joined;
                    units.RemoveAt(units.Count - 1);
                }
            }

            units.RemoveAt(units.Count - 1);

            // Country: the content first (a ZIP, a postal code, a state or province, a country name), then the hint.
            Region country;
            float confidence;
            bool inferred = true;
            Region fromContent = ll.PostalIsCa || ll.AdminIsCa ? Region.CA : (ll.Postal is not null || ll.Admin is not null ? Region.US : Region.Unknown);
            if (ll.Postal is not null && ll.Admin is not null)
            {
                confidence = ll.PostalIsCa == ll.AdminIsCa ? 0.99f : 0.7f;
            }
            else
            {
                confidence = ll.Postal is not null ? 0.93f : 0.85f;
            }

            if (!namedCountry.IsUnknown)
            {
                country = namedCountry;
                inferred = false;
                confidence = fromContent.IsUnknown || fromContent == namedCountry ? 1f : 0.8f;
                if (!fromContent.IsUnknown && fromContent != namedCountry)
                {
                    _issues.Add(new ParseIssue(ReasonCode.AddressCountryConflict, countryName!.Value.Span, IssueSeverity.Warning));
                }
            }
            else if (!fromContent.IsUnknown)
            {
                country = fromContent;
                if (!hint.IsUnknown && hint != fromContent)
                {
                    _issues.Add(new ParseIssue(ReasonCode.AddressCountryConflict, default, IssueSeverity.Warning));
                }
            }
            else if (!hint.IsUnknown)
            {
                country = hint;
                inferred = false;
                confidence = 0.9f;
            }
            else
            {
                detail = ParseDetail.Failure(ReasonCode.AddressCountryUndetermined, default);
                return OperationStatus.InvalidData;
            }

            bool canada = country == Region.CA;
            bool pr = !canada && ll.AdminCode == "PR";

            // The city, and the delivery line when it runs on into the city without a comma (123 MAIN ST SPRINGFIELD).
            List<AddressToken> cityTokens = ll.Locality;
            List<AddressToken> runOn = [];

            if (cityTokens.Count == 0 && units.Count > 0 && StreetLineParser.Parse(units[^1].Tokens, canada, pr) is null)
            {
                cityTokens = units[^1].Tokens;
                units.RemoveAt(units.Count - 1);
            }
            else if (cityTokens.Count == 0 && units.Count > 0)
            {
                // "72 Dewey St Richford, Vermont 05476": the state has no city of its own, and words left after the street's last word can only be the city.
                List<AddressToken> lastUnit = units[^1].Tokens;
                int deliveryEnd = StreetLineParser.FindDeliveryEnd(lastUnit, canada);
                if (deliveryEnd > 0 && deliveryEnd < lastUnit.Count && StreetLineParser.Parse(lastUnit.GetRange(0, deliveryEnd), canada, pr) is not null)
                {
                    cityTokens = lastUnit.GetRange(deliveryEnd, lastUnit.Count - deliveryEnd);
                    units[^1] = new Unit(lastUnit.GetRange(0, deliveryEnd));
                }
            }

            // "123 Main St, Suite 4 Springfield VA": a unit designator with its number can only be the end of the delivery line, so it is not part of the city.
            if (units.Count > 0 && cityTokens.Count >= 3 && StreetLineParser.IsUnitDesignator(cityTokens[0].Key, canada, out bool designatorNeedsNumber) && designatorNeedsNumber
                && ((StreetLineParser.IsNumberLike(cityTokens[1].Key) && cityTokens[1].Text.Any(char.IsAsciiDigit)) || (cityTokens[1].Text.Length == 1 && char.IsAsciiLetter(cityTokens[1].Text[0]))))
            {
                List<AddressToken> withUnit = [.. units[^1].Tokens, cityTokens[0], cityTokens[1]];
                if (StreetLineParser.Parse(withUnit, canada, pr) is not null)
                {
                    units[^1] = new Unit(withUnit);
                    cityTokens = cityTokens.GetRange(2, cityTokens.Count - 2);
                }
            }

            if (units.Count == 0 || (cityTokens.Count >= 3 && (StreetLineParser.IsHouseNumber(cityTokens[0].Text, canada) || StartsBox(cityTokens, canada))))
            {
                if (cityTokens.Count > 0)
                {
                    runOn = cityTokens;
                    cityTokens = [];
                }
            }

            List<ParsedAddressText> leading = [];
            if (runOn.Count > 0)
            {
                int k = FirstDeliveryToken(runOn, canada);
                if (k > 0)
                {
                    leading.Add(Lead(runOn.GetRange(0, k)));
                    runOn = runOn.GetRange(k, runOn.Count - k);
                }

                int end = StreetLineParser.FindDeliveryEnd(runOn, canada);
                if (end < 0)
                {
                    List<AddressToken> whole = runOn;
                    if (StreetLineParser.Parse(whole, canada, pr) is not null && ll.Locality.Count == 0)
                    {
                        end = whole.Count;
                    }
                    else
                    {
                        _issues.Add(new ParseIssue(ReasonCode.AddressModelRequired, new SourceSpan(whole[0].Start, whole[^1].Start + whole[^1].Length - whole[0].Start)));
                        detail = new ParseDetail(_issues, []);
                        return OperationStatus.InvalidData;
                    }
                }

                units.Add(new Unit(runOn.GetRange(0, end)));
                cityTokens = runOn.GetRange(end, runOn.Count - end);
            }

            // Delivery lines: scan upward from the line just above the city; everything above the first line the rules cannot read is kept as written.
            List<StreetLine> delivery = [];
            List<AddressToken>? pending = null;
            int u = units.Count - 1;
            for (; u >= 0; u--)
            {
                List<AddressToken> tokens = units[u].Tokens;

                // "Raymond Miller 11505 6th Pl NE": words with no digit and no street word in front of a house number are a recipient or organization the caller ran into the line.
                if (delivery.Count == 0 && pending is null && !pr && tokens.Count >= 3 && !StreetLineParser.IsHouseNumber(tokens[0].Text, canada) && !StreetLineParser.IsUnitDesignator(tokens[0].Key, canada, out _))
                {
                    int k = FirstDeliveryToken(tokens, canada);
                    if (k >= 1 && (k >= 2 || u > 0) && !tokens.Take(k).Any(t => t.Text.Any(char.IsAsciiDigit) || StreetLineParser.IsSuffix(t.Key, canada, out _) || UsCaRules.UsDirectionals.ContainsKey(t.Key)))
                    {
                        List<AddressToken> rest = tokens.GetRange(k, tokens.Count - k);
                        List<StreetLine>? restLines = StreetLineParser.Parse(rest, canada, pr);
                        if (restLines is not null && restLines.Any(l => l.Kind == StreetLineKind.Street && l.Find(StreetPartRole.HouseNumber) is not null))
                        {
                            leading.Add(Lead(tokens.GetRange(0, k)));
                            tokens = rest;
                        }
                    }
                }

                if (pending is not null)
                {
                    List<AddressToken> joined = [.. tokens, .. pending];
                    List<StreetLine>? merged = StreetLineParser.Parse(joined, canada, pr);
                    if (merged is not null && merged.Any(l => l.Kind is StreetLineKind.Street or StreetLineKind.PostOfficeBox))
                    {
                        delivery.InsertRange(0, merged);
                        pending = null;
                        continue;
                    }

                    // The unit did not belong to the line above: it stands on its own line (Publication 28 section 213.3 puts it above the delivery line).
                    delivery.InsertRange(0, StreetLineParser.Parse(pending, canada, pr)!);
                    pending = null;
                }

                // Canada Post section 4.4, French addresses: the civic number may be followed by a comma, then the street type and name.
                if (canada && u > 0 && pending is null && tokens.Count >= 2 && UsCaRules.CaStreetTypesFrench.ContainsKey(tokens[0].Key)
                    && units[u - 1].Tokens.Count == 1 && StreetLineParser.IsHouseNumber(units[u - 1].Tokens[0].Text, canada))
                {
                    List<StreetLine>? civic = StreetLineParser.Parse([units[u - 1].Tokens[0], .. tokens], canada, pr);
                    if (civic is not null && civic.Any(l => l.Kind == StreetLineKind.Street))
                    {
                        delivery.InsertRange(0, civic);
                        u--;
                        continue;
                    }
                }

                // The line just above the city is the delivery line even without a house number (Publication 28 figures show "BAY DR W" alone).
                List<StreetLine>? lines = StreetLineParser.Parse(tokens, canada, pr, allowNoNumber: delivery.Count == 0 && pending is null) ?? ParsePuertoRicoLine(tokens, canada, pr);
                if (lines is null)
                {
                    break;
                }

                if (lines.Count == 1 && lines[0].Kind == StreetLineKind.SecondaryOnly && delivery.Count == 0 && pending is null && units.Count > 1 && u > 0)
                {
                    // A unit below the street line (a line of its own before the city) belongs at the end of the street line.
                    pending = tokens;
                    continue;
                }

                delivery.InsertRange(0, lines);
            }

            if (pending is not null)
            {
                delivery.InsertRange(0, StreetLineParser.Parse(pending, canada, pr)!);
            }

            for (int i = 0; i <= u; i++)
            {
                leading.Insert(Math.Min(leading.Count, i), Lead(units[i].Tokens));
            }

            // Lines above the delivery lines were collected top-down; the run-on prefix (if any) sits after them.
            leading = [.. leading.OrderBy(l => l.Span.Start)];

            if (delivery.Count == 0 || delivery.All(l => l.Kind == StreetLineKind.SecondaryOnly))
            {
                _issues.Add(new ParseIssue(units.Count > 0 || cityTokens.Count > 0 ? ReasonCode.AddressModelRequired : ReasonCode.AddressNoDeliveryLine, default));
                detail = new ParseDetail(_issues, []);
                return OperationStatus.InvalidData;
            }

            address = Build(country, confidence, inferred, leading, delivery, cityTokens, ll, countryName, canada, pr, out detail);
            return OperationStatus.Done;
        }

        private ParsedAddressText Lead(List<AddressToken> tokens)
        {
            int start = tokens[0].Start;
            int end = tokens[^1].Start + tokens[^1].Length;
            return new ParsedAddressText(text.Substring(start, end - start), new SourceSpan(start, end - start), string.Join(' ', tokens.Select(t => t.Key)));
        }

        // Puerto Rico urbanization and building lines have no street suffix or number (Publication 28 sections 292 and 293).
        private static List<StreetLine>? ParsePuertoRicoLine(List<AddressToken> tokens, bool canada, bool pr)
        {
            if (!pr || canada || tokens.Count < 2 || !UsCaRules.PuertoRicoWords.TryGetValue(tokens[0].Key, out (string Standard, string Role) w) || w.Role is not ("urbanization" or "building"))
            {
                return null;
            }

            List<StreetPart> parts = [new StreetPart(StreetPartRole.AdditionalDesignator, tokens[0].Text, w.Standard, tokens[0].Span, "pub28-292")];
            int unit = -1;
            for (int i = 2; i < tokens.Count; i++)
            {
                if (UsCaRules.UsUnits.TryGetValue(tokens[i].Key, out (string Abbreviation, bool NeedsRange) uu) && uu.NeedsRange && i + 1 < tokens.Count)
                {
                    unit = i;
                    break;
                }
            }

            int nameEnd = unit < 0 ? tokens.Count : unit;
            for (int i = 1; i < nameEnd; i++)
            {
                parts.Add(new StreetPart(StreetPartRole.StreetName, tokens[i].Text, tokens[i].Key.Replace('-', ' '), tokens[i].Span, "pub28-292"));
            }

            if (unit >= 0)
            {
                for (int i = unit; i < tokens.Count; i += 2)
                {
                    if (i + 1 >= tokens.Count || !UsCaRules.UsUnits.TryGetValue(tokens[i].Key, out (string Abbreviation, bool NeedsRange) ud))
                    {
                        return null;
                    }

                    parts.Add(new StreetPart(StreetPartRole.UnitDesignator, tokens[i].Text, ud.Abbreviation, tokens[i].Span, "pub28-213.1"));
                    parts.Add(new StreetPart(StreetPartRole.UnitNumber, tokens[i + 1].Text, tokens[i + 1].Key, tokens[i + 1].Span, "pub28-213.1"));
                }
            }

            return [new StreetLine(StreetLineKind.AdditionalAddress, parts)];
        }

        private static bool StartsBox(List<AddressToken> tokens, bool canada)
        {
            string k0 = tokens[0].Key;
            string k1 = tokens.Count > 1 ? tokens[1].Key : string.Empty;
            return StreetLineParser.BoxPhraseEnd(tokens, 0, canada) > 0 || (k0 == "PO" && k1 == "BOX") || k0 == "POBOX" || k0 == "RR" || k0 == "HC" || (k0 == "RURAL" && k1 == "ROUTE") || (canada && k0 is "CP" or "GD");
        }

        // Words in front of the first house number or box word, with no digit among them, are a recipient the caller ran into the address.
        private static int FirstDeliveryToken(List<AddressToken> t, bool canada)
        {
            // PSC 3022 BOX 684 (Publication 28 section 238.1): the unit words belong to the delivery line.
            if (!canada && t.Count >= 4 && t[0].Key is "CPR" or "OPC" or "PSC" or "UPR" or "UNIT" && StreetLineParser.IsNumberLike(t[1].Key) && t[2].Key == "BOX")
            {
                return 0;
            }

            for (int i = 0; i < t.Count; i++)
            {
                if (t[i].Text.Any(char.IsAsciiDigit) && (StreetLineParser.IsHouseNumber(t[i].Text, canada) || StartsBox(t.GetRange(i, t.Count - i), canada)))
                {
                    return i;
                }

                if (StartsBox(t.GetRange(i, t.Count - i), canada))
                {
                    return i;
                }

                if (t[i].Text.Any(char.IsAsciiDigit))
                {
                    return 0;
                }
            }

            return 0;
        }

        private List<Unit> SplitUnits()
        {
            List<(int S, int E)> lines = [];
            int pos = 0;
            while (pos <= text.Length)
            {
                int nl = text.IndexOf('\n', pos);
                int end = nl < 0 ? text.Length : nl;
                int e = end;
                if (e > pos && text[e - 1] == '\r')
                {
                    e--;
                }

                if (text.AsSpan(pos, e - pos).Trim().Length > 0)
                {
                    lines.Add((pos, e));
                }

                if (nl < 0)
                {
                    break;
                }

                pos = nl + 1;
            }

            List<Unit> units = [];
            if (lines.Count == 1)
            {
                // One line: commas, semicolons and bars separate the parts.
                int s = lines[0].S;
                int e = lines[0].E;
                int from = s;
                for (int i = s; i <= e; i++)
                {
                    if (i == e || text[i] is ',' or ';' or '|')
                    {
                        List<AddressToken> tokens = AddressToken.Split(text, from, i);
                        if (tokens.Count > 0)
                        {
                            units.Add(new Unit(tokens));
                        }

                        from = i + 1;
                    }
                }
            }
            else
            {
                foreach ((int s, int e) in lines)
                {
                    List<AddressToken> tokens = AddressToken.Split(text, s, e);
                    if (tokens.Count > 0)
                    {
                        units.Add(new Unit(tokens));
                    }
                }
            }

            return units;
        }

        private ParsedAddress Build(
            Region country,
            float confidence,
            bool inferred,
            List<ParsedAddressText> leading,
            List<StreetLine> delivery,
            List<AddressToken> cityTokens,
            LastLine ll,
            ParsedAddressField? countryName,
            bool canada,
            bool pr,
            out ParseDetail detail)
        {
            List<ParsedAddressField> fields = [];
            foreach (ParsedAddressText l in leading)
            {
                fields.Add(new ParsedAddressField("leading-line", l.Text, l.Standard, l.Span, 1f, DecisionSource.Rule, "line-structure"));
            }

            AddressFacts facts = inferred ? AddressFacts.CountryInferred : AddressFacts.None;
            foreach (StreetLine line in delivery)
            {
                facts |= line.Kind switch
                {
                    StreetLineKind.PostOfficeBox => AddressFacts.PostOfficeBox,
                    StreetLineKind.RuralRoute or StreetLineKind.HighwayContractRoute => AddressFacts.Route,
                    StreetLineKind.GeneralDelivery => AddressFacts.GeneralDelivery,
                    StreetLineKind.Military => AddressFacts.Military,
                    _ => AddressFacts.None,
                };
                int lastName = -1;
                foreach (StreetPart part in line.Parts)
                {
                    if (part.Role == StreetPartRole.UnitDesignator)
                    {
                        facts |= AddressFacts.HasUnit;
                    }

                    SourceSpan partSpan = part.Span;
                    string partText = part.Text;

                    // The abbreviation R.R. is one word whose last period belongs to it; the word reader drops periods at the end of a word.
                    if (part.Role == StreetPartRole.RouteDesignator && partText == "R.R" && partSpan.End < text.Length && text[partSpan.End] == '.')
                    {
                        partSpan = new SourceSpan(partSpan.Start, partSpan.Length + 1);
                        partText += ".";
                    }

                    // The words of a street name are one field: a name such as "Deer Park" or "County Road 12" is a single span of the line.
                    if (part.Role == StreetPartRole.StreetName && lastName >= 0 && fields[lastName].Span.End <= partSpan.Start
                        && text.AsSpan(fields[lastName].Span.End, partSpan.Start - fields[lastName].Span.End).IsWhiteSpace()
                        && text.AsSpan(fields[lastName].Span.End, partSpan.Start - fields[lastName].Span.End).IndexOf('\n') < 0)
                    {
                        ParsedAddressField previous = fields[lastName];
                        int mergedLength = partSpan.End - previous.Span.Start;
                        fields[lastName] = previous with { Text = text.Substring(previous.Span.Start, mergedLength), Standard = previous.Standard + " " + part.Standard, Span = new SourceSpan(previous.Span.Start, mergedLength) };
                        continue;
                    }

                    fields.Add(new ParsedAddressField("street." + RoleName(part.Role), partText, part.Standard, partSpan, 1f, DecisionSource.Rule, part.Rule));
                    lastName = part.Role == StreetPartRole.StreetName ? fields.Count - 1 : -1;
                    if (part.Role == StreetPartRole.Other)
                    {
                        _issues.Add(new ParseIssue(ReasonCode.AddressUnplacedText, part.Span, IssueSeverity.Warning));
                    }
                }
            }

            ParsedAddressField? locality = null;
            if (cityTokens.Count > 0)
            {
                AddressToken a = cityTokens[0];
                AddressToken b = cityTokens[^1];
                string written = text.Substring(a.Start, b.Start + b.Length - a.Start);
                string standard = string.Join(' ', cityTokens.Select(t => canada ? t.Text.ToUpperInvariant() : t.Key));
                locality = new ParsedAddressField("locality", written, standard, new SourceSpan(a.Start, written.Length), 1f, DecisionSource.Rule, canada ? "canada-post-4.4-municipality" : "pub28-221");
                fields.Add(locality.Value);
                if (!canada && cityTokens[^1].Key is "APO" or "FPO" or "DPO")
                {
                    facts |= AddressFacts.Military;
                }
            }

            ParsedAddressField? admin = null;
            if (ll.Admin is { } ad)
            {
                AddressToken a = ll.All[ad.First];
                AddressToken b = ll.All[ad.Last];
                string written = text.Substring(a.Start, b.Start + b.Length - a.Start);
                admin = new ParsedAddressField("admin-area", written, ll.AdminCode, new SourceSpan(a.Start, written.Length), 1f, DecisionSource.Rule, canada ? "canada-post-4.4-province" : "pub28-appendix-b");
                fields.Add(admin.Value);
                string? kind = null;
                if (!canada)
                {
                    UsCaRules.UsStateKinds.TryGetValue(ll.AdminCode, out kind);
                }

                if (kind == "territory")
                {
                    facts |= AddressFacts.Territory;
                }

                if (kind == "military")
                {
                    facts |= AddressFacts.Military;
                }
            }

            ParsedAddressField? postal = null;
            if (ll.Postal is { } pp)
            {
                AddressToken a = ll.All[pp.First];
                AddressToken b = ll.All[pp.Last];
                string written = text.Substring(a.Start, b.Start + b.Length - a.Start);
                postal = new ParsedAddressField("postal-code", written, ll.PostalStandard, new SourceSpan(a.Start, written.Length), 1f, DecisionSource.Rule, ll.PostalIsCa ? "canada-post-6" : "pub28-224");
                fields.Add(postal.Value);
            }

            if (countryName is { } cn)
            {
                fields.Add(cn);
            }

            _ = pr;
            fields = [.. fields.OrderBy(f => f.Span.Start)];
            int start = fields.Min(f => f.Span.Start);
            int end = fields.Max(f => f.Span.End);
            ParsedAddress result = new(country, confidence, inferred, leading, delivery, locality, admin, postal, countryName, fields, facts, new SourceSpan(start, end - start));
            detail = new ParseDetail(_issues, fields.Select(f => new ParsedField(f.Name, f.Span, f.Confidence, f.Source)));
            return result;
        }

        private static string RoleName(StreetPartRole role) => role switch
        {
            StreetPartRole.HouseNumber => "house-number",
            // The 4 of "4-123 Main St" is the unit number, with the hyphen outside it.
            StreetPartRole.UnitPrefix => "unit-number",
            StreetPartRole.Fraction => "fraction",
            StreetPartRole.PreDirectional => "pre-directional",
            StreetPartRole.StreetName => "name",
            StreetPartRole.StreetType => "type",
            StreetPartRole.PostDirectional => "post-directional",
            StreetPartRole.UnitDesignator => "unit-designator",
            StreetPartRole.UnitNumber => "unit-number",
            StreetPartRole.BoxDesignator => "box-designator",
            StreetPartRole.BoxNumber => "box-number",
            StreetPartRole.RouteDesignator => "route-designator",
            StreetPartRole.RouteNumber => "route-number",
            StreetPartRole.GeneralDelivery => "general-delivery",
            StreetPartRole.StationDesignator => "station-designator",
            StreetPartRole.StationName => "station-name",
            StreetPartRole.MilitaryDesignator => "military-designator",
            StreetPartRole.MilitaryNumber => "military-number",
            StreetPartRole.AdditionalDesignator => "additional-designator",
            StreetPartRole.AdditionalNumber => "additional-number",
            _ => "other",
        };
    }

    // ---- the last line ---------------------------------------------------------------------------------------------------------------

    private static LastLine? ParseLastLine(List<AddressToken> tokens, Region hint)
    {
        _ = hint;
        int j = tokens.Count;
        if (j == 0)
        {
            return null;
        }

        LastLine ll = new() { All = tokens };

        // Postal code: Canadian "A1A 1A1" (one or two words) or a ZIP / ZIP+4 (publication 28 section 224; Canada Post section 6).
        if (j >= 2 && IsFsa(tokens[j - 2].Key) && IsLdu(tokens[j - 1].Key))
        {
            ll.Postal = (j - 2, j - 1);
            ll.PostalStandard = tokens[j - 2].Key + " " + tokens[j - 1].Key;
            ll.PostalIsCa = true;
            j -= 2;
        }
        else if (IsCaPostalWhole(tokens[j - 1].Key, out string ca))
        {
            ll.Postal = (j - 1, j - 1);
            ll.PostalStandard = ca;
            ll.PostalIsCa = true;
            j--;
        }
        else if (j >= 2 && tokens[j - 2].Key.Length == 5 && tokens[j - 2].Key.All(char.IsAsciiDigit) && tokens[j - 1].Key.Length == 4 && tokens[j - 1].Key.All(char.IsAsciiDigit))
        {
            ll.Postal = (j - 2, j - 1);
            ll.PostalStandard = tokens[j - 2].Key + "-" + tokens[j - 1].Key;
            j -= 2;
        }
        else if (IsZip(tokens[j - 1].Key, out string zip))
        {
            ll.Postal = (j - 1, j - 1);
            ll.PostalStandard = zip;
            j--;
        }

        bool hasPostal = ll.Postal is not null;

        // State, territory or province: the longest phrase of up to four words that ends here.
        for (int words = Math.Min(4, j); words >= 1; words--)
        {
            string key = string.Join(' ', tokens.Skip(j - words).Take(words).Select(t => t.Key));
            bool upper = tokens.Skip(j - words).Take(words).All(t => !t.Text.Any(char.IsLower));
            bool shortCode = key.Length == 2;
            if (shortCode && !hasPostal && !upper)
            {
                continue;
            }

            string? code = null;
            bool isCa = false;
            if (!(hasPostal && ll.PostalIsCa) && UsCaRules.UsStateNames.TryGetValue(key, out string? us))
            {
                code = us;
            }

            if (code is null && !(hasPostal && !ll.PostalIsCa) && UsCaRules.CaProvinceNames.TryGetValue(key, out string? c))
            {
                code = c;
                isCa = true;
            }

            if (code is not null)
            {
                // A city that is also a state name ("NEW YORK 10001", "WASHINGTON 20019") with nothing before it is the city.
                if (words > 2 && j - words == 0 && !hasPostal)
                {
                    continue;
                }

                ll.Admin = (j - words, j - 1);
                ll.AdminCode = code;
                ll.AdminIsCa = isCa;
                j -= words;
                break;
            }
        }

        if (ll.Admin is null && !hasPostal)
        {
            return null;
        }

        ll.Locality = tokens.GetRange(0, j);
        return ll;
    }

    private static bool IsZip(string key, out string standard)
    {
        standard = string.Empty;
        if (key.Length == 5 && key.All(char.IsAsciiDigit))
        {
            standard = key;
            return true;
        }

        if (key.Length == 10 && key[5] == '-' && key[..5].All(char.IsAsciiDigit) && key[6..].All(char.IsAsciiDigit))
        {
            standard = key;
            return true;
        }

        if (key.Length == 9 && key.All(char.IsAsciiDigit))
        {
            standard = key[..5] + "-" + key[5..];
            return true;
        }

        return false;
    }

    // Canada Post section 6: A1A 1A1, letters D, F, I, O, Q and U are never used, W and Z never start one.
    private static bool IsFsa(string k) => k.Length == 3 && IsCaLetter(k[0], first: true) && char.IsAsciiDigit(k[1]) && IsCaLetter(k[2], first: false);

    private static bool IsLdu(string k) => k.Length == 3 && char.IsAsciiDigit(k[0]) && IsCaLetter(k[1], first: false) && char.IsAsciiDigit(k[2]);

    private static bool IsCaLetter(char c, bool first) =>
        char.IsAsciiLetterUpper(c) && c is not ('D' or 'F' or 'I' or 'O' or 'Q' or 'U') && !(first && c is 'W' or 'Z');

    private static bool IsCaPostalWhole(string k, out string standard)
    {
        standard = string.Empty;
        string s = k.Replace("-", string.Empty, StringComparison.Ordinal);
        if (s.Length == 6 && IsFsa(s[..3]) && IsLdu(s[3..]))
        {
            standard = s[..3] + " " + s[3..];
            return true;
        }

        return false;
    }
}
