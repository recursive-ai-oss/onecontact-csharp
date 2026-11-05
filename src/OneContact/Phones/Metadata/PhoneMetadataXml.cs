using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace RecursiveAI.OneContact.Phones.Metadata;

/// <summary>
/// Reads the phone metadata XML files (the main numbering-plan file, the short-number file and the alternate-formats file share one shape:
/// <c>phoneNumberMetadata/territories/territory</c>) into <see cref="TerritoryData"/>. The document type declaration is skipped, never
/// processed, so no entity can expand. The reader is bounded: it refuses documents over <see cref="MaxCharacters"/> characters or with
/// more than <see cref="MaxTerritories"/> territories, and any malformed value raises <see cref="InvalidDataException"/>.
/// </summary>
internal static class PhoneMetadataXml
{
    internal const long MaxCharacters = 32L * 1024 * 1024;
    internal const int MaxTerritories = 4096;
    private const int MaxFormatsPerTerritory = 1024;
    private const int MaxPatternLength = 1 << 16;
    private const int MaxLengthValue = 250;

    internal static List<TerritoryData> Read(Stream xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = MaxCharacters,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };

        XDocument document;
        try
        {
            using XmlReader reader = XmlReader.Create(xml, settings);
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException e)
        {
            throw new InvalidDataException("The phone metadata is not well-formed XML: " + e.Message, e);
        }

        XElement? root = document.Root;
        if (root is null || root.Name.LocalName != "phoneNumberMetadata")
        {
            throw new InvalidDataException("The phone metadata must have a phoneNumberMetadata root element.");
        }

        XElement? territories = root.Element("territories");
        if (territories is null)
        {
            throw new InvalidDataException("The phone metadata has no territories element.");
        }

        List<TerritoryData> result = [];
        foreach (XElement element in territories.Elements("territory"))
        {
            if (result.Count >= MaxTerritories)
            {
                throw new InvalidDataException("The phone metadata has too many territories.");
            }

            result.Add(ReadTerritory(element));
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException("The phone metadata has no territories.");
        }

        return result;
    }

    private static TerritoryData ReadTerritory(XElement element)
    {
        TerritoryData t = new()
        {
            Id = Attr(element, "id") ?? "",
            CountryCode = ParseInt(Attr(element, "countryCode") ?? "0", "countryCode", 0, 999),
            MainCountryForCode = Flag(element, "mainCountryForCode"),
            LeadingDigits = Squash(Attr(element, "leadingDigits")),
            InternationalPrefix = Squash(Attr(element, "internationalPrefix")),
            PreferredInternationalPrefix = Attr(element, "preferredInternationalPrefix"),
            NationalPrefix = Attr(element, "nationalPrefix"),
            NationalPrefixForParsing = Squash(Attr(element, "nationalPrefixForParsing")),
            NationalPrefixTransformRule = Attr(element, "nationalPrefixTransformRule"),
            PreferredExtnPrefix = Attr(element, "preferredExtnPrefix"),
            NationalPrefixFormattingRule = Attr(element, "nationalPrefixFormattingRule"),
            NationalPrefixOptionalWhenFormatting = Flag(element, "nationalPrefixOptionalWhenFormatting"),
            CarrierCodeFormattingRule = Attr(element, "carrierCodeFormattingRule"),
            MobileNumberPortableRegion = Flag(element, "mobileNumberPortableRegion"),
        };

        foreach (XElement child in element.Elements())
        {
            string name = child.Name.LocalName;
            if (name == "availableFormats")
            {
                foreach (XElement f in child.Elements("numberFormat"))
                {
                    if (t.Formats.Count >= MaxFormatsPerTerritory)
                    {
                        throw new InvalidDataException("A territory has too many number formats.");
                    }

                    t.Formats.Add(ReadFormat(f));
                }
            }
            else if (TryKind(name, out DescKind kind))
            {
                t.Descs[(int)kind] = ReadDesc(child);
            }
        }

        return t;
    }

    private static FormatData ReadFormat(XElement element)
    {
        string pattern = Squash(Attr(element, "pattern")) ?? throw new InvalidDataException("A numberFormat has no pattern.");
        string format = element.Element("format")?.Value ?? throw new InvalidDataException("A numberFormat has no format.");
        CheckLength(pattern);
        CheckLength(format);
        FormatData f = new()
        {
            Pattern = pattern,
            Format = format,
            LeadingDigits = [.. element.Elements("leadingDigits").Select(e => Checked(Squash(e.Value) ?? ""))],
            IntlFormats = [.. element.Elements("intlFormat").Select(e => Checked(e.Value))],
            NationalPrefixFormattingRule = Attr(element, "nationalPrefixFormattingRule"),
            CarrierCodeFormattingRule = Attr(element, "carrierCodeFormattingRule"),
            HasNationalPrefixOptionalWhenFormatting = element.Attribute("nationalPrefixOptionalWhenFormatting") is not null,
            NationalPrefixOptionalWhenFormatting = Flag(element, "nationalPrefixOptionalWhenFormatting"),
        };
        return f;
    }

    private static DescData ReadDesc(XElement element)
    {
        DescData d = new();
        XElement? pattern = element.Element("nationalNumberPattern");
        if (pattern is not null)
        {
            d.Pattern = Checked(Squash(pattern.Value) ?? "");
        }

        XElement? lengths = element.Element("possibleLengths");
        if (lengths is not null)
        {
            d.HasPossibleLengths = true;
            d.PossibleLengths = ParseLengths(Attr(lengths, "national") ?? throw new InvalidDataException("possibleLengths has no national attribute."));
            string? local = Attr(lengths, "localOnly");
            d.LocalOnlyLengths = local is null ? [] : ParseLengths(local);
        }

        XElement? example = element.Element("exampleNumber");
        if (example is not null)
        {
            d.Example = Checked(example.Value.Trim());
        }

        return d;
    }

    /// <summary>Parses a length list such as <c>6,8,[10-12]</c> into sorted distinct values.</summary>
    internal static int[] ParseLengths(string text)
    {
        SortedSet<int> values = [];
        foreach (string raw in text.Split(','))
        {
            string item = raw.Trim();
            if (item.Length > 2 && item[0] == '[' && item[^1] == ']')
            {
                string[] range = item[1..^1].Split('-');
                if (range.Length != 2)
                {
                    throw new InvalidDataException($"Bad length range '{item}'.");
                }

                int lo = ParseInt(range[0], "length", 1, MaxLengthValue);
                int hi = ParseInt(range[1], "length", 1, MaxLengthValue);
                if (hi < lo)
                {
                    throw new InvalidDataException($"Bad length range '{item}'.");
                }

                for (int v = lo; v <= hi; v++)
                {
                    values.Add(v);
                }
            }
            else
            {
                values.Add(ParseInt(item, "length", -1, MaxLengthValue));
            }
        }

        return [.. values];
    }

    private static bool TryKind(string name, out DescKind kind)
    {
        switch (name)
        {
            case "generalDesc": kind = DescKind.General; return true;
            case "fixedLine": kind = DescKind.FixedLine; return true;
            case "mobile": kind = DescKind.Mobile; return true;
            case "tollFree": kind = DescKind.TollFree; return true;
            case "premiumRate": kind = DescKind.PremiumRate; return true;
            case "sharedCost": kind = DescKind.SharedCost; return true;
            case "personalNumber": kind = DescKind.PersonalNumber; return true;
            case "voip": kind = DescKind.Voip; return true;
            case "pager": kind = DescKind.Pager; return true;
            case "uan": kind = DescKind.Uan; return true;
            case "voicemail": kind = DescKind.Voicemail; return true;
            case "noInternationalDialling": kind = DescKind.NoInternationalDialling; return true;
            case "shortCode": kind = DescKind.ShortCode; return true;
            case "standardRate": kind = DescKind.StandardRate; return true;
            case "carrierSpecific": kind = DescKind.CarrierSpecific; return true;
            case "emergency": kind = DescKind.Emergency; return true;
            case "expandedEmergency": kind = DescKind.ExpandedEmergency; return true;
            case "smsServices": kind = DescKind.SmsServices; return true;
            default: kind = default; return false;
        }
    }

    private static string? Attr(XElement e, string name)
    {
        string? v = (string?)e.Attribute(name);
        if (v is not null)
        {
            CheckLength(v);
        }

        return v;
    }

    private static bool Flag(XElement e, string name) =>
        string.Equals((string?)e.Attribute(name), "true", StringComparison.Ordinal);

    /// <summary>Removes every white-space character: pattern text is written across lines for readability and the white space carries no meaning.</summary>
    private static string? Squash(string? text)
    {
        if (text is null)
        {
            return null;
        }

        CheckLength(text);
        StringBuilder sb = new(text.Length);
        foreach (char c in text)
        {
            if (!char.IsWhiteSpace(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static string Checked(string text)
    {
        CheckLength(text);
        return text;
    }

    private static void CheckLength(string text)
    {
        if (text.Length > MaxPatternLength)
        {
            throw new InvalidDataException("A metadata value is implausibly long.");
        }
    }

    private static int ParseInt(string text, string what, int min, int max)
    {
        if (!int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) || value < min || value > max)
        {
            throw new InvalidDataException($"Bad {what} value '{text}'.");
        }

        return value;
    }
}
