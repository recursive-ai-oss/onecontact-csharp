using System.Text.Json;
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Extraction;
using RecursiveAI.OneContact.Extraction.Records;
using RecursiveAI.OneContact.Phones;
using RecursiveAI.OneContact.Phones.Geo;
using RecursiveAI.OneContact.Serialization;

// Phones: national input needs a region; international input does not.
PhoneNumber phone = PhoneNumber.Parse("(202) 456-1111", Region.US);
Console.WriteLine($"{phone.E164} | {phone.Format(PhoneFormat.International)} | {phone.Format(PhoneFormat.National)} | {phone.Type} | valid={phone.IsValid}");
Console.WriteLine(PhoneNumber.TryParse("2024561111", out _) ? "parsed" : "a national number without a region is refused");
Console.WriteLine($"{PhoneGeocoder.Default.GetDescription(PhoneNumber.Parse("+44 20 7946 0958"), "en")}");
foreach (PhoneNumberMatch m in PhoneNumber.FindAll("Call 202-456-1111 or +44 20 7946 0958 today", Region.US, PhoneMatchLeniency.Valid))
{
    Console.WriteLine($"found {m.RawString} at {m.Start}");
}

// Email: the canonical form keeps what the standard keeps; the key is the aggressive, versioned form.
EmailAddress email = EmailAddress.Parse("\"J. Smith\" <J.Smith+crm@GoogleMail.com>");
Console.WriteLine($"{email.Canonical} | key {email.Key} | {email.Flags}");

// Web addresses: WHATWG URL, IDNA and the Public Suffix List.
WebAddress web = WebAddress.Parse("www.Example.com/?utm_source=x");
Console.WriteLine($"{web.Canonical} | {web.RegistrableDomain} | key {web.Key} | {web.Flags}");

// Addresses: parse free text, write it back in the postal authority's style, validate its structure.
PostalAddress address = PostalAddress.Parse("1 main street apt 4, springfield, va 22162");
Console.WriteLine($"{address.Country.Code} | {address.Locality} | {address.PostalCode} | {address.Coverage}");
Console.WriteLine(address.ToString(AddressStyle.Postal, AddressLayout.SingleLine));
Console.WriteLine(address.ToString(AddressStyle.Display, AddressLayout.SingleLine));
Console.WriteLine($"{address.Validate().Count} structural issues");

// Text that does not say which country it is in is refused; give the country.
Console.WriteLine(PostalAddress.TryParse("Unter den Linden 77, 10117 Berlin", out _) ? "parsed" : "the country cannot be told: give one");
Console.WriteLine(PostalAddress.Parse("Unter den Linden 77, 10117 Berlin", Region.Parse("DE")).Country.Code);

// Names and companies.
PersonName person = PersonName.Parse("DR. MARÍA JOSÉ GARCÍA LÓPEZ", Region.Parse("ES"));
Console.WriteLine($"{person.Prefix.Text} | {person.Given.Text} | {person.Family.Text} | {person.SecondFamily.Text} | key {person.Key}");
CompanyName company = CompanyName.Parse("Muster Aktiengesellschaft", Region.Parse("DE"));
Console.WriteLine($"{company.Canonical} | {company.Base.Text} | {company.LegalForm?.ElfCode}");

// The same key means the same thing, whatever the kind.
Console.WriteLine(Contacts.Normalize("j.smith@example.com").Kind);
Console.WriteLine(Contacts.SameKey("(202) 456-1111", "+1 202-456-1111", Region.US));

// JSON goes through source-generated metadata, so it is trim and AOT safe.
Console.WriteLine(JsonSerializer.Serialize(email, ContactJsonContext.Default.EmailAddress));

// Extraction: contact records from a page or from plain text, every field with its offsets.
ExtractResult note = new ContactExtractor().ExtractText("Name: Jo Smoke\nTel: +1 202 555 0145\njo@example.com");
foreach (ContactRecord r in note.Records)
{
    Console.WriteLine($"{r.Kind}: {r.Name?.Text} | {r.Phones[0].Text} | {r.Emails[0].Text}");
}
