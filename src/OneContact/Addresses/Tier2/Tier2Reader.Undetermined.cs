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
            && Evidence(text, Universe(readable.Select(r => (r.Code, r.Address))), readable.ToDictionary(r => r.Code, r => ZoneOnlyDigit(r.Address, r.Code))).Count == 1)
        {
            return false;
        }

        string signature = Signature(readable[0].Address);
        if (readable.Exists(r => Signature(r.Address) != signature))
        {
            return false;
        }

        ParsedAddress first = readable[0].Address;
        address = Undetermined(first, text);
        detail = new ParseDetail([new ParseIssue(ReasonCode.AddressCountryUndetermined, default, IssueSeverity.Warning)], address.Fields.Select(f => new ParsedField(f.Name, f.Span, f.Confidence, f.Source)));
        return true;
    }

    // The fields as the user sees them: the house number, the unit, the road (its name, type and directionals as one stretch), the locality, the admin area, the postal code, the
    // country name and the lines kept as written, each with its offsets. Two readings agree when these are the same.
    private static string Signature(ParsedAddress a)
    {
        Dictionary<string, (int Start, int End)> groups = new(StringComparer.Ordinal);
        List<string> single = [];
        foreach (ParsedAddressField f in a.Fields)
        {
            string group = f.Name switch
            {
                "street.house-number" => "house",
                "street.unit-designator" or "street.unit-number" or "street.additional-designator" or "street.additional-number" => "unit",
                "street.name" or "street.type" or "street.pre-directional" or "street.post-directional" or "street.other" => "road",
                _ => string.Empty,
            };
            if (group.Length == 0)
            {
                single.Add(f.Name + ":" + f.Span.Start + "-" + f.Span.End);
            }
            else
            {
                groups[group] = groups.TryGetValue(group, out (int Start, int End) g) ? (Math.Min(g.Start, f.Span.Start), Math.Max(g.End, f.Span.End)) : (f.Span.Start, f.Span.End);
            }
        }

        single.AddRange(groups.Select(g => g.Key + ":" + g.Value.Start + "-" + g.Value.End));
        single.Sort(StringComparer.Ordinal);
        return string.Join('|', single);
    }

    // The same fields with the country removed: the standardized form of each field is the text as written, because no authority's style applies.
    private static ParsedAddress Undetermined(ParsedAddress read, string text)
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
            read.Facts | AddressFacts.CountryUndetermined,
            read.Span)
        {
            SourceText = text,
            Hint = Region.Unknown,
        };
        return result;
    }
}
