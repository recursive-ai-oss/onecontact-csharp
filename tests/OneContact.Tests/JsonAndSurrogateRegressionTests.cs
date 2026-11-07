using System.Text.Json;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Serialization;
using RecursiveAI.OneContact.Text.Idna;
using RecursiveAI.OneContact.Web;

namespace RecursiveAI.OneContact.Tests;

// One minimized case per defect found by the ill-formed UTF-16 sweep and per JSON round-trip defect.
public class JsonAndSurrogateRegressionTests
{
    [Theory]
    [InlineData("\uD800")]
    [InlineData("\uDC00")]
    [InlineData("\uDC00\uD800")]
    [InlineData("a.\uD800")]
    [InlineData("\uFFFF")]
    public void Public_suffix_list_takes_ill_formed_text_as_data(string domain)
    {
        // Must not throw. A lone surrogate reads as U+FFFD (a USVString), which no rule matches, so the default rule "*" applies.
        _ = PublicSuffixList.GetPublicSuffix(domain);
        _ = PublicSuffixList.GetRegistrableDomain("www." + domain);
        _ = PublicSuffixList.IsKnownTopLevel(domain);
    }

    [Theory]
    [InlineData("\uD800")]
    [InlineData("a\uDC00b.com")]
    [InlineData("\uDC00\uD800.example")]
    public void Uts46_ToUnicode_maps_a_lone_surrogate_to_a_disallowed_replacement(string domain)
    {
        bool ok = Uts46.TryToUnicode(domain, Uts46Options.None, out string result);
        Assert.False(ok);
        Assert.Contains("\uFFFD", result, StringComparison.Ordinal);
        Assert.False(Uts46.TryToAscii(domain, Uts46Options.None, out _));
    }

    [Fact]
    public void Uts46_ToUnicode_survives_punycode_that_decodes_to_a_surrogate()
    {
        Assert.True(Punycode.TryEncode([0xD800], out string encoded));
        bool ok = Uts46.TryToUnicode("xn--" + encoded, Uts46Options.None, out string result);
        Assert.False(ok);
        Assert.True(string.IsNullOrEmpty(result) || !result.Any(char.IsSurrogate));
    }

    [Fact]
    public void Address_with_a_lone_surrogate_in_its_language_tag_is_data()
    {
        PostalAddress a = PostalAddress.Create(Region.Parse("JP"), locality: "Tokyo", language: "\uD800");
        Assert.NotNull(a.Validate());
        Assert.NotNull(a.GetLabelLines());
    }

    [Fact]
    public void Web_address_with_ill_formed_text_is_refused_or_replaced_never_thrown()
    {
        // WHATWG URL: input is converted to scalar values first, so a lone surrogate becomes U+FFFD, which a host refuses and a path percent-encodes.
        Assert.False(WebAddress.TryParse("http://exa\uD800mple.com/", out _));
        Assert.True(WebAddress.TryParse("http://example.com/a\uD800b", out WebAddress w));
        Assert.Contains("%EF%BF%BD", w.Canonical, StringComparison.Ordinal);
    }

    private static readonly JsonSerializerOptions DictionaryOptions = new() { Converters = { new PhoneNumberJsonConverter() } };

    private static T RoundTrip<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info, out string json)
    {
        json = JsonSerializer.Serialize(value, info);
        return JsonSerializer.Deserialize(json, info)!;
    }

    [Fact]
    public void Phone_json_writes_the_plus_sign_literally()
    {
        PhoneNumber p = PhoneNumber.Parse("+1 202 456 1111");
        PhoneNumber back = RoundTrip(p, ContactJsonContext.Default.PhoneNumber, out string json);
        Assert.Equal("\"+12024561111\"", json);
        Assert.Equal(p, back);
        Assert.Equal(p.Key, back.Key);

        // As a dictionary key too, and through the reflection-free options path.
        string dict = JsonSerializer.Serialize(new Dictionary<PhoneNumber, int> { [p] = 1 }, DictionaryOptions);
        Assert.Equal("{\"+12024561111\":1}", dict);
    }

    [Fact]
    public void Dictionary_keys_with_a_plus_and_an_ampersand_write_the_plus_literally()
    {
        WebAddress w = WebAddress.Parse("https://example.com/a+b?q=1&r=2");
        JsonSerializerOptions options = new() { Converters = { new WebAddressJsonConverter() } };
        string json = JsonSerializer.Serialize(new Dictionary<WebAddress, int> { [w] = 1 }, options);
        Assert.Contains("a+b?q=1&r=2", json, StringComparison.Ordinal);
        Assert.DoesNotContain("u002B", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, JsonSerializer.Deserialize<Dictionary<WebAddress, int>>(json, options)![w]);
    }

    [Fact]
    public void Email_json_writes_a_plus_tag_literally_and_keeps_other_escapes()
    {
        EmailAddress e = EmailAddress.Parse("J.Smith+crm@example.com");
        EmailAddress back = RoundTrip(e, ContactJsonContext.Default.EmailAddress, out string json);
        Assert.Equal("\"J.Smith+crm@example.com\"", json);
        Assert.Equal(e, back);
        Assert.Equal(e.Key, back.Key);
    }

    [Fact]
    public void Web_company_and_person_json_round_trip_and_stay_escaped_where_unsafe()
    {
        WebAddress w = WebAddress.Parse("https://example.com/a+b?q=1&r=2");
        WebAddress wb = RoundTrip(w, ContactJsonContext.Default.WebAddress, out string wjson);
        Assert.Equal(w, wb);
        Assert.Contains("a+b", wjson, StringComparison.Ordinal);
        Assert.Contains("\\u0026", wjson, StringComparison.Ordinal);

        CompanyName c = CompanyName.Parse("AT&T Inc <x>");
        CompanyName cb = RoundTrip(c, ContactJsonContext.Default.CompanyName, out string cjson);
        Assert.Equal(c, cb);
        Assert.Equal(c.Key, cb.Key);
        Assert.DoesNotContain('<', cjson);
        Assert.DoesNotContain('&', cjson);

        PersonName p = PersonName.Parse("Dr. María José García López");
        PersonName pb = RoundTrip(p, ContactJsonContext.Default.PersonName, out _);
        Assert.Equal(p, pb);
        Assert.Equal(p.Key, pb.Key);
    }

    [Theory]
    [InlineData("Pat McDonald\n350 fifth avenue apt 4B\nnew york ny 10118")]
    [InlineData("MCDONALD HOLDINGS\n10 n6w23001 main st\nSpringfield va 22162")]
    [InlineData("Pat Morgan\n10-123 1/2 rue principale\nmontréal QC  H3Z 2Y7")]
    public void Parsed_address_keeps_its_renderings_after_a_json_round_trip(string text)
    {
        PostalAddress a = PostalAddress.Parse("  " + text + "  ");
        PostalAddress b = RoundTrip(a, ContactJsonContext.Default.PostalAddress, out string json);

        Assert.Equal(a, b);
        Assert.Equal(a.Source, b.Source);
        Assert.Equal(a.Flags, b.Flags);
        Assert.Equal(a.Key, b.Key);
        foreach (AddressStyle style in Enum.GetValues<AddressStyle>())
        {
            foreach (AddressLayout layout in Enum.GetValues<AddressLayout>())
            {
                Assert.Equal(a.ToString(style, layout), b.ToString(style, layout));
                Assert.Equal(a.ToString(style, layout, true), b.ToString(style, layout, true));
            }
        }

        Assert.Equal(a.StreetDetail.Count, b.StreetDetail.Count);
        Assert.Equal(a.Locality, b.Locality);
        Assert.Equal(a.StreetLines, b.StreetLines);
        Assert.Equal(a.Country, b.Country);

        // The stored text is readable, and a second round trip does not change it.
        Assert.Contains("\"text\"", json, StringComparison.Ordinal);
        Assert.Equal(json, JsonSerializer.Serialize(b, ContactJsonContext.Default.PostalAddress));
    }

    [Fact]
    public void Address_json_with_a_text_that_no_longer_matches_its_canonical_form_is_refused()
    {
        string json = JsonSerializer.Serialize(PostalAddress.Parse("350 Fifth Avenue, New York NY 10118"), ContactJsonContext.Default.PostalAddress)
            .Replace("350 Fifth", "351 Fifth", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, ContactJsonContext.Default.PostalAddress));
    }
}
