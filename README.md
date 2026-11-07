# OneContact

libphonenumber- and libpostal-class contact data for .NET.

A clean-room library for .NET 10 with no native code and no network, NativeAOT and trim clean. It turns one value of a known kind (a phone number, an email address, a web address, a postal address, a person's name, a company name) into its canonical form, a versioned comparison key, flags, and offsets back into the input. A second package finds those values in web pages and free text and groups them into person and organization records.

- **Phones** for every region: parse, validate, number type, every format, an as-you-type formatter, a matcher with leniency levels, short numbers, example numbers. Geocoder, carrier and time-zone mappers in an add-on.
- **Email** (RFC 5322, RFC 6531, IDNA domains) with provider-aware keys and role, free-provider and disposable flags.
- **Web addresses**: WHATWG URL and the Public Suffix List, tracking parameters removed.
- **Addresses**: a structural layer for every country (fields, layout, required fields, postcode patterns), and full free-text parsing, standardization to the national postal authority's style, validation and expansion for the United States, Canada, Mexico, Australia, the United Kingdom and Crown dependencies, the Latin-script countries of the EU, Switzerland, Norway, Iceland, Liechtenstein and the small European states.
- **Names**: Western-pattern person names (titles, suffixes, particles, order, several people in one string, casing restoration) and company names with ISO 20275 legal forms.
- **Extraction**: people, organizations, addresses, phones, emails and websites from pages (JSON-LD, microdata, RDFa Lite, h-card, `mailto:`/`tel:`, `<address>`, visible text) and from plain text.

Everything runs offline. Nothing in the packages opens a socket, resolves a name or downloads a file. The library keeps no state about what you parse, and `CultureInfo` never changes a result.

## Packages

All packages share one version.

| Package | Depends on | Contents |
|---|---|---|
| `RecursiveAI.OneContact` | the .NET base class library only | The value types, `Contacts`, keys and flags, JSON converters; phones, email, web addresses, addresses (structural layer for every country; rules for the Tier 1 and Tier 2 countries), names and companies |
| `RecursiveAI.OneContact.Phones.Geo` | core | Geocoder, carrier and time-zone mappers for phone numbers |
| `RecursiveAI.OneContact.Extraction` | core, OneHtml | `ContactExtractor`: markup readers, visible-text reading, obfuscation decoding, records, site merge |

Version 1.0 runs on rules and markup alone. The trained model packages (`RecursiveAI.OneContact.Models.Tier1`, `.Models` and `.Models.Accurate`) follow in 1.1, after OneNLP 1.0.

The rules read what is certain (phone metadata, postcode patterns, region names, authority abbreviations, legal forms, titles, line structure) and say so in `ParseDetail`. They never read a text in the wrong country and never print a value the text does not contain. Text they cannot read is refused with a reason: when only a trained model could fill a field, the reason names the package that does so, available from 1.1; when the country cannot be told, `address.country-undetermined` asks you to give one.

## Quick start

```
dotnet add package RecursiveAI.OneContact
```

The program below is [samples/OneContact.QuickStart](samples/OneContact.QuickStart/Program.cs), built with the solution and run before each release, so what you read here compiles.

```csharp
using RecursiveAI.OneContact;

// Phones: national input needs a region; international input does not.
PhoneNumber phone = PhoneNumber.Parse("(202) 456-1111", Region.US);
Console.WriteLine($"{phone.E164} | {phone.Format(PhoneFormat.International)} | {phone.Format(PhoneFormat.National)} | {phone.Type} | valid={phone.IsValid}");
Console.WriteLine(PhoneNumber.TryParse("2024561111", out _) ? "parsed" : "a national number without a region is refused");

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
```

It prints:

```
+12024561111 | +1 202-456-1111 | (202) 456-1111 | FixedLineOrMobile | valid=True
a national number without a region is refused
J.Smith+crm@googlemail.com | key jsmith@gmail.com | FreeProvider, HasDisplayName
https://www.example.com/ | example.com | key example.com | SchemeAssumed, TrackingParametersRemoved
US | springfield | 22162 | Full
1 MAIN ST APT 4, SPRINGFIELD VA 22162
1 Main St Apt 4, Springfield, VA 22162
0 structural issues
the country cannot be told: give one
DE
Dr. | María | García | López | key maria j garcialopez
Muster AG | Muster | 6QQB
Email
True
```

The sample also shows the matcher (`PhoneNumber.FindAll`), the geocoder from `.Phones.Geo`, JSON through the source-generated `ContactJsonContext`, and extraction:

```csharp
ExtractResult note = new ContactExtractor().ExtractText("Name: Jo Smoke\nTel: +1 202 555 0145\njo@example.com");
foreach (ContactRecord r in note.Records)
{
    Console.WriteLine($"{r.Kind}: {r.Name?.Text} | {r.Phones[0].Text} | {r.Emails[0].Text}");
}
// Person: Jo Smoke | +1 202 555 0145 | jo@example.com
```

For pages, `ContactExtractor.Extract(html)` returns the records and `ExtractHtml` returns every field with its source, confidence and byte offsets into the original HTML. Pages of one site merge with `ExtractSite`. Disagreeing values (a stale phone in markup, a different one in the text) are kept, each with its source. Extraction runs on what you give it and stores nothing; responsibility for lawful processing of the personal data it finds sits with the caller (see [docs/extraction.md](docs/extraction.md)).

Low-level forms take spans and return `OperationStatus` plus a `ParseDetail`; phones, names and emails also have batch forms (`PhoneNumberParser.ParseMany`, `NameParsing.SplitMany`, `EmailParser.ParseMany`), and addresses are parsed one at a time. The low-level address form keeps a partial reading when the country cannot be told (`AddressFacts.CountryUndetermined`), where `PostalAddress.Parse` refuses. Explicit metadata (`PhoneMetadata.LoadXml`; numbers parsed with it answer from it) can be supplied instead of the built-in tables.

## Coming from another library

[docs/migrating.md](docs/migrating.md) maps libphonenumber (Java and C#), libpostal (`parse_address`, `expand_address`) and python-nameparser / probablepeople calls to OneContact. It is a table of what to call, not a compatibility layer.

## Offline, NativeAOT and trimming

- Core references the .NET base class library only. No native code, no P/Invoke, no reflection-based serialization, no `Reflection.Emit`.
- Metadata, structural tables, authority rules, dictionaries and legal-form tables are compiled into the assembly as data and read on first use.
- `IsAotCompatible` and `IsTrimmable` are on, with warnings as errors. [samples/OneContact.AotSmoke](samples/OneContact.AotSmoke/Program.cs) is published with NativeAOT and exercises the library.
- JSON uses `Utf8JsonReader`/`Utf8JsonWriter` and a source-generated context (`ContactJsonContext`).

## Results

The library is measured against the leaders it replaces. Every number below was measured; the evaluation sets behind real-text rows are private and only aggregates are published.

### Exact agreement

| Part | Scope | Metric | OneContact | Reference | Verdict |
|---|---|---|---|---|---|
| Phones: parse, validate, type, every format, region, possibility, examples | every region and non-geographic code | mismatches on generated cases | 0 of 2,819,630 results (141,206 cases, 2 seeds) | libphonenumber 9.0.43 | exact |
| Phones: as-you-type, matcher (every leniency), short numbers | every region | mismatches on generated cases | 0 of 969,881 results (207,679 cases, 2 seeds) | libphonenumber 9.0.43 | exact |
| Phone geocoder, carrier and time zones (`Phones.Geo`) | every calling code | mismatches on generated cases | 0 of 1,159,521 cases (2 seeds) | libphonenumber geocoder 3.43, carrier 2.43 | exact |
| URL | WPT `urltestdata.json` | entries passing | 896 / 896 | WHATWG URL Standard conformance data | exact |
| URL | WPT `setters_tests.json`, `toascii.json` | entries passing | 278 / 278, 87 / 87 | same | exact |
| Internationalized domain names | `IdnaTestV2.json` | entries passing | 2676 / 2676 | UTS #46 conformance data | exact |
| Public Suffix List | `test_psl.txt` | tests passing | 78 / 78 | the list's own test file | exact |
| Email | our RFC 5322 / 6531 corpus | cases passing | 205 / 205 | the RFCs' examples | exact |
| Email | public isemail test list | cases passing | 164 / 164, with 35 documented expectation overrides | the list's expectations | exact |
| Address standardization, United States and Canada | the authorities' published examples | examples reproduced | 206 / 206 | USPS Publication 28, Canada Post (EN, FR) | exact |
| Address standardization, Tier 2 countries | the posts' and UPU's published examples, five example sets | examples reproduced | 316 / 316 (five example sets of 57, 54, 51, 65 and 89) | national postal authorities, UPU addressing sheets | exact |
| Structural address layer | every libaddressinput region, generated cases | mismatches | 0 of 32,883, with 4 documented intended differences | libaddressinput (released build) | exact, with the 4 differences |

Fuzzing: every parser and loader the packages ship (20 targets: phone strings, the matcher, as-you-type, phone and short-number metadata, email, web addresses, IDNA, addresses, names, companies, `Contacts.Normalize`, HTML and text extraction, the model-file loader that ships with the 1.1 packages, JSON converters and the compiled-table reader) was fuzzed for 600 seconds each with AFL++ and SharpFuzz. One bug turned up (a negative span length in the address reader) and is fixed with a regression test; the rerun of that target found none. See [fuzz/README.md](fuzz/README.md).

The four intended differences of the structural layer: (1) we ship a newer data snapshot than the reference build's built-in table; (2) a country with no separate Latin layout uses its main layout, where the reference uses a default one; (3) any language tag with a `Latn` script subtag selects the Latin layout; (4) the convenience `Create` lowercases tags and maps `_` to `-`.

### Honest refusal

The rules never read a text in the wrong country and never print a value the text does not contain; what they cannot read they refuse with a reason. Measured on 4,623 items of real pages (the United States, Canada, every Tier 2 country), with and without the country given: with it, 0 readings in a wrong country; without it, 1 of 3,098 readings with a country was in a wrong country (a Czech-labelled item whose text is Slovak) and 925 more were returned without a country rather than guessed; one standardized state abbreviation (`D.F.` written `CDMX`) was the only printed word not found in its input. Every cause is listed in [docs/maintainers/refusal-rows.md](docs/maintainers/refusal-rows.md).

### Speed

| Operation | Comparator | Ratio (ours / comparator time) | Allocations | Verdict |
|---|---|---|---|---|
| Phone parse, validate, format, type, match | libphonenumber-csharp | _to be filled from the 1.0 benchmark run_ | | |
| Email parse | MimeKit `MailboxAddress`, `System.Net.Mail.MailAddress` | _to be filled_ | | |
| URL parse | AngleSharp `Url` | _to be filled_ | | |
| Public suffix lookup | Nager.PublicSuffix | _to be filled_ | | |
| Person name parse | NameParserSharp | _to be filled_ | | |
| Address parse | US regex address parsers (at matched or better accuracy) | _to be filled_ | | |
| Extraction | Microsoft.Recognizers.Text sequence finding | _to be filled_ | | |

### Size

| Package | Size | libpostal |
|---|---|---|
| `RecursiveAI.OneContact` | 1.39 MB | about 2 GB of data |
| `RecursiveAI.OneContact.Phones.Geo` | 1.52 MB | |
| `RecursiveAI.OneContact.Extraction` | 0.21 MB | |

Trained-model quality rows (address parsing, names, legal forms and extraction against libpostal, probablepeople, python-nameparser, cleanco and spaCy) are published with the model packages in 1.1.

How the rows are measured is described in [docs/index.md](docs/index.md) under evaluation.

## Building

```
dotnet build -c Release
dotnet test --solution OneContact.slnx -c Release
```

Build output goes outside the source tree: set `ONECONTACT_ARTIFACTS` to a directory, or leave it unset to use `artifacts/`. How the code and its data fit together: [docs/architecture.md](docs/architecture.md). Where the tools keep data, models and evaluation sets: [docs/tools-environment.md](docs/tools-environment.md). All documentation: [docs/index.md](docs/index.md).

Licensed `MIT OR Apache-2.0`. Copyright (c) Recursive AI, LLC. Third-party data is listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Home: <https://recursiveai.org/onecontact/>
