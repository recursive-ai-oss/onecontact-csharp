using System.Buffers;
using System.Text.Json;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Phones;
using RecursiveAI.OneContact.Keys;
using RecursiveAI.OneContact.Serialization;

// Exercises the calls that exist today, so a NativeAOT publish proves they survive trimming. Exits non-zero on the first failure.
int failures = 0;

void Check(bool condition, string what)
{
    if (!condition)
    {
        failures++;
        Console.Error.WriteLine("FAILED: " + what);
    }
}

PhoneNumber phone = PhoneNumber.Parse("+1 202 456 1111");
Check(phone.Canonical.Length > 0, "phone parse");
Check(!PhoneNumber.TryParse("2024561111", out _), "national phone without a region is refused");
Check(PhoneNumber.TryParse("2024561111", Region.US, out _), "national phone with a region");

PhoneNumber us = PhoneNumber.Parse("(202) 456-1111", Region.US);
Check(us.E164 == "+12024561111", "phone e164");
Check(us.Format(PhoneFormat.International) == "+1 202-456-1111", "phone international format");
Check(us.Format(PhoneFormat.National) == "(202) 456-1111", "phone national format");
Check(us.Type == PhoneNumberType.FixedLineOrMobile, "phone type");
Check(us.IsValid && us.Key == "+12024561111", "phone validity and key");
Check(PhoneNumber.Parse("+44 20 7946 0958").Format(PhoneFormat.Rfc3966) == "tel:+44-20-7946-0958", "phone rfc3966");
Check(PhoneNumber.Parse("1-800-FLOWERS", Region.US).E164 == "+18003569377", "vanity phone");
PhoneNumberParser phones = PhoneNumberParser.Default;
Check(phones.GetExample(Region.Parse("DE"), PhoneNumberType.Mobile) is not null, "phone example");
Check(phones.TryParse("tel:+1-202-456-1111;ext=5", Region.Unknown, false, out PhoneNumber ext, out _) == OperationStatus.Done && ext.Extension == "5", "phone tel uri");

EmailAddress email = EmailAddress.Parse("  a@example.com ");
Check(email.Source.Start == 2 && email.Source.Length == 13, "email offsets");
WebAddress web = WebAddress.Parse("https://example.com/");
Check(web.GetKey(KeyVersion.V1).Length > 0, "web key");
Check(PostalAddress.Parse("1 Main St").Canonical.Length > 0, "address parse");
Check(PersonName.Parse("Ada Lovelace").Canonical.Length > 0, "person parse");
Check(CompanyName.Parse("Acme Ltd").Canonical.Length > 0, "company parse");

Check(Contacts.Normalize("a@example.com").Kind == ContactKind.Email, "normalize email");
Check(Contacts.SameKey("a@example.com", "a@example.com"), "same key");

OperationStatus status = EmailAddress.TryParse("   ", out _, out ParseDetail detail);
Check(status == OperationStatus.InvalidData && detail.PrimaryReason == ReasonCode.Empty, "reason code");

try
{
    _ = Region.Parse("USA");
    Check(false, "bad region throws");
}
catch (ContactParseException e)
{
    Check(e.Reason == ReasonCode.InvalidRegion, "bad region reason");
}

string json = JsonSerializer.Serialize(email, ContactJsonContext.Default.EmailAddress);
Check(json == "\"a@example.com\"", "json write");
Check(JsonSerializer.Deserialize(json, ContactJsonContext.Default.EmailAddress) == email, "json round trip");
Check(JsonSerializer.Deserialize("\"US\"", ContactJsonContext.Default.Region) == Region.US, "region json");

Console.WriteLine(failures == 0 ? "OneContact AOT smoke: ok" : $"OneContact AOT smoke: {failures} failure(s)");
return failures == 0 ? 0 : 1;
