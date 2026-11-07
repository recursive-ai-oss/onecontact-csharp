using System.Globalization;
using System.Text;
using RecursiveAI.OneContact.Phones.Metadata;

namespace RecursiveAI.OneContact.Phones;

/// <summary>Builders for the text forms of a number that do not depend on metadata.</summary>
internal static class PhoneFormatter
{
    /// <summary>The canonical form: E.164, followed by <c>;ext=</c> and the extension when there is one (RFC 3966 writes the extension that way).</summary>
    internal static string Canonical(int countryCode, ulong nationalNumber, int leadingZeros, string? extension)
    {
        string cc = countryCode.ToString(CultureInfo.InvariantCulture);
        string nn = nationalNumber.ToString(CultureInfo.InvariantCulture);
        string ext = string.IsNullOrEmpty(extension) ? "" : ";ext=" + extension;
        return string.Create(1 + cc.Length + leadingZeros + nn.Length + ext.Length, (cc, leadingZeros, nn, ext), static (span, parts) =>
        {
            span[0] = '+';
            int at = 1;
            parts.cc.CopyTo(span[at..]);
            at += parts.cc.Length;
            span.Slice(at, parts.leadingZeros).Fill('0');
            at += parts.leadingZeros;
            parts.nn.CopyTo(span[at..]);
            at += parts.nn.Length;
            parts.ext.CopyTo(span[at..]);
        });
    }
}

public sealed partial class PhoneNumberParser
{
    /// <summary>Writes a number in one of the standard formats.</summary>
    /// <param name="number">The number.</param>
    /// <param name="format">The format.</param>
    /// <returns>The text. A number whose calling code is unknown is written as its national significant number, except in E.164.</returns>
    /// <remarks>
    /// <see cref="PhoneFormat.E164"/> never includes the extension. The other formats append it using the region's preferred
    /// extension prefix (or <c> ext. </c>; RFC 3966 uses <c>;ext=</c>). A number with no digits that was parsed with its raw input kept
    /// is written as that raw input; in E.164 the calling code is put in front when the number was read in its default region.
    /// </remarks>
    public string Format(PhoneNumber number, PhoneFormat format)
    {
        int cc = number.CountryCode;
        string nsn = GetNsn(number);

        // A number with no digits (such as "00") that kept its raw input is shown as that input. In E.164 the calling code goes in front
        // only when the number was read in its default region; a text that carried a plus sign or dialling prefix is left as written.
        if (number.NationalNumber == 0 && !string.IsNullOrEmpty(number.RawInput))
        {
            if (format == PhoneFormat.E164 && number.CountryCodeSource == CountryCodeSource.FromDefaultCountry)
            {
                return "+" + cc.ToString(CultureInfo.InvariantCulture) + number.RawInput;
            }

            return number.RawInput;
        }

        if (format == PhoneFormat.E164)
        {
            return "+" + cc.ToString(CultureInfo.InvariantCulture) + nsn;
        }

        RegionMetadata[]? regions = _metadata.GetByCallingCode(cc);
        if (regions is null)
        {
            return nsn;
        }

        // Formatting data for regions that share a calling code lives with the main region.
        RegionMetadata md = regions[0];
        StringBuilder sb = new(FormatNsn(nsn, md, format, null));
        AppendExtension(sb, number, md, format);
        return PrefixWithCountryCode(cc, format, sb.ToString());
    }

    /// <summary>Writes a number as it should be dialled from another country.</summary>
    /// <param name="number">The number.</param>
    /// <param name="callingFrom">The region the call is placed from.</param>
    /// <returns>
    /// The national format when the call stays in the number's calling code area (with the calling code in front inside the North American
    /// plan), otherwise the international format behind the caller's international dialling prefix when it has a single one. A
    /// region with no data gives the international format.
    /// </returns>
    public string FormatOutOfCountry(PhoneNumber number, Region callingFrom)
    {
        RegionMetadata? from = _metadata.GetRegionMetadata(callingFrom);
        if (from is null)
        {
            return Format(number, PhoneFormat.International);
        }

        int cc = number.CountryCode;
        string nsn = GetNsn(number);
        RegionMetadata[]? regions = _metadata.GetByCallingCode(cc);
        if (regions is null)
        {
            return nsn;
        }

        if (cc == 1)
        {
            if (from.CountryCode == 1)
            {
                // Inside the North American plan the national format is dialled, behind the calling code.
                return "1 " + Format(number, PhoneFormat.National);
            }
        }
        else if (cc == from.CountryCode)
        {
            return Format(number, PhoneFormat.National);
        }

        // A preferred prefix wins; otherwise a single plain prefix is used; a region with several prefixes gives the international form.
        string intlPrefix = from.InternationalPrefix ?? "";
        string prefixForFormatting = "";
        if (!string.IsNullOrEmpty(from.PreferredInternationalPrefix))
        {
            prefixForFormatting = from.PreferredInternationalPrefix;
        }
        else if (PhoneText.UniqueInternationalPrefix.IsMatch(intlPrefix))
        {
            prefixForFormatting = intlPrefix;
        }

        RegionMetadata md = regions[0];
        StringBuilder sb = new(FormatNsn(nsn, md, PhoneFormat.International, null));
        AppendExtension(sb, number, md, PhoneFormat.International);
        if (prefixForFormatting.Length > 0)
        {
            return prefixForFormatting + " " + cc.ToString(CultureInfo.InvariantCulture) + " " + sb;
        }

        return PrefixWithCountryCode(cc, PhoneFormat.International, sb.ToString());
    }

    /// <summary>Writes a number the way a mobile phone should be given it to dial from a region.</summary>
    /// <param name="number">The number.</param>
    /// <param name="callingFrom">The region the call is placed from.</param>
    /// <param name="withFormatting">Whether to keep the punctuation; otherwise only digits and <c>+</c>, <c>*</c>, <c>#</c> remain.</param>
    /// <returns>The text; empty when the number cannot be dialled that way (for example a Brazilian fixed line or mobile number from inside Brazil with no carrier code).</returns>
    public string FormatForMobileDialing(PhoneNumber number, Region callingFrom, bool withFormatting)
    {
        int cc = number.CountryCode;
        RegionMetadata[]? regions = _metadata.GetByCallingCode(cc);
        if (regions is null)
        {
            return number.RawInput ?? "";
        }

        // The extension cannot be dialled together with the number.
        PhoneNumber noExtension = number.WithoutExtension();
        string regionId = regions[0].Id;
        PhoneNumberType type = GetNumberType(noExtension);
        bool isValid = type != PhoneNumberType.Unknown;
        string formatted = "";
        if (string.Equals(callingFrom.Code, regionId, StringComparison.Ordinal))
        {
            bool fixedOrMobile = type is PhoneNumberType.FixedLine or PhoneNumberType.Mobile or PhoneNumberType.FixedLineOrMobile;
            if (regionId == "BR" && fixedOrMobile)
            {
                // These numbers need a carrier code when dialled inside Brazil; without one nothing useful can be given.
                formatted = !string.IsNullOrEmpty(noExtension.PreferredDomesticCarrierCode)
                    ? FormatNationalWithPreferredCarrierCode(noExtension, "")
                    : "";
            }
            else if (cc == 1)
            {
                // North American numbers that can be dialled abroad are given in international form, except those that might be short numbers.
                RegionMetadata? fromMd = _metadata.GetRegionMetadata(callingFrom);
                if (CanBeInternationallyDialled(noExtension) && fromMd is not null
                    && TestNumberLength(GetNsn(noExtension), fromMd, PhoneNumberType.Unknown) != PhonePossibility.TooShort)
                {
                    formatted = Format(noExtension, PhoneFormat.International);
                }
                else
                {
                    formatted = Format(noExtension, PhoneFormat.National);
                }
            }
            else if ((regionId == RegionMetadata.NonGeographicId || (regionId is "MX" or "CL" or "UZ" && fixedOrMobile)) && CanBeInternationallyDialled(noExtension))
            {
                formatted = Format(noExtension, PhoneFormat.International);
            }
            else
            {
                formatted = Format(noExtension, PhoneFormat.National);
            }
        }
        else if (isValid && CanBeInternationallyDialled(noExtension))
        {
            // Short numbers are assumed not to be dialled from abroad, so only a full valid number takes this route.
            return withFormatting ? Format(noExtension, PhoneFormat.International) : Format(noExtension, PhoneFormat.E164);
        }

        return withFormatting ? formatted : PhoneText.NormalizeDiallableCharsOnly(formatted);
    }

    /// <summary>Writes a number in the format it was typed in, as far as that can be told from the raw input kept at parse time.</summary>
    /// <param name="number">The number, parsed with its raw input kept (any parse from a <see cref="string"/>).</param>
    /// <param name="callingFrom">The region the number is being shown in, used when the number was typed with an international dialling prefix.</param>
    /// <returns>
    /// The raw input when formatting would add, remove or change a digit, or when the region has no formatting pattern for the number;
    /// otherwise the number formatted the way it was typed: international, with a dialling prefix, or national with or without the national prefix.
    /// </returns>
    public string FormatInOriginalFormat(PhoneNumber number, Region callingFrom)
    {
        string raw = number.RawInput ?? "";
        if (number.RawInput is not null && !HasFormattingPattern(number))
        {
            return raw;
        }

        if (number.CountryCodeSource == CountryCodeSource.Unspecified)
        {
            return Format(number, PhoneFormat.National);
        }

        string? formatted;
        switch (number.CountryCodeSource)
        {
            case CountryCodeSource.FromNumberWithPlusSign:
                formatted = Format(number, PhoneFormat.International);
                break;
            case CountryCodeSource.FromNumberWithIdd:
                formatted = FormatOutOfCountry(number, callingFrom);
                break;
            case CountryCodeSource.FromNumberWithoutPlusSign:
                formatted = Format(number, PhoneFormat.International)[1..];
                break;
            default:
                formatted = FormatNationalAsTyped(number, raw);
                break;
        }

        if (formatted is not null && raw.Length > 0)
        {
            // If formatting inserted, removed or changed a digit, the raw input is the safer answer.
            if (!string.Equals(PhoneText.NormalizeDiallableCharsOnly(formatted), PhoneText.NormalizeDiallableCharsOnly(raw), StringComparison.Ordinal))
            {
                formatted = raw;
            }
        }

        return formatted ?? "";
    }

    private string FormatNationalAsTyped(PhoneNumber number, string raw)
    {
        RegionMetadata[]? regions = _metadata.GetByCallingCode(number.CountryCode);
        string nationalFormat = Format(number, PhoneFormat.National);
        if (regions is null)
        {
            return nationalFormat;
        }

        RegionMetadata md = regions[0];
        string prefix = (md.NationalPrefix ?? "").Replace("~", "", StringComparison.Ordinal);
        if (md.Id == RegionMetadata.NonGeographicId || prefix.Length == 0)
        {
            // No national prefix at all: the national format cannot have added one.
            return nationalFormat;
        }

        if (RawInputContainsNationalPrefix(raw, prefix, md.Id))
        {
            return nationalFormat;
        }

        string nsn = GetNsn(number);
        NumberFormatRule? rule = ChooseFormattingRule(md.NumberFormats, nsn);
        if (rule is null)
        {
            return nationalFormat;
        }

        // The national prefix formatting rule is written like "0$1"; the prefix is whatever comes before the first group.
        string candidate = rule.NationalPrefixFormattingRule ?? "";
        int firstGroup = candidate.IndexOf("$1", StringComparison.Ordinal);
        if (firstGroup <= 0)
        {
            return nationalFormat;
        }

        if (PhoneText.NormalizeDigitsOnly(candidate.AsSpan(0, firstGroup)).Length == 0)
        {
            return nationalFormat;
        }

        // The prefix would be shown but the text did not have one: format without it.
        NumberFormatRule withoutPrefix = rule.WithoutNationalPrefixRule();
        StringBuilder sb = new(FormatNsnUsingRule(nsn, withoutPrefix, PhoneFormat.National, null));
        AppendExtension(sb, number, md, PhoneFormat.National);
        return sb.ToString();
    }

    private bool RawInputContainsNationalPrefix(string raw, string nationalPrefix, string regionId)
    {
        string digits = PhoneText.NormalizeDigitsOnly(raw);
        if (!digits.StartsWith(nationalPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        // Some numbers (Japanese 00777123) look as if they carry the prefix when they do not, so check the rest is a valid number.
        if (!Region.TryParse(regionId, out Region region))
        {
            return false;
        }

        return TryParse(digits.AsSpan(nationalPrefix.Length), region, false, out PhoneNumber rest, out _) == System.Buffers.OperationStatus.Done
            && IsValid(rest);
    }

    private bool HasFormattingPattern(PhoneNumber number)
    {
        RegionMetadata[]? regions = _metadata.GetByCallingCode(number.CountryCode);
        return regions is not null && ChooseFormattingRule(regions[0].NumberFormats, GetNsn(number)) is not null;
    }

    /// <summary>Writes a number in national format with a carrier selection code in the position the region's rule puts it.</summary>
    /// <param name="number">The number.</param>
    /// <param name="carrierCode">The carrier code.</param>
    /// <returns>The text; where the region has no carrier-code rule this is the plain national format.</returns>
    public string FormatNationalWithCarrierCode(PhoneNumber number, string carrierCode)
    {
        ArgumentNullException.ThrowIfNull(carrierCode);
        string nsn = GetNsn(number);
        RegionMetadata[]? regions = _metadata.GetByCallingCode(number.CountryCode);
        if (regions is null)
        {
            return nsn;
        }

        RegionMetadata md = regions[0];
        StringBuilder sb = new(FormatNsn(nsn, md, PhoneFormat.National, carrierCode));
        AppendExtension(sb, number, md, PhoneFormat.National);
        return sb.ToString();
    }

    /// <summary>Writes a number in national format with the carrier code kept from parsing, or <paramref name="fallbackCarrierCode"/> when there is none.</summary>
    /// <param name="number">The number.</param>
    /// <param name="fallbackCarrierCode">The carrier code to use when the number has none.</param>
    /// <returns>The text.</returns>
    public string FormatNationalWithPreferredCarrierCode(PhoneNumber number, string fallbackCarrierCode)
    {
        ArgumentNullException.ThrowIfNull(fallbackCarrierCode);
        string? carrier = number.PreferredDomesticCarrierCode;
        return FormatNationalWithCarrierCode(number, carrier ?? fallbackCarrierCode);
    }

    private static string PrefixWithCountryCode(int countryCode, PhoneFormat format, string formatted)
    {
        string cc = countryCode.ToString(CultureInfo.InvariantCulture);
        return format switch
        {
            PhoneFormat.E164 => "+" + cc + formatted,
            PhoneFormat.International => "+" + cc + " " + formatted,
            PhoneFormat.Rfc3966 => "tel:+" + cc + "-" + formatted,
            _ => formatted,
        };
    }

    private static void AppendExtension(StringBuilder sb, PhoneNumber number, RegionMetadata md, PhoneFormat format)
    {
        string? ext = number.Extension;
        if (string.IsNullOrEmpty(ext))
        {
            return;
        }

        if (format == PhoneFormat.Rfc3966)
        {
            sb.Append(";ext=").Append(ext);
        }
        else if (!string.IsNullOrEmpty(md.PreferredExtnPrefix))
        {
            sb.Append(md.PreferredExtnPrefix).Append(ext);
        }
        else
        {
            sb.Append(" ext. ").Append(ext);
        }
    }

    internal static NumberFormatRule? ChooseFormattingRule(NumberFormatRule[] rules, string nsn)
    {
        foreach (NumberFormatRule rule in rules)
        {
            // Only the last leading-digits pattern is used: it is the most specific one.
            int n = rule.LeadingDigits.Length;
            if ((n == 0 || rule.LeadingDigits[n - 1].Get().LookingAt(nsn, out _)) && rule.Pattern.Get().IsMatch(nsn))
            {
                return rule;
            }
        }

        return null;
    }

    private static string FormatNsn(string nsn, RegionMetadata md, PhoneFormat format, string? carrierCode)
    {
        NumberFormatRule[] available = md.IntlNumberFormats.Length == 0 || format == PhoneFormat.National ? md.NumberFormats : md.IntlNumberFormats;
        NumberFormatRule? rule = ChooseFormattingRule(available, nsn);
        return rule is null ? nsn : FormatNsnUsingRule(nsn, rule, format, carrierCode);
    }

    internal static string FormatNsnUsingRule(string nsn, NumberFormatRule rule, PhoneFormat format, string? carrierCode)
    {
        PhoneRegex pattern = rule.Pattern.Get();
        // Bounds: at most 2 * (32 + 1) integers.
        Span<int> groups = stackalloc int[2 * (pattern.GroupCount + 1)];
        if (!pattern.Match(nsn, groups))
        {
            return nsn;
        }

        string template = rule.Format;
        if (format == PhoneFormat.National && !string.IsNullOrEmpty(carrierCode) && !string.IsNullOrEmpty(rule.CarrierCodeFormattingRule))
        {
            template = ReplaceFirstGroupToken(template, rule.CarrierCodeFormattingRule.Replace("$CC", carrierCode, StringComparison.Ordinal));
        }
        else if (format == PhoneFormat.National && !string.IsNullOrEmpty(rule.NationalPrefixFormattingRule))
        {
            template = ReplaceFirstGroupToken(template, rule.NationalPrefixFormattingRule);
        }

        StringBuilder sb = new();
        ExpandReplacement(template, nsn, groups, pattern.GroupCount, sb);
        string result = sb.ToString();
        if (format == PhoneFormat.Rfc3966)
        {
            PhoneRegex separator = PhoneText.Separator;
            if (separator.LookingAt(result, out int lead))
            {
                result = result[lead..];
            }

            // Every remaining run of separators becomes a dash.
            StringBuilder dashed = new();
            int i = 0;
            while (i < result.Length)
            {
                if (separator.LookingAt(result.AsSpan(i), out int run) && run > 0)
                {
                    dashed.Append('-');
                    i += run;
                }
                else
                {
                    dashed.Append(result[i]);
                    i++;
                }
            }

            result = dashed.ToString();
        }

        return result;
    }

    // The rule says where the first group goes ("0$1", "($1)"); the format's first "$n" token is replaced by the rule with its own "$1" naming that token.
    private static string ReplaceFirstGroupToken(string format, string rule)
    {
        for (int i = 0; i + 1 < format.Length; i++)
        {
            if (format[i] == '$' && char.IsAsciiDigit(format[i + 1]))
            {
                string token = format.Substring(i, 2);
                string inserted = rule.Replace("$1", token, StringComparison.Ordinal);
                return format[..i] + inserted + format[(i + 2)..];
            }
        }

        return format;
    }
}
