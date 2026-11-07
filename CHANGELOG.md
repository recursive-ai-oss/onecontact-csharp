# Changelog

All notable changes are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

First release. All packages share one version. `KeyVersion.V1` is frozen with this release: from here on, any change to a key rule ships as a new version.

### Added

**`RecursiveAI.OneContact` (the .NET base class library only)**

- Value types `PhoneNumber`, `EmailAddress`, `WebAddress`, `PostalAddress`, `PersonName` and `CompanyName`, and `Region`. Each has `Parse`/`TryParse` on strings and spans, a canonical form, a versioned comparison key (`KeyVersion`), flags, and offsets into the input. They implement `ISpanParsable<T>`, `ISpanFormattable` and `IEquatable<T>`, and ship System.Text.Json converters and a source-generated `ContactJsonContext`. `ParseDetail`, reason codes (`ReasonCode.Id` and `ReasonCode.Value`) and the `OperationStatus`-returning low level are available for every type; `Contacts.Normalize` and `Contacts.SameKey` work across types.
- Phones for every region in libphonenumber's metadata: parse, validate, possibility, number type, E.164, international, national, RFC 3966, out-of-country and mobile-dialing formats, `AsYouTypeFormatter`, a matcher with leniency levels, short numbers with their cost classes, and example numbers. A `PhoneNumberParser` loads user-supplied newer metadata. Results equal libphonenumber 9.0.43 on generated cases for every region and operation.
- Email: RFC 5322 and RFC 6531 addresses with display names and obsolete forms, IDNA (UTS #46) domains, provider-aware keys (Gmail, Outlook, Yahoo, Fastmail), role, free-provider and disposable flags, and typo suggestions that are never applied.
- Web addresses: the WHATWG URL Standard and the Public Suffix List (shipped unmodified), tracking parameters removed from the canonical form, a missing scheme assumed as `https` and flagged, social-profile flags, `IsWebsite`.
- Addresses: a structural layer for every region in libaddressinput's data (fields, layout, required fields, postcode patterns, `Validate()`); parsing, standardization to each postal authority's published style (`AddressStyle.Postal` and `AddressStyle.Display`, single-line and multi-line), structural validation and libpostal-style expansions for the United States, Canada, Mexico, Australia, the United Kingdom with Jersey, Guernsey and the Isle of Man, and the Latin-script countries of Europe (Austria, Belgium, Croatia, Czechia, Denmark, Estonia, Finland, France, Germany, Hungary, Iceland, Ireland, Italy, Latvia, Liechtenstein, Lithuania, Luxembourg, Malta, Monaco, the Netherlands, Norway, Poland, Portugal, Romania, San Marino, Slovakia, Slovenia, Spain, Sweden, Switzerland, Andorra and the Vatican). Country inference from the text when no country is given; `address.country-undetermined` when the text does not say. `PostalAddressJsonConverter(Region)` reads strings as free text of a country.
- Person names (titles, suffixes, particles, second family names, name order, several people in one string, casing restoration traced to offsets) and company names (ISO 20275 legal forms with ELF codes, DBA and c/o, a leading "The"). Nothing is inferred about gender, origin or salutation.
- Czech, Slovak and Croatian settlement lines between the street and the postcode line are read as the dependent locality, and a Bucharest sector as the dependent locality of the town; an addressee written in front of the street name without a comma (Hungarian and Nordic forms) is left to the model rather than read as part of the street name.
- `ContactModels` and the model seam: a referenced model package registers itself, the rules decide what is certain, and `ParseDetail` says per field whether a rule or a model decided it.

**`RecursiveAI.OneContact.Phones.Geo`**

- `PhoneGeocoder`, carrier and time-zone mappers, with the original-carrier caveat in their documentation.

**`RecursiveAI.OneContact.Models.Tier1` and `RecursiveAI.OneContact.Models`**

- Trained fast-tier models on OneNLP and OneModel: an address-field head, name-part and name-class heads, and a contact-span head for extraction from plain text. Tier 1 covers the United States and Canada; `Models` also covers the Tier 2 countries and is preferred when both are referenced. The weights are embedded; `Models` is trained on open national registers mixed with OpenStreetMap addresses (© OpenStreetMap contributors, ODbL 1.0), as its readme and notices state.

**`RecursiveAI.OneContact.Models.Accurate`**

- The same address-field, name and contact-span heads over OneNLP's distilled multilingual encoder (`RecursiveAI.OneNlp.Models.Multilingual.FrozenEncoder`), for running text. About 10 MB of head weights in the package; the encoder is a dependency. With a fast-tier package referenced too, the fast tier keeps normalizing single addresses and names and this package reads running text. `ContactModels.Register` gained an optional `normalizationRank`.

**`RecursiveAI.OneContact.Extraction`**

- `ContactExtractor` over HTML (string or bytes), plain text, or a set of pages from one site that you supply (it never fetches). Markup readers for JSON-LD, microdata, RDFa Lite, h-card, `mailto:`/`tel:` links and `<address>`; visible-text reading through OneHtml's cascade; decoding of obfuscated emails with a `Deobfuscated` flag; `Person` and `Organization` records with their flat fields, sources, byte offsets into the original HTML, confidence, normalized values and kept disagreements; grouping and key-based de-duplication; site merge; `ContactExtractor.Limits`.

### Notes

- Everything runs offline. No package opens a socket, resolves a name or downloads a file; a missing model is an error that names the package to add.
- `CultureInfo` never affects a result.
- `RecursiveAI.OneContact.Models.Accurate` is not part of this release.
