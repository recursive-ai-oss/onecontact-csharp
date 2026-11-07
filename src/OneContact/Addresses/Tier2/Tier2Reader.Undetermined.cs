using RecursiveAI.OneContact.Addresses.Parsing;

namespace RecursiveAI.OneContact.Addresses.Tier2;

// An address whose country cannot be told but whose fields can. Postcode forms are shared (four digits in Belgium, Austria, Switzerland and others; five in France,
// Germany, Spain and others), and the text may hold nothing that names one country, yet every country that can read it may find the same fields in it. Then the
// fields are right whichever country it is, and only the country is missing. Nothing is guessed: the country stays unknown, the address says so, and when the
// countries that can read the text disagree on a field the text is refused as before.
internal static partial class Tier2Reader
{
    /// <summary>
    /// Reads text for which no country could be decided, when every country that can read it finds the same fields.
    /// </summary>
    /// <param name="text">The input.</param>
    /// <param name="address">The address, with <see cref="Region.Unknown"/> as its country and <see cref="AddressFacts.CountryUndetermined"/> set.</param>
    /// <param name="detail">The per-field provenance, with a <see cref="ReasonCode.AddressCountryUndetermined"/> warning.</param>
    internal static bool TryReadUndetermined(string text, out ParsedAddress? address, out ParseDetail detail)
    {
        address = null;
        detail = ParseDetail.Failure(ReasonCode.AddressCountryUndetermined, default);
        if (text.Length > MaxInferenceLength || !text.AsSpan().ContainsAnyInRange('0', '9'))
        {
            return false;
        }

        List<(string Code, ParsedAddress Address, ParseDetail Detail)> readable = ReadCandidates(text);

        // A candidate that cannot be written in the letters of the text is not a candidate, and the shared Swedish/Finnish five-digit form is not Swedish.
        readable.RemoveAll(r => !LettersHold(text, r.Code));
        readable.RemoveAll(r => r.Code == "SE" && r.Address.PostalCode is { } p && !p.Text.Contains(' ', StringComparison.Ordinal));
        HashSet<string> taken = [.. readable.Where(r => r.Address.AdminArea is not null).Select(r => r.Address.AdminArea!.Value.Text)];
        readable.RemoveAll(r => r.Address.AdminArea is null && LastWord(r.Address.Locality) is { } word && word.Length == 2 && taken.Contains(word));
        if (readable.Count == 0)
        {
            return false;
        }

        // Evidence for exactly one country that the country-deciding path turned down (a letter the country does not use, say) is a conflict, not an absence: the refusal stays.
        if (HasAnyEvidence(text)
            && Evidence(text, Universe(readable.Select(r => (r.Code, r.Address)), text), readable.ToDictionary(r => r.Code, r => ZoneOnlyDigit(r.Address, r.Code))).Count == 1)
        {
            return false;
        }

        // The parts every candidate finds alike are returned. When the candidates agree on all of them the read is whole; when they disagree on some (a house number set apart from
        // a flat in one country and joined in another), those parts are left out and the read is partial. Without the postal code among the agreed parts there is nothing to anchor
        // the read on, and the text is refused as before.
        List<Dictionary<string, List<ParsedAddressField>>> elements = [.. readable.Select(r => Elements(r.Address))];
        HashSet<string> agreed = [.. elements[0].Keys];
        foreach (Dictionary<string, List<ParsedAddressField>> e in elements)
        {
            agreed.IntersectWith(e.Keys);
        }

        bool whole = elements.TrueForAll(e => e.Count == agreed.Count);
        if (!whole && !agreed.Any(k => k.StartsWith("postal-code:", StringComparison.Ordinal)))
        {
            return false;
        }

        ParsedAddress first = readable[0].Address;
        address = whole ? Undetermined(first, text) : Undetermined(Narrow(first, elements[0], agreed), text, disputed: true);
        List<ParseIssue> issues = [new ParseIssue(ReasonCode.AddressCountryUndetermined, default, IssueSeverity.Warning)];
        if (!whole)
        {
            issues.Add(new ParseIssue(ReasonCode.AddressFieldsDisputed, default, IssueSeverity.Warning));
        }

        detail = new ParseDetail(issues, address.Fields.Select(f => new ParsedField(f.Name, f.Span, f.Confidence, f.Source)));
        return true;
    }

    // The fields as the user sees them, each with its offsets: the house number; the unit parts; the road (its name, type and directionals as one stretch); the locality, admin area,
    // postal code, country name and the lines kept as written. Two readings agree on a part when they have the same element, keyed by its name and offsets.
    private static Dictionary<string, List<ParsedAddressField>> Elements(ParsedAddress a)
    {
        Dictionary<string, (int Start, int End)> groups = new(StringComparer.Ordinal);
        List<(string Group, ParsedAddressField Field)> members = [];
        foreach (ParsedAddressField f in a.Fields)
        {
            // A settlement line read as a dependent locality in one country is a line kept as written in another: for agreement between candidates it is still part of the road.
            string group = f.Rule == SettlementLineRule ? "road" : f.Name switch
            {
                "street.house-number" => "house",
                "street.unit-designator" or "street.unit-number" or "street.additional-designator" or "street.additional-number" => "unit",
                "street.name" or "street.type" or "street.pre-directional" or "street.post-directional" or "street.other" => "road",
                _ => f.Name,
            };
            members.Add((group, f));
            groups[group] = groups.TryGetValue(group, out (int Start, int End) g) ? (Math.Min(g.Start, f.Span.Start), Math.Max(g.End, f.Span.End)) : (f.Span.Start, f.Span.End);
        }

        Dictionary<string, List<ParsedAddressField>> map = new(StringComparer.Ordinal);
        foreach ((string group, ParsedAddressField f) in members)
        {
            // A field that stands alone is keyed by its own offsets (two lines kept as written are two elements); a part of a group by the group's.
            string key = group == f.Name ? group + ":" + f.Span.Start + "-" + f.Span.End : group + ":" + groups[group].Start + "-" + groups[group].End;
            if (!map.TryGetValue(key, out List<ParsedAddressField>? list))
            {
                list = [];
                map[key] = list;
            }

            list.Add(f);
        }

        return map;
    }

    // The reading with only the agreed elements left: their fields, and the street lines without the parts of the left-out elements.
    private static ParsedAddress Narrow(ParsedAddress read, Dictionary<string, List<ParsedAddressField>> elements, HashSet<string> agreed)
    {
        HashSet<SourceSpan> kept = [.. elements.Where(e => agreed.Contains(e.Key)).SelectMany(e => e.Value).Select(f => f.Span)];
        ParsedAddressField? Keep(ParsedAddressField? f) => f is { } x && kept.Contains(x.Span) ? x : null;
        List<ParsedAddressField> fields = [.. read.Fields.Where(f => kept.Contains(f.Span))];
        List<Streets.StreetLine> delivery = [.. read.DeliveryLines.Select(l => new Streets.StreetLine(l.Kind, [.. l.Parts.Where(p => kept.Contains(p.Span))])).Where(l => l.Parts.Count > 0)];
        return new ParsedAddress(
            read.Country,
            read.CountryConfidence,
            read.CountryInferred,
            read.LeadingLines.Where(l => kept.Contains(l.Span)).ToList(),
            delivery,
            Keep(read.Locality),
            Keep(read.AdminArea),
            Keep(read.PostalCode),
            Keep(read.CountryName),
            fields,
            read.Facts,
            read.Span);
    }

    /// <summary>The same reading with the country removed: another reader's fields (the model's), with the country left unknown and the text as written as their standardized form.</summary>
    internal static ParsedAddress AsUndetermined(ParsedAddress read, string text) => Undetermined(read, text);

    // The same fields with the country removed: the standardized form of each field is the text as written, because no authority's style applies.
    private static ParsedAddress Undetermined(ParsedAddress read, string text, bool disputed = false)
    {
        ParsedAddressField As(ParsedAddressField f) => f with { Standard = f.Text };
        List<ParsedAddressField> fields = [.. read.Fields.Select(As)];
        ParsedAddress result = new(
            Region.Unknown,
            0f,
            false,
            read.LeadingLines,
            read.DeliveryLines,
            read.Locality is { } l ? As(l) : null,
            read.AdminArea is { } a ? As(a) : null,
            read.PostalCode is { } p ? As(p) : null,
            read.CountryName is { } c ? As(c) : null,
            fields,
            (read.Facts & ~AddressFacts.CountryInferred) | AddressFacts.CountryUndetermined | (disputed ? AddressFacts.FieldsDisputed : AddressFacts.None),
            read.Span)
        {
            SourceText = text,
            Hint = Region.Unknown,
        };
        return result;
    }
}
