using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using RecursiveAI.OneContact.Keys;
using RecursiveAI.OneContact.Serialization;

namespace RecursiveAI.OneContact.Tests;

// The same contract holds for all six value types; each test runs against every one of them.
public class ValueTypeContractTests
{
    public static TheoryData<string, string> Samples => new()
    {
        { "phone", "+1 202 456 1111" },
        { "email", "someone@example.com" },
        { "web", "https://example.com/a" },
        { "address", "350 Fifth Avenue, New York" },
        { "person", "Ada Lovelace" },
        { "company", "Acme Holdings Ltd" },
    };

    [Theory]
    [MemberData(nameof(Samples))]
    public void Parses_and_reports_offsets_into_the_original_input(string kind, string sample)
    {
        string padded = "  " + sample + "\t";
        (string canonical, SourceSpan span) = kind switch
        {
            "phone" => Of(PhoneNumber.Parse(padded).Canonical, PhoneNumber.Parse(padded).Source),
            "email" => Of(EmailAddress.Parse(padded).Canonical, EmailAddress.Parse(padded).Source),
            "web" => Of(WebAddress.Parse(padded).Canonical, WebAddress.Parse(padded).Source),
            "address" => Of(PostalAddress.Parse(padded).Canonical, PostalAddress.Parse(padded).Source),
            "person" => Of(PersonName.Parse(padded).Canonical, PersonName.Parse(padded).Source),
            _ => Of(CompanyName.Parse(padded).Canonical, CompanyName.Parse(padded).Source),
        };

        Assert.NotEmpty(canonical);
        Assert.Equal(2, span.Start);
        Assert.Equal(sample.Length, span.Length);
        Assert.Equal(padded.Length - 1, span.End);
    }

    private static (string, SourceSpan) Of(string canonical, SourceSpan span) => (canonical, span);

    [Fact]
    public void Phone_contract() => Contract(PhoneNumber.Parse("+1 202 456 1111"), ContactJsonContext.Default.PhoneNumber);

    [Fact]
    public void Email_contract() => Contract(EmailAddress.Parse("someone@example.com"), ContactJsonContext.Default.EmailAddress);

    [Fact]
    public void Web_contract() => Contract(WebAddress.Parse("https://example.com/a"), ContactJsonContext.Default.WebAddress);

    [Fact]
    public void Address_contract() => Contract(PostalAddress.Parse("350 Fifth Avenue, New York"), ContactJsonContext.Default.PostalAddress);

    [Fact]
    public void Person_contract() => Contract(PersonName.Parse("Ada Lovelace"), ContactJsonContext.Default.PersonName);

    [Fact]
    public void Company_contract() => Contract(CompanyName.Parse("Acme Holdings Ltd"), ContactJsonContext.Default.CompanyName);

    private static readonly JsonSerializerOptions RelaxedOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static void Contract<T>(T value, JsonTypeInfo<T> json)
        where T : struct, IContactValue, ISpanParsable<T>, ISpanFormattable, IEquatable<T>, System.Numerics.IEqualityOperators<T, T, bool>
    {
        string text = value.Canonical;

        // Parsing through the generic interfaces gives the same value; the format provider is ignored.
        Assert.True(T.Parse(text, null) == value);
        Assert.True(T.TryParse(text.AsSpan(), null, out T viaSpan) && viaSpan.Equals(value));
        Assert.False(T.TryParse("", null, out T none));
        Assert.Equal("", none.Canonical);
        Assert.False(T.TryParse((string?)null, null, out _));

        // == compares canonical forms; default is the empty value.
        Assert.True(value != default);
        Assert.True(default(T) == default);
        Assert.Equal("", default(T).Canonical);
        Assert.Equal("", default(T).Key);
        Assert.Equal(value.GetHashCode(), T.Parse(text, null).GetHashCode());

        // Formatting: ToString and the canonical format letter agree, other letters are refused, TryFormat reports a short buffer.
        Assert.Equal(text, value.ToString());
        Assert.Equal(text, value.ToString("C", null));
        Assert.Throws<FormatException>(() => value.ToString("Z", null));
        char[] buffer = new char[text.Length];
        Assert.True(value.TryFormat(buffer, out int written, "C", null));
        Assert.Equal(text.Length, written);
        Assert.Equal(text, new string(buffer));
        Assert.False(value.TryFormat(new char[text.Length - 1], out int none2, default, null));
        Assert.Equal(0, none2);

        // Keys: the latest version is V1 today, and an undefined version is refused.
        Assert.Equal(KeyVersion.V1, KeyVersions.Latest);
        Assert.True(Contacts.SameKey(value, T.Parse(text, null)));
        Assert.False(Contacts.SameKey(default(T), default(T)));

        // JSON: a string holding the canonical form, through the source-generated context (no reflection).
        string serialized = JsonSerializer.Serialize(value, json);
        // An address read from text is written as an object that also holds the text (so its renderings keep their casing); the other types are plain strings.
        if (typeof(T) != typeof(PostalAddress))
        {
            Assert.Equal(JsonSerializer.Serialize(text, RelaxedOptions), serialized);
        }

        T back = JsonSerializer.Deserialize(serialized, json);
        Assert.True(back == value);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("\"\"", json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("12", json));
    }

    [Fact]
    public void Undefined_key_version_is_refused()
    {
        EmailAddress e = EmailAddress.Parse("a@example.com");
        Assert.Throws<ArgumentOutOfRangeException>(() => e.GetKey((KeyVersion)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => EmailKey.Compute(e, (KeyVersion)0));
        Assert.Equal(e.GetKey(KeyVersion.V1), EmailKey.Compute(e, KeyVersion.V1));
    }

    [Fact]
    public void Empty_input_throws_a_reason_coded_FormatException()
    {
        ContactParseException e = Assert.Throws<ContactParseException>(() => EmailAddress.Parse("  "));
        Assert.IsAssignableFrom<FormatException>(e);
        Assert.Equal(ReasonCode.Empty, e.Reason);
        Assert.Equal(ContactKind.Email, e.Kind);
        Assert.Contains("empty", e.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => EmailAddress.Parse((string)null!));
        Assert.False(EmailAddress.TryParse("  ", out EmailAddress none));
        Assert.True(none == default);
    }

    [Fact]
    public void National_phone_numbers_need_a_region_and_say_so()
    {
        ContactParseException e = Assert.Throws<ContactParseException>(() => PhoneNumber.Parse("(202) 456-1111"));
        Assert.Equal(ReasonCode.PhoneRegionRequired, e.Reason);
        Assert.Contains("region", e.Message, StringComparison.Ordinal);

        OperationStatus status = PhoneNumber.TryParse("(202) 456-1111", out _, out ParseDetail detail);
        Assert.Equal(OperationStatus.InvalidData, status);
        Assert.Equal(ReasonCode.PhoneRegionRequired, detail.PrimaryReason);
        Assert.Equal(new SourceSpan(0, 14), detail.Issues[0].Span);

        PhoneNumber ok = PhoneNumber.Parse("(202) 456-1111", Region.US);
        Assert.Equal("+12024561111", ok.Canonical);
        Assert.True(PhoneNumber.TryParse("(202) 456-1111", Region.US, out _));
        Assert.False(PhoneNumber.TryParse("(202) 456-1111", Region.Unknown, out _));
    }

    [Fact]
    public void Equality_is_on_canonical_form_and_keys_are_compared_explicitly()
    {
        EmailAddress a = EmailAddress.Parse("a@example.com");
        EmailAddress b = EmailAddress.Parse(" a@example.com");
        Assert.True(a == b);
        Assert.Equal(a, b);
        Assert.True(a.Source != b.Source);
        Assert.True(a != EmailAddress.Parse("A@example.com"));
        Assert.False(a.Equals((object?)null));
    }
}
