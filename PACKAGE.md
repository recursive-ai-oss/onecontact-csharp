# RecursiveAI.OneContact

libphonenumber- and libpostal-class contact data for .NET.

A clean-room library for .NET 10 with no native code and no network, NativeAOT and trim clean. It turns a phone number, email address, web address, postal address, person name or company name into a canonical form, a versioned comparison key, flags and offsets back into the input.

```csharp
PhoneNumber phone = PhoneNumber.Parse("(202) 456-1111", Region.US);   // national input needs a region
string e164 = phone.E164;                                              // +12024561111
EmailAddress email = EmailAddress.Parse("J.Smith+crm@GoogleMail.com");
string canonical = email.Canonical;                                    // J.Smith+crm@googlemail.com
string key = email.Key;                                                // jsmith@gmail.com
PostalAddress address = PostalAddress.Parse("1 main street apt 4, springfield, va 22162");
string label = address.ToString(AddressStyle.Display, AddressLayout.SingleLine);  // 1 Main St Apt 4, Springfield, VA 22162
```

- **Phones** for every region: parse, validate, type, every format, as-you-type formatter, matcher with leniency levels, short numbers, examples.
- **Email** (RFC 5322, RFC 6531), **web addresses** (WHATWG URL, Public Suffix List), **addresses** (a structural layer for every country; parsing, postal-authority standardization, validation and expansion for the United States, Canada and the Tier 2 countries), **person and company names** (ISO 20275 legal forms).
- Free-text address and name parsing runs on rules. They never read a text in the wrong country and never print a value the text does not contain; text they cannot read is refused with a reason. Trained model packages for what the rules leave open follow in 1.1.
- Related packages: `RecursiveAI.OneContact.Phones.Geo` (geocoder, carrier and time zones) and `RecursiveAI.OneContact.Extraction` (people, organizations and contact fields from pages and text).

It depends on the .NET base class library only. Nothing opens a socket or downloads a file.

- Documentation: https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/docs/index.md
- Migrating from libphonenumber, libpostal, python-nameparser or probablepeople: https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/docs/migrating.md
- Changelog: https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/CHANGELOG.md
- Source: <https://github.com/recursive-ai-oss/onecontact-csharp>
- Home: <https://recursiveai.org/onecontact/>

Licensed `MIT OR Apache-2.0`. Copyright (c) Recursive AI, LLC. Third-party data and its notices: https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/THIRD-PARTY-NOTICES.md
