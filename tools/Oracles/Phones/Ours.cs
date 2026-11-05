using System.Buffers;
using System.Globalization;
using System.Text;
using RecursiveAI.OneContact.Phones;

namespace RecursiveAI.OneContact.Oracles.Phones;

/// <summary>Answers the generated cases with OneContact, in the same result format <c>Driver.java</c> writes.</summary>
internal static class Ours
{
    private static readonly string[] OutOfCountry = ["US", "GB", "DE", "AU", "JP", "BR", "MX", "RU", "NZ", "AR", "CA", "KR"];
    private static readonly string[] MobileFrom = ["US", "BR", "MX", "CL", "UZ", "GB", "CA", "CN"];

    // The jar's enum order.
    private static readonly (string Name, PhoneNumberType Type)[] Types =
    [
        ("FIXED_LINE", PhoneNumberType.FixedLine), ("MOBILE", PhoneNumberType.Mobile), ("FIXED_LINE_OR_MOBILE", PhoneNumberType.FixedLineOrMobile),
        ("TOLL_FREE", PhoneNumberType.TollFree), ("PREMIUM_RATE", PhoneNumberType.PremiumRate), ("SHARED_COST", PhoneNumberType.SharedCost),
        ("VOIP", PhoneNumberType.Voip), ("PERSONAL_NUMBER", PhoneNumberType.PersonalNumber), ("PAGER", PhoneNumberType.Pager),
        ("UAN", PhoneNumberType.Uan), ("VOICEMAIL", PhoneNumberType.Voicemail), ("UNKNOWN", PhoneNumberType.Unknown),
    ];

    private static string TypeName(PhoneNumberType t) => Types.First(x => x.Type == t).Name;

    private static string ReasonName(PhonePossibility p) => p switch
    {
        PhonePossibility.IsPossible => "IS_POSSIBLE",
        PhonePossibility.IsPossibleLocalOnly => "IS_POSSIBLE_LOCAL_ONLY",
        PhonePossibility.InvalidCountryCode => "INVALID_COUNTRY_CODE",
        PhonePossibility.TooShort => "TOO_SHORT",
        PhonePossibility.TooLong => "TOO_LONG",
        _ => "INVALID_LENGTH",
    };

    private static string SourceName(CountryCodeSource s) => s switch
    {
        CountryCodeSource.FromNumberWithPlusSign => "FROM_NUMBER_WITH_PLUS_SIGN",
        CountryCodeSource.FromNumberWithIdd => "FROM_NUMBER_WITH_IDD",
        CountryCodeSource.FromNumberWithoutPlusSign => "FROM_NUMBER_WITHOUT_PLUS_SIGN",
        CountryCodeSource.FromDefaultCountry => "FROM_DEFAULT_COUNTRY",
        _ => "UNSPECIFIED",
    };

    private static string ErrorName(ReasonCode r)
    {
        if (r == ReasonCode.PhoneTooLong)
        {
            return "TOO_LONG";
        }

        if (r == ReasonCode.PhoneTooShort)
        {
            return "TOO_SHORT_NSN";
        }

        if (r == ReasonCode.PhoneTooShortAfterIdd)
        {
            return "TOO_SHORT_AFTER_IDD";
        }

        if (r == ReasonCode.PhoneInvalidCountryCode || r == ReasonCode.PhoneRegionRequired || r == ReasonCode.PhoneUnknownRegion)
        {
            return "INVALID_COUNTRY_CODE";
        }

        return "NOT_A_NUMBER";
    }

    private static string Inv(long v) => v.ToString(CultureInfo.InvariantCulture);

    internal static string Parts(PhoneNumber n, bool raw)
    {
        StringBuilder sb = new();
        sb.Append("cc=").Append(Inv(n.CountryCode));
        sb.Append(" nn=").Append(n.NationalNumber.ToString(CultureInfo.InvariantCulture));
        sb.Append(" ilz=").Append(n.ItalianLeadingZero ? 1 : 0);
        sb.Append(" nlz=").Append(Inv(n.LeadingZeroCount));
        sb.Append(" ext=").Append(n.Extension is null ? "-" : Escape.Encode(n.Extension));
        if (raw)
        {
            sb.Append(" src=").Append(SourceName(n.CountryCodeSource));
            sb.Append(" carrier=").Append(n.PreferredDomesticCarrierCode is null ? "-" : Escape.Encode(n.PreferredDomesticCarrierCode));
        }

        return sb.ToString();
    }

    private static string E164(PhoneNumber? n) =>
        n is not { } v ? "-" : v.Format(PhoneFormat.E164) + (v.Extension is null ? "" : ";ext=" + Escape.Encode(v.Extension));

    private static void Emit(TextWriter w, string id, string op, Func<string> body)
    {
        string value;
        try
        {
            value = body();
        }
        catch (ContactParseException e)
        {
            value = "ERR:" + ErrorName(e.Reason);
        }
#pragma warning disable CA1031 // The harness reports any failure of the code under test as a result instead of stopping.
        catch (Exception e)
#pragma warning restore CA1031
        {
            value = "EXC:" + e.GetType().Name;
        }

        w.Write(id);
        w.Write('\t');
        w.Write(op);
        w.Write('\t');
        w.Write(value);
        w.Write('\n');
    }

    internal static int Run(string casesPath, string outPath)
    {
        PhoneNumberParser p = PhoneNumberParser.Default;
        using StreamWriter w = new(outPath, false, new UTF8Encoding(false));
        int count = 0;
        foreach (string line in File.ReadLines(casesPath))
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] f = line.Split('\t');
            switch (f[1])
            {
                case "P":
                    PhoneCase(w, p, f[0], f[2], Escape.Decode(f[3]));
                    break;
                case "R":
                    RegionCase(w, p, f[0], Region.Parse(f[2]));
                    break;
                default:
                    NonGeoCase(w, p, f[0], int.Parse(f[3], CultureInfo.InvariantCulture));
                    break;
            }

            count++;
        }

        return count;
    }

    private static Region? RegionOf(string text) => text == "-" ? null : Region.Parse(text);

    private static void PhoneCase(TextWriter w, PhoneNumberParser p, string id, string regionText, string text)
    {
        Region? reg = RegionOf(regionText);
        Region defaultRegion = reg ?? Region.Unknown;
        Emit(w, id, "parsePlain", () =>
        {
            Check(p.TryParse(text, defaultRegion, false, out PhoneNumber plain, out ParseDetail d), d);
            return Parts(plain, false);
        });

        PhoneNumber n = default;
        bool ok = true;
        Emit(w, id, "parse", () =>
        {
            OperationStatus s = p.TryParse(text, defaultRegion, true, out n, out ParseDetail d);
            if (s != OperationStatus.Done)
            {
                ok = false;
            }

            Check(s, d);
            return Parts(n, true);
        });
        Emit(w, id, "alpha", () => Bool(PhoneNumberParser.IsVanityNumber(text)));
        if (!ok)
        {
            return;
        }

        string own = p.GetRegion(n).IsUnknown ? "" : p.GetRegion(n).Code;
        Emit(w, id, "valid", () => Bool(p.IsValid(n)));
        Emit(w, id, "validFor", () => reg is null ? "-" : Bool(p.IsValidForRegion(n, reg.Value)));
        Emit(w, id, "region", () => own.Length == 0 ? NonGeoOrNone(p, n) : own);
        Emit(w, id, "type", () => TypeName(p.GetNumberType(n)));
        Emit(w, id, "possible", () => Bool(p.IsPossible(n)));
        Emit(w, id, "reason", () => ReasonName(p.GetPossibility(n)));
        Emit(w, id, "reasonByType", () =>
        {
            StringBuilder sb = new();
            foreach ((string name, PhoneNumberType t) in Types)
            {
                PhonePossibility r = p.GetPossibility(n, t);
                sb.Append(name).Append('=').Append(ReasonName(r)).Append(' ');
                sb.Append(r is PhonePossibility.IsPossible or PhonePossibility.IsPossibleLocalOnly ? 'T' : 'F').Append(';');
            }

            return sb.ToString();
        });
        Emit(w, id, "e164", () => Escape.Encode(p.Format(n, PhoneFormat.E164)));
        Emit(w, id, "intl", () => Escape.Encode(p.Format(n, PhoneFormat.International)));
        Emit(w, id, "national", () => Escape.Encode(p.Format(n, PhoneFormat.National)));
        Emit(w, id, "rfc3966", () => Escape.Encode(p.Format(n, PhoneFormat.Rfc3966)));
        Emit(w, id, "ooc", () =>
        {
            StringBuilder sb = new();
            foreach (string f in FromList(reg, own, OutOfCountry))
            {
                sb.Append(f).Append('=').Append(Escape.Encode(p.FormatOutOfCountry(n, Region.Parse(f)))).Append('|');
            }

            return sb.ToString();
        });
        Emit(w, id, "mobile", () =>
        {
            StringBuilder sb = new();
            foreach (string f in FromList(reg, own, MobileFrom))
            {
                Region from = Region.Parse(f);
                sb.Append(f).Append('=').Append(Escape.Encode(p.FormatForMobileDialing(n, from, true)));
                sb.Append('/').Append(Escape.Encode(p.FormatForMobileDialing(n, from, false))).Append('|');
            }

            return sb.ToString();
        });
        Emit(w, id, "original", () =>
        {
            StringBuilder sb = new();
            List<string> from = [];
            if (reg is not null)
            {
                from.Add(regionText);
            }

            from.AddRange(["US", "GB", "DE"]);
            foreach (string f in from)
            {
                sb.Append(f).Append('=').Append(Escape.Encode(p.FormatInOriginalFormat(n, Region.Parse(f)))).Append('|');
            }

            return sb.ToString();
        });
        Emit(w, id, "carrier", () => Escape.Encode(p.FormatNationalWithCarrierCode(n, "99")) + "|" + Escape.Encode(p.FormatNationalWithPreferredCarrierCode(n, "FB")));
        Emit(w, id, "intlDial", () => Bool(p.CanBeInternationallyDialled(n)));
        Emit(w, id, "geo", () => Bool(p.IsGeographical(n)));
        Emit(w, id, "acl", () => Inv(p.GetAreaCodeLength(n)));
        Emit(w, id, "ndc", () => Inv(p.GetNationalDestinationCodeLength(n)));
        Emit(w, id, "nsn", () => n.NationalSignificantNumber);
    }

    private static string NonGeoOrNone(PhoneNumberParser p, PhoneNumber n) =>
        p.IsNonGeographic(n) ? "001" : "-";

    private static List<string> FromList(Region? reg, string own, string[] fixedList)
    {
        List<string> from = [];
        if (reg is not null)
        {
            from.Add(reg.Value.Code);
        }

        if (own.Length > 0)
        {
            from.Add(own);
        }

        from.AddRange(fixedList);
        return from;
    }

    private static string Bool(bool b) => b ? "true" : "false";

    private static void Check(OperationStatus status, ParseDetail detail)
    {
        if (status != OperationStatus.Done)
        {
            throw new ContactParseException(ContactKind.Phone, detail.PrimaryReason, default, "parse failed");
        }
    }

    private static void RegionCase(TextWriter w, PhoneNumberParser p, string id, Region region)
    {
        Emit(w, id, "cc", () => Inv(p.Metadata.GetCallingCode(region)));
        Emit(w, id, "ndd", () => Escape.Encode(p.GetNationalPrefix(region, true)) + "|" + Escape.Encode(p.GetNationalPrefix(region, false)));
        Emit(w, id, "nanpa", () => Bool(p.IsNorthAmericanPlan(region)));
        Emit(w, id, "codes", () => string.Join(',', p.Metadata.GetRegions(p.Metadata.GetCallingCode(region)).Select(r => r.Code)));
        Emit(w, id, "exampleRegion", () => E164(p.GetExample(region, PhoneNumberType.FixedLine)));
        foreach ((string name, PhoneNumberType t) in Types)
        {
            Emit(w, id, "example:" + name, () => E164(p.GetExample(region, t)));
        }

        Emit(w, id, "supportedTypes", () =>
        {
            List<string> l = [.. p.GetSupportedTypes(region).Select(TypeName)];
            l.Sort(StringComparer.Ordinal);
            return string.Join(',', l);
        });
    }

    private static void NonGeoCase(TextWriter w, PhoneNumberParser p, string id, int cc)
    {
        Emit(w, id, "exampleNonGeo", () => E164(p.GetNonGeographicExample(cc)));
        Emit(w, id, "codesNonGeo", () => p.Metadata.GetRegions(cc).Count > 0 ? string.Join(',', p.Metadata.GetRegions(cc).Select(r => r.Code)) : "001");
    }
}
