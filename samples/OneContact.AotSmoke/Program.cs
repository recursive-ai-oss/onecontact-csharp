using System.Buffers;
using System.Text.Json;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Phones;
using RecursiveAI.OneContact.Phones.Geo;
using RecursiveAI.OneContact.Addresses.Expansion;
using RecursiveAI.OneContact.Addresses.Standardization;
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

// Web addresses: the URL parser, UTS #46 on a non-ASCII host (compiled tables), the Public Suffix List and the tracking-parameter list.
WebAddress shop = WebAddress.Parse("Shop.Bücher.example.co.uk/Contact?utm_source=x&id=1#top");
Check(shop.Canonical == "https://shop.xn--bcher-kva.example.co.uk/Contact?id=1", "web canonical form");
Check(shop.RegistrableDomain == "example.co.uk" && shop.Key == "example.co.uk/contact?id=1", "web registrable domain and key");
Check(WebAddress.Parse("https://github.com/octocat").Flags.HasFlag(WebFacts.SocialGitHub), "web social flag");
Check(RecursiveAI.OneContact.Web.PublicSuffixList.GetRegistrableDomain("a.b.github.io") == "b.github.io", "public suffix list");
Check(Contacts.Normalize("www.example.com/x").Kind == ContactKind.Web, "normalize web");
PostalAddress parsed = PostalAddress.Parse("1 main street apt 4, springfield, va 22162");
Check(parsed.Canonical == "1 MAIN ST APT 4, SPRINGFIELD VA 22162", "address canonical form");
Check(parsed.GetKey(KeyVersion.V1) == "us|1 main street unit 4|springfield|va|22162", "address key reads the embedded dictionaries");
Check(AddressExpander.Expand("30 W 26th St")[0] == "30 west 26 street", "address expansion");
Check(parsed.ToString(AddressStyle.Display, AddressLayout.SingleLine) == "1 Main St Apt 4, Springfield, VA 22162" && parsed.Validate().Count == 0 && parsed.StreetDetail.Count == 1, "address rendering and structural check");
Check(JsonSerializer.Deserialize(JsonSerializer.Serialize(parsed, ContactJsonContext.Default.PostalAddress), ContactJsonContext.Default.PostalAddress) == parsed, "address json round trip");
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

// Phones.Geo: each mapper reads its embedded table on first use.
PhoneNumber geoNumber = PhoneNumber.Parse("+1 650 253 0000");
Check(PhoneGeocoder.Default.GetDescription(geoNumber, "en") == "Mountain View, CA", "geocoder");
Check(PhoneGeocoder.Default.GetDescription(PhoneNumber.Parse("+49 30 123456"), "en", Region.US) == "Germany", "geocoder country name");
Check(PhoneTimeZoneMapper.Default.GetTimeZones(geoNumber).Contains("America/Los_Angeles"), "time zones");
Check(PhoneCarrierMapper.Default.GetSafeDisplayName(geoNumber, "en") == string.Empty, "carrier safe name");
Check(PhoneCarrierMapper.Default.GetName(PhoneNumber.Parse("+49 30 123456"), "en") == string.Empty, "carrier of a fixed line");

Console.WriteLine(failures == 0 ? "OneContact AOT smoke: ok" : $"OneContact AOT smoke: {failures} failure(s)");
return failures == 0 ? 0 : 1;
