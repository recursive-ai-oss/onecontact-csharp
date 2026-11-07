# OneContact

libphonenumber- and libpostal-class contact data for .NET.

A clean-room library for .NET 10 with no native code and no network, NativeAOT and trim clean. It turns one value of a known kind (a phone number, an email address, a web address, a postal address, a person's name, a company name) into its canonical form, a versioned comparison key, flags, and offsets back into the input. A second part finds those values in web pages and free text.

> **In development, not yet released.** The shared contracts (value types, parse results, keys, JSON converters) are in place; the parsers behind them are being written. The API below is the planned shape and will change. No accuracy, speed or parity claims are made yet.

## Packages

All packages share one version.

| Package | Depends on | Contents |
|---|---|---|
| `RecursiveAI.OneContact` | the .NET base class library only | Value types, `Contacts`, keys and flags, JSON converters; phones, email, web addresses, addresses, names and companies |
| `RecursiveAI.OneContact.Phones.Geo` | core | Geocoder, carrier and time-zone mappers for phone numbers |
| `RecursiveAI.OneContact.Models.Tier1`, `.Models`, `.Models.Accurate` | core, OneNLP, OneModel | Trained models for free-text address and name parsing (not yet packable) |
| `RecursiveAI.OneContact.Extraction` | core, OneHtml, OneNLP | Finding people, organizations and their contact details in pages and text (not yet packable) |

## Planned API

Not implemented yet: this is the shape the parsers will fill in.

```csharp
using RecursiveAI.OneContact;

var phone = PhoneNumber.Parse("(202) 456-1111", Region.US);  // national input needs a region; the error says so
var email = EmailAddress.Parse("J.Smith+crm@GoogleMail.com");
string canonical = email.Canonical;                          // faithful to the standard
string key = email.Key;                                      // aggressive, versioned (KeyVersion)
ContactValue v = Contacts.Normalize("j.smith@example.com");  // finds out the kind
bool same = Contacts.SameKey(phone, PhoneNumber.Parse("+1 202-456-1111"));
```

## Building

```
dotnet build -c Release
dotnet test --solution OneContact.slnx -c Release
```

Build output goes outside the source tree: set `ONECONTACT_ARTIFACTS` to a directory, or leave it unset to use `artifacts/`. How the code and its data fit together: [docs/architecture.md](docs/architecture.md). Where the tools keep data, models and evaluation sets: [docs/tools-environment.md](docs/tools-environment.md).

Licensed `MIT OR Apache-2.0`. Copyright (c) Recursive AI, LLC. Third-party data is listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Home: <https://recursiveai.org/onecontact/>
