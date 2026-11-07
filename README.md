# OneContact

libphonenumber- and libpostal-class contact data for .NET.

A clean-room library for .NET 10 with no native code and no network, NativeAOT and trim clean. It turns one value of a known kind (a phone number, an email address, a web address, a postal address, a person's name, a company name) into its canonical form, a versioned comparison key, flags, and offsets back into the input. A second package finds those values in web pages and free text and groups them into person and organization records.

- **Phones** for every region: parse, validate, number type, every format, an as-you-type formatter, a matcher with leniency levels, short numbers, example numbers. Geocoder, carrier and time-zone mappers in an add-on.
- **Email** (RFC 5322, RFC 6531, IDNA domains) with provider-aware keys and role, free-provider and disposable flags.
- **Web addresses**: WHATWG URL and the Public Suffix List, tracking parameters removed.
- **Addresses**: a structural layer for every country (fields, layout, required fields, postcode patterns), and full free-text parsing, standardization to the national postal authority's style, validation and expansion for the United States, Canada, Mexico, Australia, the United Kingdom and Crown dependencies, the Latin-script countries of the EU, Switzerland, Norway, Iceland, Liechtenstein and the small European states.
- **Names**: Western-pattern person names (titles, suffixes, particles, order, several people in one string, casing restoration) and company names with ISO 20275 legal forms.
- **Extraction**: people, organizations, addresses, phones, emails and websites from pages (JSON-LD, microdata, RDFa Lite, h-card, `mailto:`/`tel:`, `<address>`, visible text) and from plain text.

Everything runs offline. Nothing in the packages opens a socket, resolves a name or downloads a file; a missing model is an error that names the package to add. The library keeps no state about what you parse, and `CultureInfo` never changes a result.

## Packages

All packages share one version.

| Package | Depends on | Contents |
|---|---|---|
| `RecursiveAI.OneContact` | the .NET base class library only | The value types, `Contacts`, keys and flags, JSON converters; phones, email, web addresses, addresses (structural layer for every country; rules for the Tier 1 and Tier 2 countries), names and companies |
| `RecursiveAI.OneContact.Phones.Geo` | core | Geocoder, carrier and time-zone mappers for phone numbers |
| `RecursiveAI.OneContact.Models.Tier1` | core, OneNLP, OneModel | The trained models for free-text address and name parsing in the United States and Canada (about 10 MB) |
| `RecursiveAI.OneContact.Models` | core, OneNLP, OneModel | The same for the United States, Canada and the Tier 2 countries (about 23 MB). Preferred over `.Models.Tier1` when both are referenced |
| `RecursiveAI.OneContact.Extraction` | core, OneHtml | `ContactExtractor`: markup readers, visible-text reading, obfuscation decoding, records, site merge. Markup and rules work without a model package; text spans use a model package when one is referenced |
| `RecursiveAI.OneContact.Models.Accurate` | | The same heads over a larger multilingual encoder. Not available yet |

The rules read what is certain (phone metadata, postcode patterns, region names, authority abbreviations, legal forms, titles, line structure). A model fills in only what the rules leave open and never overrules a rule hit, and `ParseDetail` says per field which of the two decided. Without a model package, text the rules cannot read is refused with a reason that names the package to add.

## Quick start

```
dotnet add package RecursiveAI.OneContact
```

The program below is [samples/OneContact.QuickStart](samples/OneContact.QuickStart/Program.cs), built with the solution and run before each release, so what you read here compiles.

```csharp
using RecursiveAI.OneContact;
using RecursiveAI.OneContact.Addresses;
using RecursiveAI.OneContact.Addresses.Standardization;
using RecursiveAI.OneContact.Names;
using RecursiveAI.OneContact.Phones;

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

// Names and companies.
PersonName person = PersonName.Parse("DR. MARÍA JOSÉ GARCÍA LÓPEZ", NameHints.ForCountry(Region.Parse("ES")));
Console.WriteLine($"{person.Prefix.Text} | {person.Given.Text} | {person.Family.Text} | {person.SecondFamily.Text} | key {person.Key}");
CompanyName company = CompanyName.Parse("Muster Aktiengesellschaft", NameHints.ForCountry(Region.Parse("DE")));
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

Low-level forms take spans and return `OperationStatus` plus a `ParseDetail`, and phones, addresses and names have batch forms. Explicit metadata (`PhoneMetadata.LoadXml`) and models (the `Models*` seam) can be supplied instead of the built-in ones.

## Coming from another library

[docs/migrating.md](docs/migrating.md) maps libphonenumber (Java and C#), libpostal (`parse_address`, `expand_address`) and python-nameparser / probablepeople calls to OneContact. It is a table of what to call, not a compatibility layer.

## Offline, NativeAOT and trimming

- Core references the .NET base class library only. No native code, no P/Invoke, no reflection-based serialization, no `Reflection.Emit`.
- Metadata, structural tables, authority rules, dictionaries and legal-form tables are compiled into the assembly as data and read on first use.
- `IsAotCompatible` and `IsTrimmable` are on, with warnings as errors. [samples/OneContact.AotSmoke](samples/OneContact.AotSmoke/Program.cs) is published with NativeAOT and exercises the library.
- JSON uses `Utf8JsonReader`/`Utf8JsonWriter` and a source-generated context (`ContactJsonContext`).
- The trained models are small OneNLP pipelines run by OneModel; no ONNX Runtime or other native runtime is involved.

## Results

The library is measured against the leaders it replaces. Rows below that carry a number have been measured; every other row says **measured at 1.0** and is filled in only from a measured run. Quality rows for trained parts are scored on private real-text sets and published only as aggregates, with paired-bootstrap 95% confidence intervals (1000 resamples).

### Exact agreement

| Part | Scope | Metric | OneContact | Reference | Verdict |
|---|---|---|---|---|---|
| Phones: parse, validate, type, every format, region, possibility, examples | every region and non-geographic code | mismatches on generated cases | 0 of about 470,000 | libphonenumber 9.0.43 | exact |
| Phones: as-you-type, matcher (every leniency), short numbers | every region | mismatches on generated cases | 0 of about 520,000 | libphonenumber 9.0.43 | exact |
| URL | WPT `urltestdata.json` | entries passing | 896 / 896 | WHATWG URL Standard conformance data | exact |
| URL | WPT `setters_tests.json`, `toascii.json` | entries passing | 278 / 278, 87 / 87 | same | exact |
| Internationalized domain names | `IdnaTestV2.json` | entries passing | 2676 / 2676 | UTS #46 conformance data | exact |
| Public Suffix List | `test_psl.txt` | tests passing | 78 / 78 | the list's own test file | exact |
| Email | our RFC 5322 / 6531 corpus | cases passing | 205 / 205 | the RFCs' examples | exact |
| Email | public isemail test list | cases passing | 164 / 164, with 35 documented expectation overrides | the list's expectations | exact |
| Address standardization, United States and Canada | the authorities' published examples | examples reproduced | 206 / 206 | USPS Publication 28, Canada Post (EN, FR) | exact |
| Address standardization, Tier 2 countries | the posts' and UPU's published examples, five example sets | examples reproduced | 316 / 316 (five example sets of 57, 54, 51, 65 and 89) | national postal authorities, UPU addressing sheets | exact |
| Structural address layer | every libaddressinput region, generated cases | mismatches | 0 of 32,883, with 4 documented intended differences | libaddressinput (released build) | exact, with the 4 differences |

The four intended differences of the structural layer: (1) we ship a newer data snapshot than the reference build's built-in table; (2) a country with no separate Latin layout uses its main layout, where the reference uses a default one; (3) any language tag with a `Latn` script subtag selects the Latin layout; (4) the convenience `Create` lowercases tags and maps `_` to `-`.

### Trained parts

| Part | Country or tier | Metric | OneContact (95% CI) | Reference | Verdict |
|---|---|---|---|---|---|
| Address parsing | US, CA | field F1, full-parse accuracy | measured at 1.0 | libpostal 1.1, Senzing's libpostal model | measured at 1.0 |
| Address parsing | each Tier 2 country, hinted and not | field F1, full-parse accuracy | measured at 1.0 | libpostal 1.1, Senzing's libpostal model | measured at 1.0 |
| Address keys and expansion | United States, Canada | same / different pair precision and recall | measured at 1.0 | libpostal `expand_address` | measured at 1.0 |
| Person names | each country | token accuracy, classification accuracy | measured at 1.0 | probablepeople, python-nameparser | measured at 1.0 |
| Company legal forms | each jurisdiction | detection and ELF-code accuracy | measured at 1.0 | cleanco | measured at 1.0 |
| Extraction, web pages | Tier 1, Tier 1+2 | field F1 per type, record F1 | measured at 1.0 | libphonenumber matcher, Microsoft.Recognizers.Text, spaCy | measured at 1.0 |
| Extraction, CRM and plain text | Tier 1, Tier 1+2 | field F1 per type, record F1 | measured at 1.0 | same | measured at 1.0 |

### Speed

| Operation | Comparator | Ratio (ours / comparator time) | Allocations | Verdict |
|---|---|---|---|---|
| Phone parse, validate, format, type, match | libphonenumber-csharp | measured at 1.0 | measured at 1.0 | measured at 1.0 |
| Email parse | MimeKit `MailboxAddress`, `System.Net.Mail.MailAddress` | measured at 1.0 | measured at 1.0 | measured at 1.0 |
| URL parse | AngleSharp `Url` | measured at 1.0 | measured at 1.0 | measured at 1.0 |
| Public suffix lookup | Nager.PublicSuffix | measured at 1.0 | measured at 1.0 | measured at 1.0 |
| Person name parse | NameParserSharp | measured at 1.0 | measured at 1.0 | measured at 1.0 |
| Address parse | US regex address parsers (at matched or better accuracy) | measured at 1.0 | measured at 1.0 | measured at 1.0 |
| Extraction | Microsoft.Recognizers.Text sequence finding | measured at 1.0 | measured at 1.0 | measured at 1.0 |

### Size

| Item | Size | libpostal |
|---|---|---|
| `RecursiveAI.OneContact.Models.Tier1` model | about 10.2 MB | about 2 GB of data |
| `RecursiveAI.OneContact.Models` model | about 22.8 MB | about 2 GB of data |
| Resident memory, load time | measured at 1.0 | |

How the rows are measured, and the evaluation sets behind them, are described in [docs/index.md](docs/index.md) under evaluation.

## Building

```
dotnet build -c Release
dotnet test --solution OneContact.slnx -c Release
```

Build output goes outside the source tree: set `ONECONTACT_ARTIFACTS` to a directory, or leave it unset to use `artifacts/`. How the code and its data fit together: [docs/architecture.md](docs/architecture.md). Where the tools keep data, models and evaluation sets: [docs/tools-environment.md](docs/tools-environment.md). All documentation: [docs/index.md](docs/index.md).

Licensed `MIT OR Apache-2.0`. Copyright (c) Recursive AI, LLC. Third-party data is listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Home: <https://recursiveai.org/onecontact/>
