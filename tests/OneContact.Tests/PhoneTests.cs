using System.Buffers;
using System.Text;
using System.Text.Json;
using RecursiveAI.OneContact.Keys;
using RecursiveAI.OneContact.Phones;
using RecursiveAI.OneContact.Serialization;

namespace RecursiveAI.OneContact.Tests;

// Expected values were written by hand from the numbering plans' published examples and checked against a released libphonenumber
// (see tools/Oracles/Phones); the parity harness there is what covers every region.
public class PhoneTests
{
    private static readonly Region GB = Region.Parse("GB");
    private static readonly Region DE = Region.Parse("DE");
    private static readonly Region BR = Region.Parse("BR");
    private static readonly Region AR = Region.Parse("AR");
    private static readonly Region RU = Region.Parse("RU");

    [Fact]
    public void Happy_path_reads_like_the_design_sample()
    {
        PhoneNumber p = PhoneNumber.Parse("(202) 456-1111", Region.US);
        Assert.Equal("+12024561111", p.E164);
        Assert.Equal("+1 202-456-1111", p.Format(PhoneFormat.International));
        Assert.Equal("(202) 456-1111", p.Format(PhoneFormat.National));
        Assert.Equal("tel:+1-202-456-1111", p.Format(PhoneFormat.Rfc3966));
        Assert.Equal(PhoneNumberType.FixedLineOrMobile, p.Type);
        Assert.Equal("+12024561111", p.Key);
        Assert.Equal("+12024561111", p.Canonical);
        Assert.True(p.IsValid);
        Assert.Equal(Region.US, p.Region);
        Assert.Equal(1, p.CountryCode);
        Assert.Equal(2024561111UL, p.NationalNumber);
    }

    [Fact]
    public void National_input_without_a_region_fails_saying_a_region_is_needed()
    {
        ContactParseException e = Assert.Throws<ContactParseException>(() => PhoneNumber.Parse("2024561111"));
        Assert.Equal(ReasonCode.PhoneRegionRequired, e.Reason);
        Assert.Equal("phone.region-required", e.Reason.Id);
        Assert.Contains("region", e.Message, StringComparison.Ordinal);
        Assert.False(PhoneNumber.TryParse("2024561111", out _));
        Assert.Equal(OperationStatus.InvalidData, PhoneNumber.TryParse("2024561111".AsSpan(), out _, out ParseDetail d));
        Assert.Equal(ReasonCode.PhoneRegionRequired, d.PrimaryReason);
    }

    [Fact]
    public void International_input_needs_no_region()
    {
        PhoneNumber p = PhoneNumber.Parse("+44 20 7946 0958");
        Assert.Equal("+442079460958", p.Canonical);
        Assert.Equal(GB, p.Region);
        Assert.Equal("020 7946 0958", p.Format(PhoneFormat.National));
        Assert.Equal(CountryCodeSource.FromNumberWithPlusSign, p.CountryCodeSource);
    }

    [Theory]
    [InlineData("(202) 456-1111", "US", "+12024561111", "+1 202-456-1111", "(202) 456-1111", "tel:+1-202-456-1111", PhoneNumberType.FixedLineOrMobile)]
    [InlineData("020 7946 0958", "GB", "+442079460958", "+44 20 7946 0958", "020 7946 0958", "tel:+44-20-7946-0958", PhoneNumberType.FixedLine)]
    [InlineData("06 6988 2000", "IT", "+390669882000", "+39 06 6988 2000", "06 6988 2000", "tel:+39-06-6988-2000", PhoneNumberType.FixedLine)]
    [InlineData("+49 30 901820", "ZZ", "+4930901820", "+49 30 901820", "030 901820", "tel:+49-30-901820", PhoneNumberType.FixedLine)]
    [InlineData("03-1234-5678", "JP", "+81312345678", "+81 3-1234-5678", "03-1234-5678", "tel:+81-3-1234-5678", PhoneNumberType.FixedLine)]
    [InlineData("0412 345 678", "AU", "+61412345678", "+61 412 345 678", "0412 345 678", "tel:+61-412-345-678", PhoneNumberType.Mobile)]
    [InlineData("01 70 18 99 00", "FR", "+33170189900", "+33 1 70 18 99 00", "01 70 18 99 00", "tel:+33-1-70-18-99-00", PhoneNumberType.FixedLine)]
    [InlineData("8 (495) 123-45-67", "RU", "+74951234567", "+7 495 123-45-67", "8 (495) 123-45-67", "tel:+7-495-123-45-67", PhoneNumberType.FixedLine)]
    [InlineData("02-312-3456", "KR", "+8223123456", "+82 2-312-3456", "02-312-3456", "tel:+82-2-312-3456", PhoneNumberType.FixedLine)]
    [InlineData("011 15-1234-5678", "AR", "+5491112345678", "+54 9 11 1234-5678", "011 15-1234-5678", "tel:+54-9-11-1234-5678", PhoneNumberType.Mobile)]
    [InlineData("+800 1234 5678", "ZZ", "+80012345678", "+800 1234 5678", "1234 5678", "tel:+800-1234-5678", PhoneNumberType.TollFree)]
    public void Parses_validates_types_and_formats(string text, string region, string e164, string international, string national, string rfc3966, PhoneNumberType type)
    {
        PhoneNumber p = PhoneNumber.Parse(text, Region.Parse(region));
        Assert.Equal(e164, p.E164);
        Assert.Equal(international, p.Format(PhoneFormat.International));
        Assert.Equal(national, p.Format(PhoneFormat.National));
        Assert.Equal(rfc3966, p.Format(PhoneFormat.Rfc3966));
        Assert.Equal(type, p.Type);
        Assert.True(p.IsValid);
    }

    [Fact]
    public void Leading_zeros_that_belong_to_the_number_survive()
    {
        PhoneNumber p = PhoneNumber.Parse("06 6988 2000", Region.Parse("IT"));
        Assert.True(p.ItalianLeadingZero);
        Assert.Equal(1, p.LeadingZeroCount);
        Assert.Equal(669882000UL, p.NationalNumber);
        Assert.Equal("0669882000", p.NationalSignificantNumber);
        Assert.Equal("+390669882000", p.Canonical);
    }

    [Theory]
    [InlineData("202-456-1111 ext. 77", "77")]
    [InlineData("202-456-1111 x77", "77")]
    [InlineData("202-456-1111 #77", "77")]
    [InlineData("202-456-1111,,77", "77")]
    [InlineData("tel:+1-202-456-1111;ext=77", "77")]
    [InlineData("202-456-1111 ext 77", "77")]
    public void Extensions_are_split_off_and_kept(string text, string extension)
    {
        PhoneNumber p = PhoneNumber.Parse(text, Region.US);
        Assert.Equal(extension, p.Extension);
        Assert.Equal("+12024561111;ext=" + extension, p.Canonical);
        Assert.Equal("+12024561111", p.E164);
        Assert.Equal("+12024561111x" + extension, p.Key);
        Assert.Equal("tel:+1-202-456-1111;ext=" + extension, p.Format(PhoneFormat.Rfc3966));
        Assert.Equal("(202) 456-1111 ext. " + extension, p.Format(PhoneFormat.National));
        Assert.True(p.Flags.HasFlag(PhoneFacts.HasExtension));
    }

    [Fact]
    public void Letters_become_keypad_digits_only_when_there_are_three_or_more()
    {
        PhoneNumber p = PhoneNumber.Parse("1-800-FLOWERS", Region.US);
        Assert.Equal("+18003569377", p.E164);
        Assert.Equal(PhoneNumberType.TollFree, p.Type);
        Assert.True(p.Flags.HasFlag(PhoneFacts.Vanity));
        Assert.True(PhoneNumberParser.IsVanityNumber("1-800-FLOWERS"));
        Assert.False(PhoneNumberParser.IsVanityNumber("202-456-1111 ext 7"));
    }

    [Fact]
    public void Digits_of_other_scripts_are_read_as_digits()
    {
        Assert.Equal("+12024561111", PhoneNumber.Parse("\uFF12\uFF10\uFF12-\uFF14\uFF15\uFF16-\uFF11\uFF11\uFF11\uFF11", Region.US).E164);
        Assert.Equal("+12024561111", PhoneNumber.Parse("\u0662\u0660\u0662\u0664\u0665\u0666\u0661\u0661\u0661\u0661", Region.US).E164);
    }

    [Fact]
    public void Only_ascii_digits_pass_through_the_letter_mapping()
    {
        // With three letters the text is a vanity number, and a vanity number keeps only ASCII digits and mapped letters.
        PhoneNumber vanity = PhoneNumber.Parse("1-800-\u0663FLOWERS", Region.US);
        Assert.Equal("+18003569377", vanity.E164);

        // With two letters it is not, and every script's digits count.
        PhoneNumber plain = PhoneNumber.Parse("1-800-\u0663-35-93-77 ab", Region.US);
        Assert.Equal("+180033593", plain.E164[..10]);
    }

    [Fact]
    public void Idd_prefix_and_national_prefix_are_recognised()
    {
        PhoneNumber idd = PhoneNumber.Parse("011 44 20 7946 0958", Region.US);
        Assert.Equal("+442079460958", idd.E164);
        Assert.Equal(CountryCodeSource.FromNumberWithIdd, idd.CountryCodeSource);
        Assert.Equal("+442079460958", PhoneNumber.Parse("0044 20 7946 0958", GB).E164);

        PhoneNumber carrier = PhoneNumber.Parse("0 31 11 2345-6789", BR);
        Assert.Equal("+551123456789", carrier.E164);
        Assert.Equal("31", carrier.PreferredDomesticCarrierCode);

        // The calling code written without a plus: it is dropped only when that makes a better number.
        Assert.Equal(CountryCodeSource.FromNumberWithoutPlusSign, PhoneNumber.Parse("1 202 456 1111", Region.US).CountryCodeSource);
    }

    [Fact]
    public void Tel_uris_with_a_phone_context_are_understood()
    {
        Assert.Equal("+12024561111", PhoneNumber.Parse("tel:2024561111;phone-context=+1").E164);
        Assert.Equal("+12024561111", PhoneNumber.Parse("tel:+12024561111;isub=123").E164);
        Assert.Equal("+12024561111", PhoneNumber.Parse("tel:2024561111;phone-context=example.com", Region.US).E164);
        ContactParseException e = Assert.Throws<ContactParseException>(() => PhoneNumber.Parse("tel:2024561111;phone-context=", Region.US));
        Assert.Equal(ReasonCode.PhoneNotANumber, e.Reason);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("abc", "phone.not-a-number")]
    [InlineData("+", "phone.not-a-number")]
    [InlineData("+999 123 4567", "phone.invalid-country-code")]
    [InlineData("+1 2024561111111111111", "phone.too-long")]
    [InlineData("011 1", "phone.too-short-after-idd")]
    public void Bad_text_fails_with_a_reason(string text, string reason)
    {
        ContactParseException e = Assert.Throws<ContactParseException>(() => PhoneNumber.Parse(text, Region.US));
        Assert.Equal(reason, e.Reason.Id);
    }

    [Fact]
    public void Text_longer_than_250_characters_is_too_long()
    {
        ContactParseException e = Assert.Throws<ContactParseException>(() => PhoneNumber.Parse(new string('1', 251), Region.US));
        Assert.Equal(ReasonCode.PhoneTooLong, e.Reason);
    }

    [Fact]
    public void A_region_with_no_data_is_reported_as_such()
    {
        OperationStatus status = PhoneNumber.TryParse("2024561111", Region.Parse("BV"), out _, out ParseDetail d);
        Assert.Equal(OperationStatus.InvalidData, status);
        Assert.Equal(ReasonCode.PhoneUnknownRegion, d.PrimaryReason);
        Assert.Equal("+12024561111", PhoneNumber.Parse("+1 202 456 1111", Region.Parse("BV")).E164);
    }

    [Fact]
    public void Offsets_point_at_the_number_in_the_input()
    {
        PhoneNumber p = PhoneNumber.Parse("call (202) 456-1111.", Region.US);
        Assert.Equal(6, p.Source.Start);
        Assert.Equal(13, p.Source.Length);
        OperationStatus status = PhoneNumber.TryParse("  +1 202 456 1111  ", Region.US, out PhoneNumber q, out ParseDetail d);
        Assert.Equal(OperationStatus.Done, status);
        Assert.Equal(2, q.Source.Start);
        Assert.Equal(15, q.Source.Length);
        Assert.Equal("number", Assert.Single(d.Fields).Name);
        Assert.Equal(q.Source, d.Fields[0].Span);
    }

    [Fact]
    public void Possibility_says_how_a_length_misses()
    {
        Assert.Equal(PhonePossibility.IsPossible, PhoneNumber.Parse("+1 202 456 1111").GetPossibility());
        Assert.Equal(PhonePossibility.TooShort, PhoneNumber.Parse("+1 202 456 11").GetPossibility());
        Assert.Equal(PhonePossibility.TooLong, PhoneNumber.Parse("+1 202 456 11111111111").GetPossibility());
        Assert.Equal(PhonePossibility.InvalidCountryCode, PhoneNumber.Create(999, "123456").GetPossibility());
        Assert.Equal(PhonePossibility.TooShort, PhoneNumber.Parse("+52 55 1234 567").GetPossibility());
        Assert.Equal(PhonePossibility.IsPossibleLocalOnly, PhoneNumber.Parse("+1 456 1234").GetPossibility());
        Assert.Equal(PhonePossibility.IsPossible, PhoneNumber.Parse("+44 20 7946 0958").GetPossibility(PhoneNumberType.FixedLine));
        Assert.False(PhoneNumberParser.Default.IsPossible(PhoneNumber.Parse("+1 202 456 11")));
    }

    [Fact]
    public void Validity_is_per_region_when_a_calling_code_is_shared()
    {
        PhoneNumber p = PhoneNumber.Parse("+1 604 555 0123");
        Assert.True(p.IsValid);
        Assert.Equal(Region.CA, p.Region);
        Assert.True(p.IsValidForRegion(Region.CA));
        Assert.False(p.IsValidForRegion(Region.US));
        Assert.False(p.IsValidForRegion(GB));
        Assert.False(PhoneNumber.Parse("+13 2024561111111", Region.Unknown).IsValid);
    }

    [Fact]
    public void Out_of_country_formats_follow_the_callers_dialling_prefix()
    {
        PhoneNumber uk = PhoneNumber.Parse("+44 20 7946 0958");
        Assert.Equal("011 44 20 7946 0958", uk.FormatOutOfCountry(Region.US));
        Assert.Equal("00 44 20 7946 0958", uk.FormatOutOfCountry(DE));
        Assert.Equal("0011 44 20 7946 0958", uk.FormatOutOfCountry(Region.Parse("AU")));
        Assert.Equal("+44 20 7946 0958", uk.FormatOutOfCountry(BR));
        Assert.Equal("020 7946 0958", uk.FormatOutOfCountry(GB));
        Assert.Equal("8~10 44 20 7946 0958", uk.FormatOutOfCountry(RU));
        Assert.Equal("+44 20 7946 0958", uk.FormatOutOfCountry(Region.Parse("BV")));

        PhoneNumber us = PhoneNumber.Parse("+1 202 456 1111 ext 123");
        Assert.Equal("1 (202) 456-1111 ext. 123", us.FormatOutOfCountry(Region.CA));
        Assert.Equal("00 1 202-456-1111 ext. 123", us.FormatOutOfCountry(GB));
    }

    [Fact]
    public void Mobile_dialing_keeps_or_drops_punctuation()
    {
        PhoneNumber uk = PhoneNumber.Parse("+44 20 7946 0958");
        Assert.Equal("020 7946 0958", uk.FormatForMobileDialing(GB, true));
        Assert.Equal("02079460958", uk.FormatForMobileDialing(GB, false));
        Assert.Equal("+44 20 7946 0958", uk.FormatForMobileDialing(Region.US, true));
        Assert.Equal("+442079460958", uk.FormatForMobileDialing(Region.US, false));

        // A Brazilian fixed line needs a carrier code from inside Brazil, and without one nothing useful can be given.
        PhoneNumber br = PhoneNumber.Parse("0 31 11 2345-6789", BR);
        Assert.Equal("0 31 (11) 2345-6789", br.FormatForMobileDialing(BR, true));
        Assert.Equal("", PhoneNumber.Parse("+55 11 2345-6789").FormatForMobileDialing(BR, true));
    }

    [Fact]
    public void Original_format_follows_how_the_number_was_written()
    {
        Assert.Equal("(202) 456-1111", PhoneNumber.Parse("(202) 456-1111", Region.US).FormatInOriginalFormat(Region.US));
        Assert.Equal("+44 20 7946 0958", PhoneNumber.Parse("+442079460958").FormatInOriginalFormat(GB));
        Assert.Equal("020 7946 0958", PhoneNumber.Parse("02079460958", GB).FormatInOriginalFormat(GB));
        Assert.Equal("20 7946 0958", PhoneNumber.Parse("2079460958", GB).FormatInOriginalFormat(GB));
        Assert.Equal("011 44 20 7946 0958", PhoneNumber.Parse("011 44 2079460958", Region.US).FormatInOriginalFormat(Region.US));

        // Formatting that would change a digit gives the text back as written.
        Assert.Equal("\uFF12\uFF10\uFF12\uFF14\uFF15\uFF16\uFF11\uFF11\uFF11\uFF11", PhoneNumber.Parse("\uFF12\uFF10\uFF12\uFF14\uFF15\uFF16\uFF11\uFF11\uFF11\uFF11", Region.US).FormatInOriginalFormat(Region.US));
    }

    [Fact]
    public void Carrier_codes_are_placed_by_the_regions_rule()
    {
        PhoneNumberParser p = PhoneNumberParser.Default;
        PhoneNumber br = PhoneNumber.Parse("0 31 11 2345-6789", BR);
        Assert.Equal("0 99 (11) 2345-6789", p.FormatNationalWithCarrierCode(br, "99"));
        Assert.Equal("0 31 (11) 2345-6789", p.FormatNationalWithPreferredCarrierCode(br, "FB"));
        Assert.Equal("(202) 456-1111", p.FormatNationalWithCarrierCode(PhoneNumber.Parse("2024561111", Region.US), "99"));
    }

    [Fact]
    public void Area_codes_and_geography()
    {
        PhoneNumberParser p = PhoneNumberParser.Default;
        PhoneNumber uk = PhoneNumber.Parse("+44 20 7946 0958");
        Assert.True(p.IsGeographical(uk));
        Assert.Equal(2, p.GetAreaCodeLength(uk));
        Assert.Equal(2, p.GetNationalDestinationCodeLength(uk));
        PhoneNumber ar = PhoneNumber.Parse("011 15-1234-5678", AR);
        Assert.Equal(3, p.GetAreaCodeLength(ar));
        Assert.Equal(0, p.GetAreaCodeLength(PhoneNumber.Parse("+1 800 356 9377")));
        Assert.False(p.IsGeographical(PhoneNumber.Parse("+1 800 356 9377")));
    }

    [Fact]
    public void National_only_numbers_cannot_be_dialled_abroad()
    {
        PhoneNumberParser p = PhoneNumberParser.Default;
        Assert.True(p.CanBeInternationallyDialled(PhoneNumber.Parse("+44 20 7946 0958")));
        Assert.True(p.CanBeInternationallyDialled(PhoneNumber.Create(999, "123456")));
    }

    [Fact]
    public void A_text_that_is_nearly_all_zeros_is_shown_as_written_in_the_formats_that_cannot_describe_it()
    {
        PhoneNumber p = PhoneNumber.Parse("00", Region.US);
        Assert.Equal(0UL, p.NationalNumber);
        Assert.Equal("00", p.Format(PhoneFormat.National));
        Assert.Equal("+100", p.Format(PhoneFormat.E164));
        Assert.Equal("+100", p.Canonical);
        Assert.False(p.IsValid);
    }

    [Fact]
    public void Preferred_international_prefix_wins_over_a_plain_one()
    {
        Assert.Equal("8~10 44 20 7946 0958", PhoneNumber.Parse("+44 20 7946 0958").FormatOutOfCountry(RU));
    }

    [Fact]
    public void Full_length_beats_local_only_when_both_are_listed_for_a_region()
    {
        // Albania lists six digits as local-only for fixed lines but six-digit premium-rate numbers exist: the number keeps its zero-free form.
        PhoneNumber p = PhoneNumber.Parse("0900123", Region.Parse("AL"));
        Assert.False(p.ItalianLeadingZero);
        Assert.Equal(PhoneNumberType.PremiumRate, p.Type);
    }

    [Fact]
    public void National_prefix_is_null_when_a_region_has_none()
    {
        PhoneNumberParser p = PhoneNumberParser.Default;
        Assert.Equal("0", p.GetNationalPrefix(GB));
        Assert.Equal("1", p.GetNationalPrefix(Region.US));
        Assert.Null(p.GetNationalPrefix(Region.Parse("AC")));
        Assert.Null(p.GetNationalPrefix(Region.Parse("BV")));
    }

    [Fact]
    public void Every_region_has_valid_examples_of_the_right_type()
    {
        PhoneNumberParser p = PhoneNumberParser.Default;
        int regions = 0;
        foreach (Region region in p.Metadata.Regions)
        {
            regions++;
            foreach (PhoneNumberType type in p.GetSupportedTypes(region))
            {
                PhoneNumber? example = p.GetExample(region, type);
                Assert.True(example is not null, region.Code + " " + type);
                Assert.True(p.IsValidForRegion(example.Value, region), region.Code + " " + type + " " + example.Value.E164);
                PhoneNumberType actual = p.GetNumberType(example.Value);
                Assert.True(actual == type || actual == PhoneNumberType.FixedLineOrMobile, region.Code + " " + type + " is " + actual);
            }
        }

        Assert.Equal(245, regions);
    }

    [Fact]
    public void Non_geographic_calling_codes_have_examples_and_no_region()
    {
        PhoneNumberParser p = PhoneNumberParser.Default;
        int nonGeographic = 0;
        foreach (int code in p.Metadata.CallingCodes)
        {
            if (p.Metadata.GetRegions(code).Count > 0)
            {
                continue;
            }

            nonGeographic++;
            PhoneNumber? example = p.GetNonGeographicExample(code);
            Assert.NotNull(example);
            Assert.True(p.IsValid(example.Value));
            Assert.True(p.IsNonGeographic(example.Value));
            Assert.True(p.GetRegion(example.Value).IsUnknown);
        }

        Assert.Equal(9, nonGeographic);
    }

    [Fact]
    public void Shared_calling_codes_list_the_main_region_first()
    {
        PhoneMetadata md = PhoneMetadata.Default;
        Assert.Equal(Region.US, md.GetRegions(1)[0]);
        Assert.Equal(GB, md.GetRegions(44)[0]);
        Assert.Contains(Region.CA, md.GetRegions(1));
        Assert.Equal(44, md.GetCallingCode(GB));
        Assert.Equal(0, md.GetCallingCode(Region.Parse("BV")));
    }

    [Fact]
    public void Create_builds_a_number_from_its_parts()
    {
        PhoneNumber p = PhoneNumber.Create(44, "2079460958", "12");
        Assert.Equal("+442079460958;ext=12", p.Canonical);
        Assert.True(p.IsValid);
        Assert.Throws<ArgumentOutOfRangeException>(() => PhoneNumber.Create(0, "123456"));
        Assert.Throws<ArgumentException>(() => PhoneNumber.Create(44, "12a"));
    }

    [Fact]
    public void Keys_ignore_punctuation_and_scripts_and_equality_uses_the_canonical_form()
    {
        PhoneNumber a = PhoneNumber.Parse("(202) 456-1111 x 5", Region.US);
        PhoneNumber b = PhoneNumber.Parse("+1.202.456.1111 ext. 5");
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(PhoneKey.Compute(a, KeyVersion.V1), PhoneKey.Compute(b, KeyVersion.V1));
        Assert.Equal("+12024561111x5", a.Key);
        Assert.True(Contacts.SameKey(a, PhoneNumber.Parse("202 456 1111 ext 5", Region.US)));
        Assert.Equal(string.Empty, PhoneKey.Compute(default, KeyVersion.V1));
    }

    [Fact]
    public void Json_writes_the_canonical_form_and_reads_it_back()
    {
        PhoneNumber p = PhoneNumber.Parse("+1 202 456 1111 ext 5");
        string json = JsonSerializer.Serialize(p, ContactJsonContext.Default.PhoneNumber);
        Assert.Equal("\"+12024561111;ext=5\"", json);
        Assert.Equal(p, JsonSerializer.Deserialize(json, ContactJsonContext.Default.PhoneNumber));
    }

    [Fact]
    public void Parse_many_gives_one_result_per_input_in_order()
    {
        PhoneParseResult[] results = [.. PhoneNumberParser.Default.ParseMany(["(202) 456-1111", "nope", "020 7946 0958"], Region.US)];
        Assert.Equal(3, results.Length);
        Assert.Equal(OperationStatus.Done, results[0].Status);
        Assert.Equal(OperationStatus.InvalidData, results[1].Status);
        Assert.Equal(ReasonCode.PhoneNotANumber, results[1].Detail.PrimaryReason);
    }

    [Fact]
    public void Raw_input_is_kept_for_strings_and_on_request_for_spans()
    {
        Assert.Equal("(202) 456-1111", PhoneNumber.Parse("(202) 456-1111", Region.US).RawInput);
        Assert.Null(PhoneNumber.Parse("(202) 456-1111".AsSpan(), Region.US).RawInput);
        PhoneNumberParser.Default.TryParse("(202) 456-1111", Region.US, keepRawInput: true, out PhoneNumber p, out _);
        Assert.Equal("(202) 456-1111", p.RawInput);
        Assert.Equal(CountryCodeSource.FromDefaultCountry, p.CountryCodeSource);
    }

    [Fact]
    public void Results_do_not_depend_on_the_current_culture()
    {
        System.Globalization.CultureInfo saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            Assert.Equal("+18003569377", PhoneNumber.Parse("1-800-flowers", Region.US).E164);
            Assert.Equal("+18003569377", PhoneNumber.Parse("1-800-FLOWERS", Region.US).E164);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Contacts_normalize_detects_phones_and_keeps_extensions()
    {
        ContactValue v = Contacts.Normalize("+1 (202) 456-1111 ext. 9");
        Assert.Equal(ContactKind.Phone, v.Kind);
        Assert.Equal("+12024561111;ext=9", v.Canonical);
        Assert.Equal("+12024561111x9", v.Key);
    }

    // --- user-supplied metadata -------------------------------------------------------------------------------------

    private const string TinyMetadata = """
        <phoneNumberMetadata><territories>
          <territory id="ZA" countryCode="27" internationalPrefix="00" nationalPrefix="0">
            <availableFormats><numberFormat pattern="(\d{2})(\d{3})(\d{4})"><leadingDigits>[1-9]</leadingDigits><format>$1 $2 $3</format></numberFormat></availableFormats>
            <generalDesc><nationalNumberPattern>[1-9]\d{8}</nationalNumberPattern></generalDesc>
            <fixedLine><possibleLengths national="9"/><exampleNumber>101234567</exampleNumber><nationalNumberPattern>1\d{8}</nationalNumberPattern></fixedLine>
            <mobile><possibleLengths national="9"/><exampleNumber>712345678</exampleNumber><nationalNumberPattern>7\d{8}</nationalNumberPattern></mobile>
          </territory>
        </territories></phoneNumberMetadata>
        """;

    private static PhoneMetadata Load(string xml) => PhoneMetadata.LoadXml(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    [Fact]
    public void Supplied_metadata_drives_a_parser()
    {
        PhoneNumberParser parser = new(Load(TinyMetadata));
        Region za = Region.Parse("ZA");
        Assert.Equal(OperationStatus.Done, parser.TryParse("012 345 6789", za, false, out PhoneNumber p, out _));
        Assert.Equal(PhoneNumberType.FixedLine, parser.GetNumberType(p));
        Assert.Equal("+27 12 345 6789", parser.Format(p, PhoneFormat.International));
        Assert.EndsWith("345 6789", parser.Format(p, PhoneFormat.National), StringComparison.Ordinal);
        Assert.Equal(OperationStatus.InvalidData, parser.TryParse("+1 202 456 1111", za, false, out _, out ParseDetail d));
        Assert.Equal(ReasonCode.PhoneInvalidCountryCode, d.PrimaryReason);
        Assert.Equal("+27712345678", parser.GetExample(za, PhoneNumberType.Mobile)!.Value.E164);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<phoneNumberMetadata><territories><territory id=\"ZA\" countryCode=\"27\"")]
    [InlineData("<other/>")]
    [InlineData("<phoneNumberMetadata><territories/></phoneNumberMetadata>")]
    [InlineData("<phoneNumberMetadata><territories><territory id=\"ZA\" countryCode=\"0\"><generalDesc><nationalNumberPattern>1</nationalNumberPattern></generalDesc></territory></territories></phoneNumberMetadata>")]
    [InlineData("<phoneNumberMetadata><territories><territory id=\"zz9\" countryCode=\"27\"><generalDesc><nationalNumberPattern>1</nationalNumberPattern></generalDesc></territory></territories></phoneNumberMetadata>")]
    [InlineData("<phoneNumberMetadata><territories><territory id=\"ZA\" countryCode=\"27\"><generalDesc><nationalNumberPattern>(?=1)</nationalNumberPattern></generalDesc></territory></territories></phoneNumberMetadata>")]
    [InlineData("<phoneNumberMetadata><territories><territory id=\"ZA\" countryCode=\"27\"><generalDesc><nationalNumberPattern>(1</nationalNumberPattern></generalDesc></territory></territories></phoneNumberMetadata>")]
    [InlineData("<phoneNumberMetadata><territories><territory id=\"ZA\" countryCode=\"27\"><generalDesc><nationalNumberPattern>1{99999}</nationalNumberPattern></generalDesc></territory></territories></phoneNumberMetadata>")]
    [InlineData("<phoneNumberMetadata><territories><territory id=\"ZA\" countryCode=\"27\"><fixedLine><possibleLengths national=\"x\"/></fixedLine><generalDesc><nationalNumberPattern>1</nationalNumberPattern></generalDesc></territory></territories></phoneNumberMetadata>")]
    public void Malformed_metadata_is_refused_cleanly(string xml)
    {
        Assert.Throws<InvalidDataException>(() => Load(xml));
    }

    [Fact]
    public void Duplicate_regions_and_deeply_nested_patterns_are_refused()
    {
        string territory = "<territory id=\"ZA\" countryCode=\"27\"><generalDesc><nationalNumberPattern>1</nationalNumberPattern></generalDesc></territory>";
        Assert.Throws<InvalidDataException>(() => Load("<phoneNumberMetadata><territories>" + territory + territory + "</territories></phoneNumberMetadata>"));
        string nested = new string('(', 200) + "1" + new string(')', 200);
        Assert.Throws<InvalidDataException>(() => Load("<phoneNumberMetadata><territories><territory id=\"ZA\" countryCode=\"27\"><generalDesc><nationalNumberPattern>" + nested + "</nationalNumberPattern></generalDesc></territory></territories></phoneNumberMetadata>"));
    }

    [Fact]
    public void Entity_declarations_are_never_expanded()
    {
        string xml = "<!DOCTYPE x [<!ENTITY e \"1234\">]><phoneNumberMetadata><territories><territory id=\"ZA\" countryCode=\"27\"><generalDesc><nationalNumberPattern>&e;</nationalNumberPattern></generalDesc></territory></territories></phoneNumberMetadata>";
        Assert.Throws<InvalidDataException>(() => Load(xml));
    }

    [Fact]
    public void A_null_stream_is_an_argument_error()
    {
        Assert.Throws<ArgumentNullException>(() => PhoneMetadata.LoadXml(null!));
        Assert.Throws<ArgumentNullException>(() => new PhoneNumberParser(null!));
    }
}
