using System.Buffers;
using System.Text.Json;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Keys;
using RecursiveAI.OneContact.Names;
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

EmailAddress email = EmailAddress.Parse("  a@example.com ");
Check(email.Source.Start == 2 && email.Source.Length == 13, "email offsets");
WebAddress web = WebAddress.Parse("https://example.com/");
Check(web.GetKey(KeyVersion.V1).Length > 0, "web key");
Check(PostalAddress.Parse("1 Main St").Canonical.Length > 0, "address parse");
Check(PersonName.Parse("Ada Lovelace").Canonical.Length > 0, "person parse");
Check(CompanyName.Parse("Acme Ltd").Canonical.Length > 0, "company parse");
PersonName spanish = PersonName.Parse("DR. MARÍA JOSÉ GARCÍA LÓPEZ", NameHints.ForCountry(Region.Parse("ES")));
Check(spanish.SecondFamily.Text == "López" && spanish.Key == "maria j garcialopez", "person parts and key");
CompanyName german = CompanyName.Parse("Muster Aktiengesellschaft", NameHints.ForCountry(Region.Parse("DE")));
Check(german.LegalForm?.ElfCode == "6QQB" && german.Canonical == "Muster AG", "company legal form from the ELF table");
Check(NameParsing.Split("Mr & Mrs John Smith").Persons.Count == 2, "several people");

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
