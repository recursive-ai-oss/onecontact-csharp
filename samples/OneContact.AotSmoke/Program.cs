using System.Buffers;
using System.Text.Json;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Records;
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

AsYouTypeFormatter typing = new(Region.US);
string typed = "";
foreach (char c in "2024561111")
{
    typed = typing.InputDigit(c);
}

Check(typed == "(202) 456-1111", "as-you-type formatting");
typing.Clear();
Check(typing.InputDigitAndRememberPosition('1') == "1" && typing.RememberedPosition == 1, "as-you-type remembered position");

PhoneNumberMatch[] found = [.. PhoneNumber.FindAll("Call 202-456-1111 or +44 20 7946 0958 today", Region.US, PhoneMatchLeniency.Valid)];
Check(found.Length == 2 && found[0].Start == 5 && found[0].RawString == "202-456-1111" && found[1].Number.CountryCode == 44, "phone matcher");
Check(PhoneNumber.FindAll("on 12/03/2021 at 10:30", Region.US, PhoneMatchLeniency.Possible).Any() == false, "matcher skips dates");
int nextIndex = 0;
long triesLeft = 100;
Check(PhoneNumberMatcher.Default.TryFindNext("x 650-253-0000", Region.US, PhoneMatchLeniency.ExactGrouping, ref nextIndex, ref triesLeft, out PhoneNumberMatch low) && low.End == 14, "matcher low level");

ShortNumberInfo shorts = ShortNumberInfo.Default;
PhoneNumber nineOneOne = PhoneNumber.Parse("+1911");
Check(shorts.IsValidForRegion(nineOneOne, Region.US) && shorts.GetExpectedCostForRegion(nineOneOne, Region.US) == ShortNumberCost.TollFree, "short number validity and cost");
Check(shorts.IsEmergencyNumber("911", Region.US) && shorts.ConnectsToEmergencyNumber("9115", Region.US) && !shorts.IsEmergencyNumber("9115", Region.US), "emergency numbers");
Check(shorts.GetExample(Region.US).Length > 0 && shorts.SupportedRegions.Count > 100, "short number examples");

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

// First Tier 2 group: a French, a Belgian, a Dutch and a German address, read by rules, written in both styles, and keyed.
PostalAddress fr = PostalAddress.Parse("Monsieur Jean DURAND\n25 rue des Fleurs\n33500 Libourne\nFrance");
Check(fr.Country.Code == "FR" && fr.ToString(AddressStyle.Postal, AddressLayout.Label, includeCountry: true) == "MONSIEUR JEAN DURAND\n25 RUE DES FLEURS\n33500 LIBOURNE\nFRANCE", "French address, postal style");
Check(fr.ToString(AddressStyle.Display, AddressLayout.SingleLine) == "Monsieur Jean Durand, 25 Rue des Fleurs, 33500 Libourne", "French address, display style");
PostalAddress be = PostalAddress.Parse("Dhr Paul Janssens\nKortijkstraat 37 b 1\n9800 Deinze\nBelgium");
Check(be.ToString(AddressStyle.Postal, AddressLayout.SingleLine) == "Dhr Paul Janssens, Kortijkstraat 37 bus 1, 9800 DEINZE", "Belgian address");
Check(PostalAddress.Parse("Adrianastraat 34\n6832 AM Arnhem").Country.Code == "NL", "Dutch address inferred");
PostalAddress es = PostalAddress.Parse("Calle Huertas 18, 4º, C\n29400 Marbella\nMálaga\nSpain");
Check(es.Country.Code == "ES" && es.ToString(AddressStyle.Postal, AddressLayout.Label, includeCountry: true) == "Calle Huertas 18, 4º, C\n29400 MARBELLA\nMÁLAGA\nSPAIN", "Spanish address, postal style");
PostalAddress gb = PostalAddress.Parse("10B Barry Jackson Tower, Estone Walk, Birmingham B6 5BA");
Check(gb.Country.Code == "GB" && gb.ToString(AddressStyle.Postal, AddressLayout.Label) == "10B Barry Jackson Tower\nEstone Walk\nBIRMINGHAM\nB6 5BA", "British address, postal style");
PostalAddress se = PostalAddress.Parse("Amelia Berlund\nKattstigen 5 lgh 1234\n182 46 Täby\nSweden");
Check(se.Country.Code == "SE" && se.ToString(AddressStyle.Postal, AddressLayout.Label, includeCountry: true) == "Amelia Berlund\nKattstigen 5 lgh 1234\n182 46 TÄBY\nSWEDEN", "Swedish address, postal style");
PostalAddress de = ParseIn("Wacholderweg 52a\n26133 Oldenburg", "DE");
Check(AddressKey.Compute(de, KeyVersion.V1) == AddressKey.Compute(ParseIn("Wacholderweg 52a, 26133 OLDENBURG", "DE"), KeyVersion.V1), "German address key");
Check(AddressExpander.Expand("Hauptstr", ExpansionLanguages.German).Contains("hauptstrasse"), "German expansion");
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

// Extraction of structured markup: JSON-LD, microdata, h-card and links, with byte offsets (OneHtml parses; nothing reflects).
string page = "<html lang=\"en-US\"><script type=\"application/ld+json\">{\"@type\":\"Organization\",\"name\":\"Smoke Co\",\"telephone\":\"(202) 555-0143\",\"employee\":{\"@type\":\"Person\",\"name\":\"Jo Smoke\"}}</script>" +
    "<div itemscope itemtype=\"https://schema.org/Person\"><span itemprop=\"name\">Micro Person</span></div><div class=\"h-card\"><span class=\"p-name\">Card Person</span></div><a href=\"mailto:hi@example.com\">hi</a></html>";
byte[] pageBytes = System.Text.Encoding.UTF8.GetBytes(page);
ExtractResult extracted = new ContactExtractor().ExtractHtml(pageBytes);
Check(extracted.Records.Count == 4 && extracted.OffsetUnit == OffsetUnit.Bytes, "extraction records");
Check(extracted.Records[0].Kind == ContactRecordKind.Organization && extracted.Records[0].Phones.Count == 1 && extracted.Records[0].Phones[0].Normalized.Canonical == "+12025550143", "extraction org phone");
Check(extracted.Records[1].Organization == extracted.Records[0], "extraction link");
Check(extracted.Ungrouped.Count == 1 && extracted.Ungrouped[0].Source == FieldSource.Link, "extraction link field");
ExtractedField first = extracted.Records[0].Name!;
Check(System.Text.Encoding.UTF8.GetString(pageBytes, first.Span.Start, first.Span.Length) == first.Raw, "extraction offsets");
Check(ContactExtractor.Extract("<script type=\"application/ld+json\">{oops</script>").Count == 0, "extraction rejects malformed json-ld");
// Phones.Geo: each mapper reads its embedded table on first use.
PhoneNumber geoNumber = PhoneNumber.Parse("+1 650 253 0000");
Check(PhoneGeocoder.Default.GetDescription(geoNumber, "en") == "Mountain View, CA", "geocoder");
Check(PhoneGeocoder.Default.GetDescription(PhoneNumber.Parse("+49 30 123456"), "en", Region.US) == "Germany", "geocoder country name");
Check(PhoneTimeZoneMapper.Default.GetTimeZones(geoNumber).Contains("America/Los_Angeles"), "time zones");
Check(PhoneCarrierMapper.Default.GetSafeDisplayName(geoNumber, "en") == string.Empty, "carrier safe name");
Check(PhoneCarrierMapper.Default.GetName(PhoneNumber.Parse("+49 30 123456"), "en") == string.Empty, "carrier of a fixed line");

// Extraction of visible text: the cascade hides a trap, obfuscated and reversed addresses are decoded, a note is read as plain text.
byte[] textBytes = System.Text.Encoding.UTF8.GetBytes("<html lang=\"en-US\"><body><div style=\"display:none\">trap@example.com</div><footer><p>Call +1 202 555 0144</p>" +
    "<p>john [at] example [dot] com</p><span style=\"direction:rtl;unicode-bidi:bidi-override\">moc.elpmaxe@enaj</span></footer></body></html>");
ExtractResult visible = new ContactExtractor().ExtractHtml(textBytes);
Check(visible.Of(FieldKind.Email).Count == 2 && visible.Of(FieldKind.Email).All(f => f.Flags.HasFlag(ExtractedFieldFacts.Deobfuscated) && f.Text != "trap@example.com"), "text extraction decodes and skips hidden text");
Check(visible.Of(FieldKind.Email).Select(f => f.Text).Order().SequenceEqual(["jane@example.com", "john@example.com"]), "text extraction values");
Check(visible.Records.Count == 1 && visible.Records[0].Kind == ContactRecordKind.Unknown && visible.Records[0].Phones.Count == 1, "text extraction groups a block");
ExtractedField decoded = visible.Of(FieldKind.Email).First(f => f.Text == "john@example.com");
Check(System.Text.Encoding.UTF8.GetString(textBytes, decoded.Span.Start, decoded.Span.Length) == "john [at] example [dot] com", "text extraction raw form");
ExtractResult note = new ContactExtractor().ExtractText("Name: Jo Smoke\nTel: +1 202 555 0145\njo@example.com");
Check(note.Records.Count == 1 && note.Records[0].Kind == ContactRecordKind.Person && note.Records[0].Name!.Text == "Jo Smoke" && note.OffsetUnit == OffsetUnit.Chars, "plain text extraction");

// A site: two pages merged, a stale phone kept and flagged, ids on every field, the stream form agreeing.
ContactPage[] sitePages =
[
    ContactPage.FromHtml("index", "<html lang=\"en-US\"><script type=\"application/ld+json\">{\"@type\":\"Organization\",\"name\":\"Site Co\",\"url\":\"https://siteco.example.com/\",\"telephone\":\"+1 202 555 0101\"}</script></html>"),
    ContactPage.FromHtml("contact", System.Text.Encoding.UTF8.GetBytes("<html><div class=\"h-card\"><span class=\"p-name p-org\">Site Co</span><a class=\"u-url\" href=\"https://siteco.example.com/\">w</a><a class=\"p-tel\" href=\"tel:+12025550177\">t</a></div></html>")),
];
SiteExtractResult siteResult = new ContactExtractor().ExtractSite(sitePages);
Check(siteResult.Records.Count == 1 && siteResult.Organization == siteResult.Records[0] && siteResult.Records[0].Pages.Count == 2, "site merges the same organization");
Check(siteResult.Records[0].Phones.Count == 2 && siteResult.Records[0].Disagreements.Any(d => d.Kind == FieldKind.Phone), "site keeps a stale phone");
Check(siteResult.Fields.All(f => f.PageId is "index" or "contact"), "site fields carry their page");
Check(ContactExtractor.Extract(sitePages).Count == 1, "site happy path");
SiteExtractResult streamed = await new ContactExtractor().ExtractSiteAsync(Pages(sitePages));
Check(streamed.Records.Count == 1 && streamed.Fields.Count == siteResult.Fields.Count, "site stream form");

static PostalAddress ParseIn(string text, string country)
{
    _ = PostalAddress.TryParse(text, Region.Parse(country), out PostalAddress value, out _);
    return value;
}

static async IAsyncEnumerable<ContactPage> Pages(ContactPage[] pages)
{
    foreach (ContactPage p in pages)
    {
        await Task.Yield();
        yield return p;
    }
}

Console.WriteLine(failures == 0 ? "OneContact AOT smoke: ok" : $"OneContact AOT smoke: {failures} failure(s)");
return failures == 0 ? 0 : 1;
