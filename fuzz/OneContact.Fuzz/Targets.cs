using System.Text;
using System.Text.Json;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Models.Superset;
using RecursiveAI.OneContact.Models.Tier1;
using RecursiveAI.OneContact.Data;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Phones;
using RecursiveAI.OneContact.Serialization;
using RecursiveAI.OneContact.Text.Idna;
using RecursiveAI.OneHtml;

namespace RecursiveAI.OneContact.Fuzz;

/// <summary>Source-generated JSON metadata for property-name reads, which the library context does not cover.</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(Dictionary<PhoneNumber, int>))]
internal sealed partial class FuzzJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}

/// <summary>
/// The fuzz targets. Each takes the raw bytes of one input. A target passes when it returns, or throws only what the API documents
/// for hostile input (<see cref="ContactParseException"/> from <c>Parse</c>, <see cref="InvalidDataException"/> from loaders,
/// <see cref="JsonException"/> from converters, <see cref="HtmlLimitExceededException"/> from the page parser). Any other exception is a
/// finding, as is an invariant that does not hold (a match outside the text, an offset outside the input).
/// </summary>
internal static class Targets
{
    /// <summary>Gets the targets by name. Text targets decode the bytes as UTF-8, so invalid bytes reach the parsers as U+FFFD.</summary>
    internal static readonly Dictionary<string, Action<byte[]>> All = new(StringComparer.Ordinal)
    {
        ["phone"] = Text(Phone),
        ["phone-metadata"] = bytes => Loader(() => PhoneMetadata.LoadXml(new MemoryStream(bytes))),
        ["short-number-metadata"] = bytes => Loader(() => ShortNumberInfo.LoadXml(PhoneMetadata.Default, new MemoryStream(bytes))),
        ["phone-match"] = Text(MatchAll),
        ["phone-typing"] = Text(TypeAll),
        ["email"] = Text(text => { _ = EmailAddress.TryParse(text, out _, out _); Expect(() => EmailAddress.Parse(text)); }),
        ["web"] = Text(text => { _ = WebAddress.TryParse(text, out _, out _); Expect(() => WebAddress.Parse(text)); }),
        ["idna"] = Text(Idna),
        ["address"] = Text(text => { _ = PostalAddress.TryParse(text, out _, out _); Expect(() => PostalAddress.Parse(text)); }),
        ["address-hint"] = Text(HintedAddress),
        ["address-validate"] = Text(ValidateAddress),
        ["person"] = Text(text => { _ = PersonName.TryParse(text, out _, out _); Expect(() => PersonName.Parse(text)); }),
        ["company"] = Text(text => { _ = CompanyName.TryParse(text, out _, out _); Expect(() => CompanyName.Parse(text)); }),
        ["normalize"] = Text(text => { _ = Contacts.TryNormalize(text, Region.US, out _); Expect(() => Contacts.Normalize(text, Region.US)); }),
        ["extract-html"] = ExtractHtml,
        ["extract-text"] = Text(text => _ = new ContactExtractor { DefaultRegion = Region.US }.ExtractText(text)),
        ["model-tier1"] = bytes => Loader(() => Tier1Model.Load(new MemoryStream(bytes))),
        ["model-superset"] = bytes => Loader(() => SupersetModel.Load(new MemoryStream(bytes))),
        ["json"] = Json,
        ["canary"] = bytes => { _ = PhoneNumber.TryParse(Encoding.UTF8.GetString(bytes), Region.US, out _); if (bytes.Length > 1 && bytes[0] == (byte)'X' && bytes[1] == (byte)'a') { throw new InvalidOperationException("canary"); } },
        ["tables"] = bytes => Loader(() => CompiledTableFile.Read(bytes)),
    };

    private static readonly string[] s_hintRegions = ["US", "CA", "GB", "DE", "FR", "NL", "PL", "MX", "AU", "ES", "IT", "CH"];

    private static Action<byte[]> Text(Action<string> target) => bytes => target(Encoding.UTF8.GetString(bytes));

    // Parse is allowed to refuse with ContactParseException and nothing else.
    private static void Expect(Action parse)
    {
        try
        {
            parse();
        }
        catch (ContactParseException)
        {
        }
    }

    // A loader of user-supplied files may refuse with InvalidDataException and nothing else.
    private static void Loader(Action load)
    {
        try
        {
            load();
        }
        catch (InvalidDataException)
        {
        }
    }

    private static void Phone(string text)
    {
        foreach (Region region in new[] { Region.US, Region.Unknown, Region.Parse("GB"), Region.Parse("BR") })
        {
            if (PhoneNumber.TryParse(text, region, out PhoneNumber number))
            {
                // Everything a valid parse offers must hold for whatever it returned.
                foreach (PhoneFormat format in Enum.GetValues<PhoneFormat>())
                {
                    _ = number.Format(format);
                }

                _ = number.Key;
                _ = number.Type;
                _ = number.Region;
                _ = number.FormatOutOfCountry(Region.US);
                _ = number.FormatForMobileDialing(Region.US, withFormatting: true);
                _ = ShortNumberInfo.Default.IsValid(number);
                _ = ShortNumberInfo.Default.GetExpectedCost(number);
            }
        }

        Expect(() => PhoneNumber.Parse(text, Region.US));
        _ = ShortNumberInfo.Default.IsEmergencyNumber(text, Region.US);
    }

    // The matcher must not throw, and every match must be the text it says it is, inside the input.
    private static void MatchAll(string text)
    {
        foreach (Region region in new[] { Region.US, Region.Parse("GB"), Region.Parse("DE"), Region.Unknown })
        {
            foreach (PhoneMatchLeniency leniency in Enum.GetValues<PhoneMatchLeniency>())
            {
                int previousEnd = 0;
                foreach (PhoneNumberMatch m in PhoneNumberMatcher.Default.FindAll(text, region, leniency))
                {
                    if (m.Start < previousEnd || m.End > text.Length || !string.Equals(text.Substring(m.Start, m.RawString.Length), m.RawString, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("a match does not lie in the text");
                    }

                    previousEnd = m.End;
                }
            }
        }
    }

    // The formatter takes any key presses; the remembered position stays inside the last output.
    private static void TypeAll(string text)
    {
        foreach (Region region in new[] { Region.US, Region.Parse("GB"), Region.Parse("BR"), Region.Unknown })
        {
            AsYouTypeFormatter formatter = new(region);
            int i = 0;
            foreach (char c in text)
            {
                string output = i++ % 7 == 3 ? formatter.InputDigitAndRememberPosition(c) : formatter.InputDigit(c);
                if (formatter.RememberedPosition < 0 || formatter.RememberedPosition > output.Length)
                {
                    throw new InvalidOperationException("the remembered position is outside the output");
                }
            }
        }
    }

    // UTS #46 processing under every option combination that matters; an accepted ASCII result must convert back without throwing.
    private static void Idna(string text)
    {
        for (int options = 0; options < 128; options += 3)
        {
            if (Uts46.TryToAscii(text, (Uts46Options)options, out string ascii))
            {
                _ = Uts46.TryToUnicode(ascii, (Uts46Options)options, out _);
            }

            _ = Uts46.TryToUnicode(text, (Uts46Options)options, out _);
        }
    }

    // Free text read with each country hint; whatever parses must lay out, validate and key without throwing.
    private static void HintedAddress(string text)
    {
        foreach (string code in s_hintRegions)
        {
            if (PostalAddressParser.TryParse(text, Region.Parse(code), out PostalAddress address, out _) == System.Buffers.OperationStatus.Done)
            {
                _ = address.ToString(AddressStyle.Postal);
                _ = address.ToString(AddressStyle.Display, AddressLayout.SingleLine);
                _ = address.Key;
                _ = address.Validate();
            }
        }
    }

    private static void ValidateAddress(string text)
    {
        if (PostalAddress.TryParse(text, out PostalAddress address))
        {
            _ = address.Validate();
            _ = address.GetLabelLines();
            _ = address.GetLabelLines(AddressScript.Local);
        }
    }

    // Raw HTML bytes through OneHtml, markup readers and text rules. The page limits are a documented refusal.
    private static void ExtractHtml(byte[] html)
    {
        try
        {
            ExtractResult result = new ContactExtractor { DefaultRegion = Region.US }.ExtractHtml(html);
            _ = result.Records.Count;
        }
        catch (HtmlLimitExceededException)
        {
        }
    }

    // Every value type read from arbitrary bytes. The converters refuse with JsonException and nothing else.
    private static void Json(byte[] bytes)
    {
        JsonGuard(() => JsonSerializer.Deserialize(bytes, ContactJsonContext.Default.PhoneNumber));
        JsonGuard(() => JsonSerializer.Deserialize(bytes, ContactJsonContext.Default.EmailAddress));
        JsonGuard(() => JsonSerializer.Deserialize(bytes, ContactJsonContext.Default.WebAddress));
        JsonGuard(() => JsonSerializer.Deserialize(bytes, ContactJsonContext.Default.PostalAddress));
        JsonGuard(() => JsonSerializer.Deserialize(bytes, ContactJsonContext.Default.PersonName));
        JsonGuard(() => JsonSerializer.Deserialize(bytes, ContactJsonContext.Default.CompanyName));
        JsonGuard(() => JsonSerializer.Deserialize(bytes, ContactJsonContext.Default.Region));
        JsonGuard(() => JsonSerializer.Deserialize(bytes, FuzzJsonContext.Default.DictionaryPhoneNumberInt32));
    }

    private static void JsonGuard(Action read)
    {
        try
        {
            read();
        }
        catch (JsonException)
        {
        }
    }
}
