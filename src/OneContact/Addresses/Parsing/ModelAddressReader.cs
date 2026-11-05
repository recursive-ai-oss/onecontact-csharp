using System.Buffers;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Addresses.Streets;
using RecursiveAI.OneContact.Models;

namespace RecursiveAI.OneContact.Addresses.Parsing;

/// <summary>
/// Reads an address from the fields a model labelled, for the input the rules refuse, and fills the recipient and organization lines the rules
/// keep only as written. It runs after the rules: a field a rule found is never replaced, and every field it makes says <see cref="DecisionSource.Model"/>
/// with the model's confidence.
/// </summary>
/// <remarks>
/// The model labels words; this class turns labels into the same <see cref="ParsedAddress"/> the rules make. The standard form of a model field is the
/// authority's table entry when the table has one (a state, a province, a street type, a directional, a unit designator) and the upper-cased words otherwise.
/// Nothing is invented: a field the model did not label is absent.
/// </remarks>
internal static class ModelAddressReader
{
    private const string Rule = "model";

    /// <summary>Reads an address the rules refused.</summary>
    internal static OperationStatus TryRead(IAddressFieldModel model, string text, Region hint, out ParsedAddress? address, out ParseDetail detail)
    {
        address = null;
        IReadOnlyList<ModelSpan> spans = model.Tag(text);
        ModelSpan? locality = null, admin = null, postal = null, country = null;
        List<ModelSpan> street = [];
        List<ModelSpan> leading = [];
        foreach (ModelSpan s in spans)
        {
            switch (s.Label)
            {
                case "locality" when locality is null: locality = s; break;
                case "admin-area" when admin is null: admin = s; break;
                case "postal-code" when postal is null: postal = s; break;
                case "country" when country is null: country = s; break;
                case "recipient" or "organization": leading.Add(s); break;
                default:
                    if (s.Label.StartsWith("street.", StringComparison.Ordinal))
                    {
                        street.Add(s);
                    }

                    break;
            }
        }

        if (street.Count == 0 && locality is null)
        {
            detail = ParseDetail.Failure(ReasonCode.AddressNoDeliveryLine, default);
            return OperationStatus.InvalidData;
        }

        string adminCode = string.Empty;
        bool adminIsCa = false;
        if (admin is { } a)
        {
            string key = Key(text, a);
            if (UsCaRules.CaProvinceNames.TryGetValue(key, out string? caCode))
            {
                adminCode = caCode;
                adminIsCa = true;
            }
            else if (UsCaRules.UsStateNames.TryGetValue(key, out string? usCode))
            {
                adminCode = usCode;
            }
            else
            {
                adminCode = key;
            }
        }

        string postalStandard = string.Empty;
        bool postalIsCa = false;
        if (postal is { } p)
        {
            postalStandard = Key(text, p);
            string compact = postalStandard.Replace(" ", string.Empty, StringComparison.Ordinal);
            if (compact.Length == 6 && char.IsAsciiLetter(compact[0]) && char.IsAsciiDigit(compact[1]) && char.IsAsciiLetter(compact[2]) && char.IsAsciiDigit(compact[3]) && char.IsAsciiLetter(compact[4]) && char.IsAsciiDigit(compact[5]))
            {
                postalIsCa = true;
                postalStandard = compact[..3] + " " + compact[3..];
            }
            else if (compact.Length == 9 && compact.Take(5).All(char.IsAsciiDigit) && compact.Skip(5).All(char.IsAsciiDigit))
            {
                postalStandard = compact[..5] + "-" + compact[5..];
            }
        }

        // The country: a written country name, then what the state or province and the postal code say, then the hint.
        Region fromContent = adminIsCa || postalIsCa ? Region.CA : (admin is not null || postal is not null ? Region.US : Region.Unknown);
        Region named = Region.Unknown;
        if (country is { } c)
        {
            string key = Key(text, c);
            named = RuleAddressParser.UsCountryNames.Contains(key) ? Region.US : (RuleAddressParser.CaCountryNames.Contains(key) ? Region.CA : Region.Unknown);
        }

        Region region;
        bool inferred = true;
        float confidence = admin is not null && postal is not null ? 0.95f : 0.85f;
        List<ParseIssue> issues = [];
        if (!named.IsUnknown)
        {
            region = named;
            inferred = false;
            confidence = 1f;
        }
        else if (!fromContent.IsUnknown)
        {
            region = fromContent;
            if (!hint.IsUnknown && hint != fromContent)
            {
                issues.Add(new ParseIssue(ReasonCode.AddressCountryConflict, default, IssueSeverity.Warning));
            }
        }
        else if (!hint.IsUnknown)
        {
            region = hint;
            inferred = false;
            confidence = 0.9f;
        }
        else
        {
            detail = ParseDetail.Failure(ReasonCode.AddressCountryUndetermined, default);
            return OperationStatus.InvalidData;
        }

        bool canada = region == Region.CA;
        List<ParsedAddressField> fields = [];
        AddressFacts facts = inferred ? AddressFacts.CountryInferred : AddressFacts.None;

        List<ParsedAddressText> leadingLines = [];
        foreach (ModelSpan s in leading)
        {
            string written = text.Substring(s.Start, s.Length);
            leadingLines.Add(new ParsedAddressText(written, new SourceSpan(s.Start, s.Length), PostalLine(written)));
            fields.Add(new ParsedAddressField(s.Label, written, PostalLine(written), new SourceSpan(s.Start, s.Length), s.Confidence, DecisionSource.Model, Rule));
        }

        List<StreetPart> parts = [];
        StreetLineKind kind = StreetLineKind.Street;
        foreach (ModelSpan s in street)
        {
            string name = s.Label["street.".Length..];
            StreetPartRole role = RoleOf(name);
            string written = text.Substring(s.Start, s.Length);
            string standard = StandardOf(role, written, canada);
            parts.Add(new StreetPart(role, written, standard, new SourceSpan(s.Start, s.Length), Rule));
            fields.Add(new ParsedAddressField(s.Label, written, standard, new SourceSpan(s.Start, s.Length), s.Confidence, DecisionSource.Model, Rule));
            switch (role)
            {
                case StreetPartRole.UnitDesignator or StreetPartRole.UnitNumber:
                    facts |= AddressFacts.HasUnit;
                    break;
                case StreetPartRole.BoxDesignator or StreetPartRole.BoxNumber:
                    kind = StreetLineKind.PostOfficeBox;
                    facts |= AddressFacts.PostOfficeBox;
                    break;
                case StreetPartRole.RouteDesignator or StreetPartRole.RouteNumber:
                    kind = StreetLineKind.RuralRoute;
                    facts |= AddressFacts.Route;
                    break;
                case StreetPartRole.GeneralDelivery:
                    kind = StreetLineKind.GeneralDelivery;
                    facts |= AddressFacts.GeneralDelivery;
                    break;
                case StreetPartRole.MilitaryDesignator or StreetPartRole.MilitaryNumber:
                    kind = StreetLineKind.Military;
                    facts |= AddressFacts.Military;
                    break;
            }
        }

        List<StreetLine> delivery = parts.Count > 0 ? [new StreetLine(kind, parts)] : [];

        ParsedAddressField? localityField = null;
        if (locality is { } l)
        {
            string written = text.Substring(l.Start, l.Length);
            string standard = canada ? written.ToUpperInvariant() : Key(text, l);
            localityField = new ParsedAddressField("locality", written, standard, new SourceSpan(l.Start, l.Length), l.Confidence, DecisionSource.Model, Rule);
            fields.Add(localityField.Value);
        }

        ParsedAddressField? adminField = null;
        if (admin is { } ad)
        {
            string written = text.Substring(ad.Start, ad.Length);
            adminField = new ParsedAddressField("admin-area", written, adminCode, new SourceSpan(ad.Start, ad.Length), ad.Confidence, DecisionSource.Model, Rule);
            fields.Add(adminField.Value);
            if (!canada && UsCaRules.UsStateKinds.TryGetValue(adminCode, out string? stateKind))
            {
                facts |= stateKind == "territory" ? AddressFacts.Territory : (stateKind == "military" ? AddressFacts.Military : AddressFacts.None);
            }
        }

        ParsedAddressField? postalField = null;
        if (postal is { } po)
        {
            string written = text.Substring(po.Start, po.Length);
            postalField = new ParsedAddressField("postal-code", written, postalStandard, new SourceSpan(po.Start, po.Length), po.Confidence, DecisionSource.Model, Rule);
            fields.Add(postalField.Value);
        }

        ParsedAddressField? countryField = null;
        if (country is { } co)
        {
            string written = text.Substring(co.Start, co.Length);
            countryField = new ParsedAddressField("country", written, named == Region.CA ? "CANADA" : (named == Region.US ? "USA" : written.ToUpperInvariant()), new SourceSpan(co.Start, co.Length), co.Confidence, DecisionSource.Model, Rule);
            fields.Add(countryField.Value);
        }

        if (fields.Count == 0)
        {
            detail = ParseDetail.Failure(ReasonCode.AddressNoDeliveryLine, default);
            return OperationStatus.InvalidData;
        }

        fields = [.. fields.OrderBy(f => f.Span.Start)];
        int start = fields[0].Span.Start;
        int end = fields.Max(f => f.Span.End);
        address = new ParsedAddress(region, confidence, inferred, leadingLines, delivery, localityField, adminField, postalField, countryField, fields, facts, new SourceSpan(start, end - start));
        detail = new ParseDetail(issues, fields.Select(f => new ParsedField(f.Name, f.Span, f.Confidence, f.Source)));
        return OperationStatus.Done;
    }

    /// <summary>
    /// Gives the lines above the delivery lines their model labels (recipient, organization) where the rules kept them only as written. A leading line
    /// the model gave no label stays as it was. Fields the rules found are not touched.
    /// </summary>
    internal static ParsedAddress Refine(IAddressFieldModel model, string text, ParsedAddress rules, IReadOnlyList<ParseIssue> issues, out ParseDetail detail)
    {
        if (rules.LeadingLines.Count == 0)
        {
            detail = new ParseDetail(issues, rules.Fields.Select(f => new ParsedField(f.Name, f.Span, f.Confidence, f.Source)));
            return rules;
        }

        IReadOnlyList<ModelSpan> spans = model.Tag(text);
        List<ParsedAddressField> fields = [];
        foreach (ParsedAddressField f in rules.Fields)
        {
            if (f.Name != "leading-line")
            {
                fields.Add(f);
                continue;
            }

            bool replaced = false;
            foreach (ModelSpan s in spans)
            {
                if ((s.Label is "recipient" or "organization") && s.Start >= f.Span.Start && s.End <= f.Span.End)
                {
                    string written = text.Substring(s.Start, s.Length);
                    fields.Add(new ParsedAddressField(s.Label, written, PostalLine(written), new SourceSpan(s.Start, s.Length), s.Confidence, DecisionSource.Model, Rule));
                    replaced = true;
                }
            }

            if (!replaced)
            {
                fields.Add(f);
            }
        }

        fields = [.. fields.OrderBy(f => f.Span.Start)];
        detail = new ParseDetail(issues, fields.Select(f => new ParsedField(f.Name, f.Span, f.Confidence, f.Source)));
        return new ParsedAddress(rules.Country, rules.CountryConfidence, rules.CountryInferred, rules.LeadingLines, rules.DeliveryLines, rules.Locality, rules.AdminArea, rules.PostalCode, rules.CountryName, fields, rules.Facts, rules.Span);
    }

    private static string Key(string text, ModelSpan s) =>
        text.AsSpan(s.Start, s.Length).ToString().Replace(".", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    // USPS Publication 28 section 222: the postal style is upper case with the periods and commas left out.
    private static string PostalLine(string written) =>
        written.Replace(".", string.Empty, StringComparison.Ordinal).Replace(",", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    private static string StandardOf(StreetPartRole role, string written, bool canada)
    {
        string key = written.Replace(".", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        switch (role)
        {
            case StreetPartRole.StreetType when !canada && UsCaRules.UsSuffixes.TryGetValue(key, out (string Standard, string Primary) suffix):
                return suffix.Standard;
            case StreetPartRole.PreDirectional or StreetPartRole.PostDirectional:
                if (!canada && UsCaRules.UsDirectionals.TryGetValue(key, out (string Abbreviation, string Full) directional))
                {
                    return directional.Abbreviation;
                }

                return canada && UsCaRules.CaDirections.TryGetValue(key, out string? direction) ? direction : key;
            case StreetPartRole.UnitDesignator:
                if (!canada && UsCaRules.UsUnits.TryGetValue(key, out (string Abbreviation, bool NeedsRange) unit))
                {
                    return unit.Abbreviation;
                }

                return canada && UsCaRules.CaUnits.TryGetValue(key, out string? caUnit) ? caUnit : key;
            default:
                return key;
        }
    }

    private static StreetPartRole RoleOf(string name) => name switch
    {
        "house-number" => StreetPartRole.HouseNumber,
        "fraction" => StreetPartRole.Fraction,
        "pre-directional" => StreetPartRole.PreDirectional,
        "name" => StreetPartRole.StreetName,
        "type" => StreetPartRole.StreetType,
        "post-directional" => StreetPartRole.PostDirectional,
        "unit-designator" => StreetPartRole.UnitDesignator,
        "unit-number" => StreetPartRole.UnitNumber,
        "box-designator" => StreetPartRole.BoxDesignator,
        "box-number" => StreetPartRole.BoxNumber,
        "route-designator" => StreetPartRole.RouteDesignator,
        "route-number" => StreetPartRole.RouteNumber,
        "general-delivery" => StreetPartRole.GeneralDelivery,
        "station-designator" => StreetPartRole.StationDesignator,
        "station-name" => StreetPartRole.StationName,
        "military-designator" => StreetPartRole.MilitaryDesignator,
        "military-number" => StreetPartRole.MilitaryNumber,
        "additional-designator" => StreetPartRole.AdditionalDesignator,
        "additional-number" => StreetPartRole.AdditionalNumber,
        _ => StreetPartRole.Other,
    };
}
