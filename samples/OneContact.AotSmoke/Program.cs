using System.Buffers;
using System.Text.Json;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Addresses;
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

EmailAddress email = EmailAddress.Parse("  a@example.com ");
Check(email.Source.Start == 2 && email.Source.Length == 13, "email offsets");
WebAddress web = WebAddress.Parse("https://example.com/");
Check(web.GetKey(KeyVersion.V1).Length > 0, "web key");

// Web addresses: the URL parser, UTS #46 on a non-ASCII host (compiled tables), the Public Suffix List and the tracking-parameter list.
WebAddress shop = WebAddress.Parse("Shop.Bücher.example.co.uk/Contact?utm_source=x&id=1#top");
Check(shop.Canonical == "https://shop.xn--bcher-kva.example.co.uk/Contact?id=1", "web canonical form");
Check(shop.RegistrableDomain == "example.co.uk" && shop.Key == "example.co.uk/contact?id=1", "web registrable domain and key");
Check(WebAddress.Parse("https://github.com/octocat").Flags.HasFlag(WebFacts.SocialGitHub), "web social flag");
Check(RecursiveAI.OneContact.Web.PublicSuffixList.GetRegistrableDomain("a.b.github.io") == "b.github.io", "public suffix list");
Check(Contacts.Normalize("www.example.com/x").Kind == ContactKind.Web, "normalize web");
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

// The structural address layer: field model, validation, layout, region data and the postal code pattern matcher.
PostalAddress address = PostalAddress.Create(Region.US, "CA", "Mountain View", postalCode: "94043", streetLines: ["1600 Amphitheatre Parkway"]);
Check(address.Validate().Count == 0, "address validates");
Check(address.GetLabelLines().Count == 2, "address label");
Check(address.Coverage == AddressCoverage.Full, "address coverage");
IReadOnlyList<AddressIssue> issues = PostalAddress.Create(Region.US, "CA", "Mountain View", postalCode: "9404", streetLines: ["1 Main"]).Validate();
Check(issues.Count == 1 && issues[0].Field == AddressField.PostalCode && issues[0].Kind == AddressIssueKind.InvalidFormat, "address issue");
Check(AddressRegions.TryGet(Region.US, out AddressRegionInfo? usInfo) && usInfo!.Subdivisions.Count > 50, "region data");
Check(AddressRegions.TryFindByName("united kingdom", out Region uk) && uk.Code == "GB", "region by name");
Check(PostalAddress.TryParse("1-1 Chiyoda", Region.Parse("JP"), out _, out ParseDetail refused) == OperationStatus.InvalidData && refused.PrimaryReason == ReasonCode.AddressFreeTextRefused, "free text refused");

// Email: display name, key rules, internationalized domain (the IDNA tables are embedded data), disposable list.
EmailAddress mailbox = EmailAddress.Parse("\"J. Smith\" <J.Smith+crm@GoogleMail.com>");
Check(mailbox.Canonical == "J.Smith+crm@googlemail.com" && mailbox.Key == "jsmith@gmail.com" && mailbox.DisplayName == "J. Smith", "email happy path");
Check(EmailAddress.Parse("info@B\u00fccher.example").Domain == "xn--bcher-kva.example", "email idn domain");
Check(EmailAddress.Parse("x@mailinator.com").Flags.HasFlag(EmailFacts.Disposable), "email disposable flag");

Console.WriteLine(failures == 0 ? "OneContact AOT smoke: ok" : $"OneContact AOT smoke: {failures} failure(s)");
return failures == 0 ? 0 : 1;
