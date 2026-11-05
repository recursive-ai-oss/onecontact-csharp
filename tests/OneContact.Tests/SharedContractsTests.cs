using System.Buffers;
using System.Globalization;
using System.Text.Json;
using RecursiveAI.OneContact.Serialization;

namespace RecursiveAI.OneContact.Tests;

public class SharedContractsTests
{
    [Theory]
    [InlineData("us", "US")]
    [InlineData("De", "DE")]
    [InlineData("CA", "CA")]
    public void Region_parses_two_letters_in_either_case(string text, string code)
    {
        Region r = Region.Parse(text);
        Assert.Equal(code, r.Code);
        Assert.Equal(code, r.ToString());
        Assert.False(r.IsUnknown);
        Assert.Equal(r, Region.Parse(code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("U")]
    [InlineData("USA")]
    [InlineData("1A")]
    [InlineData("\u00DCS")]
    public void Region_refuses_anything_else_with_a_reason_code(string text)
    {
        Assert.False(Region.TryParse(text, out Region r));
        Assert.True(r.IsUnknown);
        ContactParseException e = Assert.Throws<ContactParseException>(() => Region.Parse(text));
        Assert.Equal(ReasonCode.InvalidRegion, e.Reason);
    }

    [Fact]
    public void Region_default_is_unknown_and_known_regions_are_not()
    {
        Assert.True(default(Region).IsUnknown);
        Assert.Equal("ZZ", Region.Unknown.Code);
        Assert.True(Region.US == Region.Parse("US"));
        Assert.True(Region.US != Region.CA);
    }

    [Fact]
    public void Region_never_depends_on_the_current_culture()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            // In Turkish, a culture-sensitive upper-casing of "i" would give a dotted capital I.
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            Assert.Equal("IT", Region.Parse("it").Code);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Region_json_is_the_code_string()
    {
        Assert.Equal("\"CA\"", JsonSerializer.Serialize(Region.CA, ContactJsonContext.Default.Region));
        Assert.Equal(Region.CA, JsonSerializer.Deserialize("\"ca\"", ContactJsonContext.Default.Region));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("\"CAN\"", ContactJsonContext.Default.Region));
    }

    [Fact]
    public void Reason_codes_compare_by_kind_and_number_and_have_stable_ids()
    {
        Assert.Equal("empty", ReasonCode.Empty.Id);
        Assert.Equal("phone.region-required", ReasonCode.PhoneRegionRequired.Id);
        Assert.True(default(ReasonCode).IsNone);
        Assert.Equal("none", default(ReasonCode).Name);
        Assert.True(ReasonCode.Empty != ReasonCode.InvalidRegion);
        Assert.Equal(ReasonCode.Empty, ReasonCode.Empty);
        Assert.Equal(ContactKind.Phone, ReasonCode.PhoneRegionRequired.Kind);
        Assert.Contains("region", ReasonCode.PhoneRegionRequired.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_detail_reports_issues_fields_and_the_primary_reason()
    {
        Assert.Empty(ParseDetail.Empty.Issues);
        Assert.True(ParseDetail.Empty.PrimaryReason.IsNone);

        ParseDetail d = new(
            [new ParseIssue(ReasonCode.NotImplemented, new SourceSpan(0, 1), IssueSeverity.Info), new ParseIssue(ReasonCode.Empty, new SourceSpan(2, 3))],
            [new ParsedField("locality", new SourceSpan(4, 5), 0.9f, DecisionSource.Model)]);
        Assert.Equal(ReasonCode.Empty, d.PrimaryReason);
        Assert.Equal("locality", d.Fields[0].Name);
        Assert.Equal(DecisionSource.Model, d.Fields[0].Source);
        Assert.Equal(9, d.Fields[0].Span.End);
        Assert.Throws<ArgumentNullException>(() => new ParseDetail(null!, []));
    }

    [Theory]
    [InlineData("j.smith@example.com", ContactKind.Email)]
    [InlineData("+1 202-456-1111", ContactKind.Phone)]
    [InlineData("www.example.com/x", ContactKind.Web)]
    [InlineData("https://example.com", ContactKind.Web)]
    public void Normalize_detects_the_kind(string text, ContactKind kind)
    {
        ContactValue v = Contacts.Normalize(text);
        Assert.Equal(kind, v.Kind);
        string expected = kind == ContactKind.Phone ? "+12024561111" : text;
        Assert.Equal(expected, v.Canonical);
        Assert.Equal(expected, v.ToString());
    }

    [Fact]
    public void Normalize_hands_back_the_typed_value()
    {
        ContactValue v = Contacts.Normalize("a@example.com");
        Assert.True(v.TryGetEmail(out EmailAddress e));
        Assert.Equal("a@example.com", e.Canonical);
        Assert.False(v.TryGetPhone(out _));
        Assert.False(v.TryGetWeb(out _));
        Assert.False(v.TryGetAddress(out _));
        Assert.False(v.TryGetPerson(out _));
        Assert.False(v.TryGetCompany(out _));
        Assert.Equal(ContactKind.Unknown, default(ContactValue).Kind);
    }

    [Fact]
    public void Normalize_needs_a_region_for_national_phone_numbers_and_refuses_unrecognizable_text()
    {
        ContactParseException e = Assert.Throws<ContactParseException>(() => Contacts.Normalize("(202) 456-1111"));
        Assert.Equal(ReasonCode.PhoneRegionRequired, e.Reason);
        Assert.Equal(ContactKind.Phone, Contacts.Normalize("(202) 456-1111", Region.US).Kind);

        e = Assert.Throws<ContactParseException>(() => Contacts.Normalize("hello there"));
        Assert.Equal(ReasonCode.UnrecognizedKind, e.Reason);
        e = Assert.Throws<ContactParseException>(() => Contacts.Normalize("   "));
        Assert.Equal(ReasonCode.Empty, e.Reason);
        Assert.Throws<ArgumentNullException>(() => Contacts.Normalize(null!));
        Assert.False(Contacts.TryNormalize(null, default, out _));
        Assert.Equal(OperationStatus.InvalidData, Contacts.TryNormalize("hello", default, out ContactValue none, out ParseDetail d));
        Assert.Equal(ContactKind.Unknown, none.Kind);
        Assert.Equal(ReasonCode.UnrecognizedKind, d.PrimaryReason);
    }

    [Fact]
    public void SameKey_needs_the_same_kind_and_a_nonempty_equal_key()
    {
        Assert.True(Contacts.SameKey("a@example.com", "a@example.com"));
        Assert.False(Contacts.SameKey("a@example.com", "b@example.com"));
        Assert.False(Contacts.SameKey("a@example.com", "not a contact"));
        Assert.False(Contacts.SameKey(null, null));
        Assert.False(Contacts.SameKey(Contacts.Normalize("x@example.com"), default));
        Assert.True(Contacts.SameKey(PhoneNumber.Parse("+12024561111"), PhoneNumber.Parse("+12024561111")));
    }
}
