using System.Buffers;
using RecursiveAI.OneContact.Addresses.Parsing;
using RecursiveAI.OneContact.Addresses.Streets;
using RecursiveAI.OneContact.Models;

namespace RecursiveAI.OneContact.Addresses.Tier2;

/// <summary>
/// Reads an address of a Tier 2 country from the fields a model labelled, for the input the Tier 2 rules refuse (<c>address.model-required</c>,
/// <c>address.no-last-line</c>, <c>address.no-delivery-line</c>). A rule never gives way to the model: this runs only after the rules have refused.
/// </summary>
/// <remarks>
/// The model labels words; this turns the labels into the same <see cref="ParsedAddress"/> the rules make, with every field saying
/// <see cref="DecisionSource.Model"/> and the model's confidence. Nothing is invented: a field the model did not label is absent, the written words are
/// only upper-cased (or reduced to La Poste's line form where that authority asks for it) and never rewritten, and the country is the hint, the country
/// the rules had already decided from the postcode, or none, in which case the read is refused as <c>address.country-undetermined</c>.
/// </remarks>
internal static partial class Tier2Reader
{
    private const string ModelRule = "model";

    /// <summary>Reads the address with the model's labels.</summary>
    /// <param name="model">The address model.</param>
    /// <param name="text">The input.</param>
    /// <param name="hint">The caller's country hint; used when it is a Tier 2 country.</param>
    /// <param name="decided">The country the rules had already decided before they refused, or <see langword="null"/>.</param>
    /// <param name="address">The address when the status is <see cref="OperationStatus.Done"/>.</param>
    /// <param name="detail">The issues and the per-field provenance.</param>
    internal static OperationStatus TryReadModel(IAddressFieldModel model, string text, Region hint, T2Country? decided, out ParsedAddress? address, out ParseDetail detail)
    {
        address = null;
        T2Country? country = decided;
        bool inferred = true;
        float confidence = 0.85f;
        if (T2Rules.Handles(hint))
        {
            country = T2Rules.Countries[hint.Code];
            inferred = false;
            confidence = 0.9f;
        }

        if (country is null)
        {
            detail = ParseDetail.Failure(ReasonCode.AddressCountryUndetermined, default);
            return OperationStatus.InvalidData;
        }

        IReadOnlyList<ModelSpan> spans = model.Tag(text);
        List<ModelSpan> leading = [];
        List<ModelSpan> street = [];
        List<ModelSpan> dependent = [];
        ModelSpan? locality = null, admin = null, postal = null, countrySpan = null;
        foreach (ModelSpan s in spans)
        {
            switch (s.Label)
            {
                case "recipient" or "organization": leading.Add(s); break;
                case "dependent-locality": dependent.Add(s); break;
                case "locality" when locality is null: locality = s; break;
                case "admin-area" when admin is null: admin = s; break;
                case "postal-code" when postal is null: postal = s; break;
                case "country" when countrySpan is null: countrySpan = s; break;
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

        List<ParsedAddressField> fields = [];
        AddressFacts facts = inferred ? AddressFacts.CountryInferred : AddressFacts.None;
        List<ParsedAddressText> leadingLines = [];
        List<(string Postal, string Display)> leadingLayout = [];
        foreach (ModelSpan s in leading)
        {
            string written = text.Substring(s.Start, s.Length);
            leadingLines.Add(new ParsedAddressText(written, new SourceSpan(s.Start, s.Length), written));
            leadingLayout.Add((written, written));
            fields.Add(new ParsedAddressField(s.Label, written, written, new SourceSpan(s.Start, s.Length), s.Confidence, DecisionSource.Model, ModelRule));
        }

        // Street parts, one delivery line per input line.
        List<StreetLine> delivery = [];
        List<(string Postal, string Display)> deliveryLayout = [];
        int lineStart = 0;
        while (lineStart < street.Count)
        {
            int lineEnd = lineStart + 1;
            while (lineEnd < street.Count && text.AsSpan(street[lineEnd - 1].End, street[lineEnd].Start - street[lineEnd - 1].End).IndexOf('\n') < 0)
            {
                lineEnd++;
            }

            List<StreetPart> parts = [];
            StreetLineKind kind = StreetLineKind.Street;
            for (int i = lineStart; i < lineEnd; i++)
            {
                ModelSpan s = street[i];
                StreetPartRole role = ModelAddressReader.RoleOf(s.Label["street.".Length..]);
                string written = text.Substring(s.Start, s.Length);
                string standard = written.ToUpperInvariant();
                parts.Add(new StreetPart(role, written, standard, new SourceSpan(s.Start, s.Length), ModelRule));
                fields.Add(new ParsedAddressField(s.Label, written, standard, new SourceSpan(s.Start, s.Length), s.Confidence, DecisionSource.Model, ModelRule));
                switch (role)
                {
                    case StreetPartRole.UnitDesignator or StreetPartRole.UnitNumber:
                        facts |= AddressFacts.HasUnit;
                        break;
                    case StreetPartRole.BoxDesignator or StreetPartRole.BoxNumber:
                        kind = StreetLineKind.PostOfficeBox;
                        facts |= AddressFacts.PostOfficeBox;
                        break;
                    case StreetPartRole.GeneralDelivery:
                        kind = StreetLineKind.GeneralDelivery;
                        facts |= AddressFacts.GeneralDelivery;
                        break;
                }
            }

            delivery.Add(new StreetLine(kind, parts));
            string line = text.Substring(street[lineStart].Start, street[lineEnd - 1].End - street[lineStart].Start);
            line = string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            deliveryLayout.Add((country.Afnor ? T2Text.Afnor(line) : line.ToUpperInvariant(), T2Text.Title(line, capitalsKept: true)));
            lineStart = lineEnd;
        }

        // A settlement or sector line between the street and the postcode is a line of its own, as the registers print it.
        foreach (ModelSpan s in dependent)
        {
            string written = text.Substring(s.Start, s.Length);
            fields.Add(new ParsedAddressField("dependent-locality", written, written.ToUpperInvariant(), new SourceSpan(s.Start, s.Length), s.Confidence, DecisionSource.Model, ModelRule));
            deliveryLayout.Add((country.Afnor ? T2Text.Afnor(written) : written.ToUpperInvariant(), T2Text.Title(written, capitalsKept: true)));
        }

        ParsedAddressField? localityField = null;
        string localityWritten = string.Empty;
        string localityPostal = string.Empty;
        string localityDisplay = string.Empty;
        if (locality is { } l)
        {
            localityWritten = text.Substring(l.Start, l.Length);
            localityPostal = country.Afnor ? T2Text.SaintAbbreviated(T2Text.Afnor(localityWritten)) : country.Profile == "lt" || country.LocalityAsWritten ? localityWritten : localityWritten.ToUpperInvariant();
            localityDisplay = T2Text.Title(localityWritten, capitalsKept: true);
            localityField = new ParsedAddressField("locality", localityWritten, localityPostal, new SourceSpan(l.Start, l.Length), l.Confidence, DecisionSource.Model, ModelRule);
            fields.Add(localityField.Value);
        }

        ParsedAddressField? adminField = null;
        string adminInline = string.Empty;
        string adminDisplay = string.Empty;
        if (admin is { } a)
        {
            string written = text.Substring(a.Start, a.Length);
            adminInline = written.ToUpperInvariant();
            adminDisplay = T2Text.Title(written, capitalsKept: true);
            adminField = new ParsedAddressField("admin-area", written, adminInline, new SourceSpan(a.Start, a.Length), a.Confidence, DecisionSource.Model, ModelRule);
            fields.Add(adminField.Value);
        }

        ParsedAddressField? postalField = null;
        string printed = string.Empty;
        if (postal is { } p)
        {
            string written = text.Substring(p.Start, p.Length);
            string standard = string.Join(' ', written.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (country.PrintedPrefix.Length > 0 && standard.StartsWith(country.PrintedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                standard = standard[country.PrintedPrefix.Length..];
            }

            standard = standard.ToUpperInvariant();
            printed = country.PrintedPrefix + standard;
            postalField = new ParsedAddressField("postal-code", written, standard, new SourceSpan(p.Start, p.Length), p.Confidence, DecisionSource.Model, ModelRule);
            fields.Add(postalField.Value);
        }

        ParsedAddressField? countryField = null;
        if (countrySpan is { } c)
        {
            string written = text.Substring(c.Start, c.Length);
            countryField = new ParsedAddressField("country", written, country.CountryLine, new SourceSpan(c.Start, c.Length), c.Confidence, DecisionSource.Model, ModelRule);
            fields.Add(countryField.Value);
            inferred = false;
            confidence = Math.Max(confidence, 0.9f);
        }

        if (fields.Count == 0)
        {
            detail = ParseDetail.Failure(ReasonCode.AddressNoDeliveryLine, default);
            return OperationStatus.InvalidData;
        }

        // The last line, the way the authority prints it (the same joins the rules use), from the fields the model gave.
        string lastPostal;
        string lastDisplay;
        if (localityField is null)
        {
            lastPostal = lastDisplay = printed;
        }
        else if (postalField is null)
        {
            lastPostal = localityPostal + (adminInline.Length > 0 ? " " + adminInline : string.Empty);
            lastDisplay = localityDisplay + (adminInline.Length > 0 ? " " + adminDisplay : string.Empty);
        }
        else if (country.IsCentral && Reader.CentralLast(country, printed, localityPostal, localityDisplay, adminInline, adminDisplay, out string centralPostal, out string centralDisplay))
        {
            lastPostal = centralPostal;
            lastDisplay = centralDisplay;
        }
        else if (country.PostcodeLast)
        {
            lastPostal = localityPostal + (adminInline.Length > 0 ? "\n" + adminInline : string.Empty) + "\n" + printed;
            lastDisplay = localityDisplay + (adminInline.Length > 0 ? "\n" + adminDisplay : string.Empty) + "\n" + printed;
        }
        else
        {
            lastPostal = printed + new string(' ', country.LocalitySpaces) + localityPostal + (adminInline.Length > 0 ? " " + adminInline : string.Empty);
            lastDisplay = printed + " " + localityDisplay + (adminInline.Length > 0 ? " " + adminDisplay : string.Empty);
        }

        fields = [.. fields.OrderBy(f => f.Span.Start)];
        int start = fields[0].Span.Start;
        int end = fields.Max(f => f.Span.End);
        address = new ParsedAddress(Region.Parse(country.Code), confidence, inferred, leadingLines, delivery, localityField, adminField, postalField, countryField, fields, facts, new SourceSpan(start, end - start))
        {
            Tier2 = new T2Layout(country, leadingLayout, deliveryLayout, (lastPostal, lastDisplay))
            {
                LocalityLine = country.Profile == "hu" && localityField is not null ? (localityPostal, localityDisplay) : null,
                LocalityAfter = leadingLayout.Count + deliveryLayout.Count,
            },
            SourceText = text,
            Hint = hint,
        };
        detail = new ParseDetail([], fields.Select(f => new ParsedField(f.Name, f.Span, f.Confidence, f.Source)));
        return OperationStatus.Done;
    }
}
