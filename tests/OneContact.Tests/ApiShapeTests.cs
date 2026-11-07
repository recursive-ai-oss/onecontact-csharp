using System.Buffers;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Models;
using RecursiveAI.OneContact.Names;
using RecursiveAI.OneContact.Web;
using RecursiveAI.OneContact.Phones;
using RecursiveAI.OneContact.Serialization;

namespace RecursiveAI.OneContact.Tests;

// The shapes of the public API that callers meet first: keys across countries, values that remember their metadata, parse overloads, reason codes
// in switch statements, text and JSON forms.
public class ApiShapeTests
{
    // --- address keys carry the country -----------------------------------------------------------------------------

    [Fact]
    public void Structural_address_keys_start_with_the_country_and_never_match_across_countries()
    {
        string[] lines = ["1-2-3 Shibuya"];
        PostalAddress jp = PostalAddress.Create(Region.Parse("JP"), adminArea: "Tokyo", locality: "Shibuya", postalCode: "150-0002", streetLines: lines);
        PostalAddress tw = PostalAddress.Create(Region.Parse("TW"), adminArea: "Tokyo", locality: "Shibuya", postalCode: "150-0002", streetLines: lines);
        Assert.StartsWith("jp|", jp.Key, StringComparison.Ordinal);
        Assert.StartsWith("tw|", tw.Key, StringComparison.Ordinal);
        Assert.False(Contacts.SameKey(jp, tw));
        Assert.True(Contacts.SameKey(jp, PostalAddress.Create(Region.Parse("JP"), adminArea: "Tokyo", locality: "Shibuya", postalCode: "1500002", streetLines: lines)));
    }

    [Fact]
    public void Structural_address_keys_do_not_follow_the_countrys_layout()
    {
        string[] lines = ["Av Paulista 1578"];
        PostalAddress br = PostalAddress.Create(Region.Parse("BR"), adminArea: "SP", locality: "Sao Paulo", postalCode: "01310-100", streetLines: lines);
        PostalAddress ar = PostalAddress.Create(Region.Parse("AR"), adminArea: "SP", locality: "Sao Paulo", postalCode: "01310-100", streetLines: lines);
        Assert.Equal(br.Key["br|".Length..], ar.Key["ar|".Length..]);
        Assert.NotEqual(br.Key, ar.Key);
    }

    // --- values remember the metadata that parsed them --------------------------------------------------------------

    private const string DotsMetadata = """
        <phoneNumberMetadata><territories>
          <territory id="ZA" countryCode="27" internationalPrefix="00" nationalPrefix="0">
            <availableFormats><numberFormat pattern="(\d{2})(\d{3})(\d{4})"><leadingDigits>[1-9]</leadingDigits><format>$1.$2.$3</format></numberFormat></availableFormats>
            <generalDesc><nationalNumberPattern>[1-9]\d{8}</nationalNumberPattern></generalDesc>
            <fixedLine><possibleLengths national="9"/><exampleNumber>101234567</exampleNumber><nationalNumberPattern>1\d{8}</nationalNumberPattern></fixedLine>
            <mobile><possibleLengths national="9"/><exampleNumber>712345678</exampleNumber><nationalNumberPattern>7\d{8}</nationalNumberPattern></mobile>
          </territory>
        </territories></phoneNumberMetadata>
        """;

    [Fact]
    public void A_phone_number_answers_from_the_metadata_that_parsed_it()
    {
        var parser = new PhoneNumberParser(PhoneMetadata.LoadXml(new MemoryStream(Encoding.UTF8.GetBytes(DotsMetadata))));
        Region za = Region.Parse("ZA");
        Assert.Equal(OperationStatus.Done, parser.TryParse("012 345 6789", za, false, out PhoneNumber custom, out _));
        Assert.Equal(parser.Format(custom, PhoneFormat.International), custom.Format(PhoneFormat.International));
        Assert.Contains("12.345.6789", custom.Format(PhoneFormat.National), StringComparison.Ordinal);
        Assert.Equal(PhoneNumberType.FixedLine, custom.Type);
        Assert.True(custom.IsValid);
        Assert.Equal(za, custom.Region);

        // The same digits through the built-in metadata are another thing: that metadata has no such South African plan.
        PhoneNumber builtIn = PhoneNumber.Parse("+27 12 345 6789");
        Assert.Equal(custom, builtIn);
        Assert.DoesNotContain("12.345.6789", builtIn.Format(PhoneFormat.National), StringComparison.Ordinal);

        // A number the matcher found keeps the matcher parser's metadata, too.
        var matcher = new PhoneNumberMatcher(parser);
        PhoneNumberMatch match = Assert.Single(matcher.FindAll("call 012 345 6789 now", za, PhoneMatchLeniency.Valid));
        Assert.Contains("12.345.6789", match.Number.Format(PhoneFormat.National), StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_phone_number_formats_as_empty_text()
    {
        Assert.Equal(string.Empty, default(PhoneNumber).E164);
        Assert.Equal(string.Empty, default(PhoneNumber).Format(PhoneFormat.National));
        Assert.Equal(string.Empty, $"{default(PhoneNumber):N}");
    }

    // --- address parse overloads --------------------------------------------------------------------------------------

    private const string BerlinNoCountry = "Unter den Linden 77, 10117 Berlin";

    [Fact]
    public void An_address_whose_country_cannot_be_told_fails_the_happy_path_and_keeps_the_partial_reading_in_the_engine()
    {
        Assert.False(PostalAddress.TryParse(BerlinNoCountry, out PostalAddress none));
        Assert.Equal(default, none);
        var ex = Assert.Throws<ContactParseException>(() => PostalAddress.Parse(BerlinNoCountry));
        Assert.Equal(ReasonCode.AddressCountryUndetermined, ex.Reason);
        Assert.Equal(ContactKind.Address, ex.Kind);

        // The status-returning form is unchanged: Done, the fields, the flag.
        OperationStatus status = PostalAddress.TryParse(BerlinNoCountry.AsSpan(), out PostalAddress partial, out _);
        Assert.Equal(OperationStatus.Done, status);
        Assert.True(partial.Flags.HasFlag(AddressFacts.CountryUndetermined));
        Assert.True(partial.Country.IsUnknown);

        Assert.False(Contacts.TryNormalize(BerlinNoCountry, default, out _));
    }

    [Fact]
    public void A_hinted_address_parses_with_a_region()
    {
        Region de = Region.Parse("DE");
        PostalAddress a = PostalAddress.Parse(BerlinNoCountry, de);
        Assert.Equal(de, a.Country);
        Assert.True(PostalAddress.TryParse(BerlinNoCountry, de, out PostalAddress b));
        Assert.Equal(a, b);
        Assert.True(PostalAddress.TryParse(BerlinNoCountry.AsSpan(), de, out PostalAddress c));
        Assert.Equal(a, c);
        Assert.Equal(a, PostalAddress.Parse(BerlinNoCountry.AsSpan(), de));

        // Region.Unknown means "from the content", like the unhinted overload.
        Assert.False(PostalAddress.TryParse(BerlinNoCountry, Region.Unknown, out _));
        Assert.Equal(Region.US, PostalAddress.Parse("1 main street apt 4, springfield, va 22162", Region.Unknown).Country);

        // A country that is only described structurally is refused for free text, with its reason.
        var ex = Assert.Throws<ContactParseException>(() => PostalAddress.Parse("1-1 Chiyoda, Tokyo 100-8111", Region.Parse("JP")));
        Assert.Equal(ReasonCode.AddressFreeTextRefused, ex.Reason);
        Assert.False(PostalAddress.TryParse(null, de, out _));
    }

    [Fact]
    public void Names_and_companies_have_hinted_overloads_like_phones()
    {
        Region hu = Region.Parse("HU");
        PersonName byRegion = PersonName.Parse("Kovács János", hu);
        Assert.Equal("Kovács", byRegion.Family.Text);
        Assert.True(PersonName.TryParse("Kovács János", hu, out PersonName b));
        Assert.Equal(byRegion, b);
        Assert.True(PersonName.TryParse("Kovács János", NameHints.ForCountry(hu), out PersonName c));
        Assert.Equal(byRegion, c);
        Assert.True(PersonName.TryParse("Kovács János".AsSpan(), NameHints.ForCountry(hu), out PersonName d));
        Assert.Equal(byRegion, d);
        Assert.False(PersonName.TryParse(null, hu, out _));

        Region de = Region.Parse("DE");
        CompanyName company = CompanyName.Parse("Muster Aktiengesellschaft", de);
        Assert.Equal("AG", company.LegalForm!.Abbreviation);
        Assert.True(CompanyName.TryParse("Muster Aktiengesellschaft", de, out CompanyName e));
        Assert.Equal(company, e);
        Assert.True(CompanyName.TryParse("Muster Aktiengesellschaft", NameHints.ForCountry(de), out _));
        Assert.True(CompanyName.TryParse("Muster Aktiengesellschaft".AsSpan(), NameHints.ForCountry(de), out _));
    }

    // --- reason codes in switch ---------------------------------------------------------------------------------------

    private static string Describe(ReasonCode reason) => reason.Value switch
    {
        ReasonValue.PhoneTooShort => "short",
        ReasonValue.PhoneRegionRequired => "region",
        ReasonValue.EmailMissingAt => "at",
        ReasonValue.AddressCountryUndetermined => "country",
        _ => "other",
    };

    [Fact]
    public void Reason_codes_can_be_switched_on()
    {
        var ex = Assert.Throws<ContactParseException>(() => PhoneNumber.Parse("2024561111"));
        Assert.Equal("region", Describe(ex.Reason));
        Assert.Equal("at", Describe(Assert.Throws<ContactParseException>(() => EmailAddress.Parse("nobody")).Reason));
        Assert.Equal("country", Describe(ReasonCode.AddressCountryUndetermined));
        Assert.Equal(ReasonValue.None, default(ReasonCode).Value);
    }

    [Fact]
    public void Every_reason_code_has_an_enumeration_member_with_its_value()
    {
        ReasonCode[] all =
        [
            ReasonCode.Empty, ReasonCode.InvalidRegion, ReasonCode.UnrecognizedKind, ReasonCode.NotImplemented,
            ReasonCode.PhoneRegionRequired, ReasonCode.PhoneNotANumber, ReasonCode.PhoneInvalidCountryCode, ReasonCode.PhoneTooShortAfterIdd, ReasonCode.PhoneTooShort, ReasonCode.PhoneTooLong, ReasonCode.PhoneUnknownRegion,
            ReasonCode.WebHostMissing, ReasonCode.WebInvalidHost, ReasonCode.WebInvalidPort, ReasonCode.WebInvalidUrl,
            ReasonCode.AddressFreeTextRefused, ReasonCode.AddressCountryRequired, ReasonCode.AddressModelRequired, ReasonCode.AddressCountryUndetermined, ReasonCode.AddressUnsupportedCountry,
            ReasonCode.AddressCountryConflict, ReasonCode.AddressNoDeliveryLine, ReasonCode.AddressNoLastLine, ReasonCode.AddressUnplacedText, ReasonCode.AddressFieldsDisputed,
            ReasonCode.PersonNotAPerson, ReasonCode.PersonMultiple, ReasonCode.PersonNoName, ReasonCode.PersonUnexpectedCharacters, ReasonCode.PersonTooLong, ReasonCode.PersonAmbiguousConnector,
            ReasonCode.PersonSingleWord, ReasonCode.PersonCasingRestored, ReasonCode.PersonFamilyNameGuessed, ReasonCode.PersonNoEvidence,
            ReasonCode.CompanyNoBase, ReasonCode.CompanyCareOfOnly, ReasonCode.CompanyLegalFormAmbiguous, ReasonCode.CompanyLegalFormOutsideCountry, ReasonCode.CompanyLegalFormInactive,
            ReasonCode.CompanyEmptyTradingName, ReasonCode.CompanyTooLong,
        ];
        foreach (ReasonCode r in all)
        {
            Assert.True(Enum.IsDefined(r.Value), r.Id);
            Assert.Equal(((int)r.Kind << 16) | r.Number, (int)r.Value);
        }
    }

    // --- Region -------------------------------------------------------------------------------------------------------

    [Fact]
    public void Region_validates_codes_and_the_unknown_region_round_trips()
    {
        Assert.False(Region.TryParse("UK", out _));
        Assert.False(Region.TryParse("XX", out _));
        Assert.False(Region.TryParse("", out _));
        var ex = Assert.Throws<ContactParseException>(() => Region.Parse("UK"));
        Assert.Contains("GB", ex.Message, StringComparison.Ordinal);
        Assert.Equal(Region.Unknown, Region.Parse("zz"));
        Assert.Equal(Region.Unknown, Region.Parse("ZZ"));
        Assert.True(Region.Parse("ZZ").IsUnknown);
        Assert.Equal("ZZ", Region.Unknown.ToString());
        Assert.Equal(Region.Unknown, JsonSerializer.Deserialize<Region>(JsonSerializer.Serialize(Region.Unknown)));
        Assert.Equal(Region.Parse("DE"), JsonSerializer.Deserialize<Region>("\"de\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Region>("\"UK\""));
        Assert.True(Region.TryParse("XK", out _));
        Assert.True(Region.TryParse("AC", out _));
    }

    [Fact]
    public void Every_region_in_the_phone_and_address_data_is_a_known_region()
    {
        foreach (Region r in PhoneNumberParser.Default.Metadata.Regions)
        {
            Assert.True(Region.TryParse(r.Code, out Region back) && back == r, r.Code);
        }

        foreach (Region r in AddressRegions.All)
        {
            Assert.True(Region.TryParse(r.Code, out Region back) && back == r, r.Code);
        }
    }

    // --- formatting -------------------------------------------------------------------------------------------------

    [Fact]
    public void A_phone_number_formats_with_short_format_specifiers()
    {
        PhoneNumber n = PhoneNumber.Parse("(202) 456-1111 ext. 9", Region.US);
        Assert.Equal(n.Canonical, $"{n}");
        Assert.Equal(n.Canonical, $"{n:C}");
        Assert.Equal("+12024561111", $"{n:E}");
        Assert.Equal(n.Format(PhoneFormat.International), $"{n:I}");
        Assert.Equal(n.Format(PhoneFormat.National), $"{n:N}");
        Assert.Equal(n.Format(PhoneFormat.Rfc3966), $"{n:R}");
        Assert.Equal($"{n:N}", $"{n:n}");
        Assert.Equal(n.Format(PhoneFormat.National), string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:N}", n));
        char[] buffer = new char[64];
        Assert.True(n.TryFormat(buffer, out int written, "I", null));
        Assert.Equal(n.Format(PhoneFormat.International), new string(buffer, 0, written));
        Assert.False(n.TryFormat(buffer.AsSpan(0, 3), out _, "I", null));
        Assert.Throws<FormatException>(() => $"{n:Q}");
        Assert.Throws<FormatException>(() => $"{n:NN}");
        Assert.Throws<FormatException>(() => n.TryFormat(new char[64], out _, "X", null));
    }

    // --- JSON -------------------------------------------------------------------------------------------------------

    private sealed record Holder(PhoneNumber Phone);

    [Fact]
    public void Json_errors_carry_the_reason_the_position_and_the_cause()
    {
        JsonException ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Holder>("{\n \"Phone\": \"202 456 1111\"\n}"));
        Assert.Contains("phone.region-required", ex.Message, StringComparison.Ordinal);
        Assert.Contains("byte offset", ex.Message, StringComparison.Ordinal);
        Assert.Equal("$.Phone", ex.Path);
        Assert.Equal(1, ex.LineNumber);
        Assert.IsType<ContactParseException>(ex.InnerException);
        Assert.Equal(ReasonCode.PhoneRegionRequired, ((ContactParseException)ex.InnerException!).Reason);

        JsonException notString = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EmailAddress>("5"));
        Assert.Contains("JSON string", notString.Message, StringComparison.Ordinal);
        Assert.Contains("email.", Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<EmailAddress>("\"nobody\"")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_phone_converter_can_be_given_a_region()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new PhoneNumberJsonConverter(Region.US));
        PhoneNumber n = JsonSerializer.Deserialize<PhoneNumber>("\"(202) 456-1111\"", options);
        Assert.Equal("+12024561111", n.E164);
        Assert.Equal("\"+12024561111\"", JsonSerializer.Serialize(n, options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PhoneNumber>("\"(202) 456-1111\""));
        Assert.Equal(n, JsonSerializer.Deserialize<PhoneNumber>("\"+12024561111\""));
    }

    [Fact]
    public void A_postal_address_has_one_json_shape_and_reads_back_equal()
    {
        PostalAddress parsed = PostalAddress.Parse("1 main street apt 4, springfield, va 22162");
        PostalAddress made = PostalAddress.Create(Region.Parse("JP"), adminArea: "Tokyo", locality: "Shibuya", postalCode: "150-0002", streetLines: ["1-2-3 Shibuya"]);
        string[] required = ["canonical", "country", "adminArea", "locality", "dependentLocality", "postalCode", "sortingCode", "organization", "recipient", "streetLines", "flags", "sourceStart", "sourceLength"];
        foreach (PostalAddress a in new[] { parsed, made })
        {
            string json = JsonSerializer.Serialize(a);
            using JsonDocument doc = JsonDocument.Parse(json);
            foreach (string name in required)
            {
                Assert.True(doc.RootElement.TryGetProperty(name, out _), name);
            }

            PostalAddress back = JsonSerializer.Deserialize<PostalAddress>(json);
            Assert.Equal(a, back);
            Assert.Equal(a.Key, back.Key);
            Assert.Equal(a.Country, back.Country);
        }

        using JsonDocument p = JsonDocument.Parse(JsonSerializer.Serialize(parsed));
        Assert.True(p.RootElement.TryGetProperty("text", out _));
        Assert.Equal(parsed.ToString(AddressStyle.Display), JsonSerializer.Deserialize<PostalAddress>(JsonSerializer.Serialize(parsed)).ToString(AddressStyle.Display));
    }

    [Fact]
    public void An_email_keeps_its_display_name_through_json()
    {
        EmailAddress e = EmailAddress.Parse("\"Smith, \\\"JJ\\\" J.\" <J.Smith@Example.com>");
        Assert.NotNull(e.DisplayName);
        EmailAddress back = JsonSerializer.Deserialize<EmailAddress>(JsonSerializer.Serialize(e));
        Assert.Equal(e, back);
        Assert.Equal(e.DisplayName, back.DisplayName);

        EmailAddress plain = EmailAddress.Parse("a@example.com");
        Assert.Equal("\"a@example.com\"", JsonSerializer.Serialize(plain));
        Assert.Null(JsonSerializer.Deserialize<EmailAddress>("\"a@example.com\"").DisplayName);
    }

    [Fact]
    public void A_web_address_documents_what_json_drops_and_flags_non_web_schemes()
    {
        WebAddress w = WebAddress.Parse("https://app.example.com/#/settings");
        Assert.True(w.Flags.HasFlag(WebFacts.FragmentRemoved));
        Assert.Equal("\"https://app.example.com/\"", JsonSerializer.Serialize(w));
        Assert.True(w.IsWebsite);
        Assert.True(WebAddress.Parse("www.example.com").IsWebsite);
        WebAddress js = WebAddress.Parse("javascript:alert(1)");
        Assert.False(js.IsWebsite);
        Assert.True(js.Flags.HasFlag(WebFacts.NonWebScheme));
        Assert.False(WebAddress.Parse("mailto:a@example.com").IsWebsite);
        Assert.False(default(WebAddress).IsWebsite);
    }

    [Fact]
    public void The_public_suffix_list_returns_null_for_text_that_is_not_a_domain()
    {
        Assert.Null(PublicSuffixList.GetRegistrableDomain("not a domain"));
        Assert.Null(PublicSuffixList.GetRegistrableDomain("https://www.example.co.uk/"));
        Assert.Null(PublicSuffixList.GetPublicSuffix("example.com:8080"));
        Assert.Equal("example.co.uk", PublicSuffixList.GetRegistrableDomain("www.example.co.uk"));
    }

    // --- explicit models --------------------------------------------------------------------------------------------

    private sealed class FakeAddressModel : IAddressFieldModel
    {
        internal int Calls { get; private set; }

        public IReadOnlyList<ModelSpan> Tag(string text)
        {
            Calls++;
            return [new ModelSpan(0, text.Length, "locality", 0.9f)];
        }
    }

    private sealed class FakeNameModel : INameModel
    {
        internal int Calls { get; private set; }

        public NameModelResult Analyze(string text)
        {
            Calls++;
            return new NameModelResult(NameClass.Person, 0.9f, [new ModelSpan(0, text.Length, "Family", 0.9f)]);
        }
    }

    [Fact]
    public void An_explicit_address_model_is_used_for_one_call_and_null_means_rules_only()
    {
        var fake = new FakeAddressModel();
        var registered = new FakeAddressModel();
        using (ContactModels.Use(registered, null))
        {
            Assert.Equal(OperationStatus.Done, PostalAddress.TryParse("Springfield VA 22162", Region.US, fake, out PostalAddress a, out _));
            Assert.Equal(1, fake.Calls);
            Assert.Equal(0, registered.Calls);
            Assert.False(a.Canonical.Length == 0);

            // Rules only, although a model is in use for the surrounding flow.
            OperationStatus status = PostalAddress.TryParse("Springfield VA 22162", Region.US, null, out _, out ParseDetail detail);
            Assert.Equal(OperationStatus.InvalidData, status);
            Assert.Equal(ReasonCode.AddressModelRequired, detail.PrimaryReason);
            Assert.Equal(0, registered.Calls);
        }

        // The scope ended: the explicit model does not stay registered.
        Assert.Null(ContactModels.Address);
    }

    [Fact]
    public void An_explicit_name_model_reads_what_the_rules_leave_open()
    {
        var fake = new FakeNameModel();
        Assert.Equal(NameClass.Person, NameParsing.Classify("jsmith2019".AsSpan(), default, fake));
        Assert.True(fake.Calls > 0);
        Assert.Equal(NameClass.Unknown, NameParsing.Classify("jsmith2019".AsSpan(), default, null));
        NameParseResult r = NameParsing.Split("jsmith2019".AsSpan(), default, fake);
        Assert.Equal(NameClass.Person, r.Class);
        Assert.Null(ContactModels.Name);
    }

    // --- extraction limits ------------------------------------------------------------------------------------------

    [Fact]
    public void Extraction_wraps_the_parsers_limit_exception_and_exposes_the_limits()
    {
        string deep = string.Concat(Enumerable.Repeat("<div>", 6000)) + "x@y.com";
        var ex = Assert.Throws<ContactExtractionException>(() => new ContactExtractor().ExtractHtml(deep));
        Assert.Equal("MaxNesting", ex.LimitName);
        Assert.NotNull(ex.InnerException);

        var strict = new ContactExtractor { Limits = new ExtractionLimits { MaxInputLength = 100 } };
        var big = Assert.Throws<ContactExtractionException>(() => strict.ExtractHtml(new string('a', 101)));
        Assert.Equal("MaxInputLength", big.LimitName);
        Assert.Equal(100, big.Limit);
        Assert.Throws<ContactExtractionException>(() => strict.ExtractText(new string('a', 101)));
        Assert.Throws<ContactExtractionException>(() => strict.ExtractSite([ContactPage.FromHtml("a", new string('a', 101))]));
        Assert.Throws<ContactExtractionException>(() => ContactExtractor.Extract(deep));

        ExtractionLimits two = new() { MaxFields = 1 };
        var limited = new ContactExtractor { Limits = two }.ExtractText("Mail a@example.com or b@example.org or call +1 202 456 1111.");
        Assert.Contains(limited.Issues, i => i.Kind == RecursiveAI.OneContact.Extraction.Records.ExtractIssueKind.LimitExceeded);
    }

    // --- name length ------------------------------------------------------------------------------------------------

    [Fact]
    public void Very_long_names_are_refused_with_a_reason()
    {
        string huge = new('a', NameParsing.MaxInputLength + 1);
        Assert.Equal(ReasonCode.PersonTooLong, Assert.Throws<ContactParseException>(() => PersonName.Parse(huge)).Reason);
        Assert.Equal(ReasonCode.CompanyTooLong, Assert.Throws<ContactParseException>(() => CompanyName.Parse(huge)).Reason);
        Assert.Equal(NameClass.Unknown, NameParsing.Classify(huge));
        Assert.False(PersonName.TryParse(huge, out _));
        Assert.True(PersonName.TryParse(new string('a', 30) + " Smith", out _));
    }
}
