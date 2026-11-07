# Parsing, formatting and JSON: what the types promise

**Parse overloads.** `PhoneNumber`, `PostalAddress`, `PersonName` and `CompanyName` all take their context as a second argument: `Parse(text, Region)` and `TryParse(text, Region, out value)` on strings and spans. For names the region is shorthand for `NameHints.ForCountry(region)`; `NameHints` also takes a language. `Region.Unknown` means no context.

**A successful result is usable.** `PostalAddress.Parse`/`TryParse` fail with `address.country-undetermined` when the country is neither given nor told by the content (a postcode form several countries share, no region name). The status-returning `PostalAddress.TryParse(span, out value, out detail)` is the low-level engine and keeps the partial reading: `Done`, the fields, `AddressFacts.CountryUndetermined`, `Country` unknown. `Contacts.Normalize` follows the happy path.

**Reasons.** `ReasonCode.Id` (`phone.too-short`) is the stable text for logs and wire; `ReasonCode.Value` is the same code as the `ReasonValue` enumeration for `switch`/`case`. The enumeration value is the kind times 65536 plus the number. Keep a default branch: later versions add members.

**Regions.** `Region` holds assigned ISO 3166-1 alpha-2 codes plus `AC`, `TA` and `XK`. `Region.Parse("ZZ")` is `Region.Unknown` and prints as `ZZ`; `UK` and other unassigned codes are refused (`invalid-region`). Whether a data set has an entry for an assigned region stays that data set's answer (`phone.unknown-region`, `Coverage`).

**Formats.** `PhoneNumber` implements `IFormattable`: `C` (default) is the canonical form, `E` E.164 without extension, `I` international, `N` national, `R` RFC 3966, in either letter case, so `$"{number:N}"` works. Any other specifier throws `FormatException`. The default value formats as `""`. The other value types define only `C`.

**Explicit metadata and models.** A `PhoneNumber` remembers the `PhoneNumberParser` that parsed it when that parser's metadata is not the built-in one. Models come from the registry (a referenced model package registers itself) unless you pass one: `ContactModels.Use(address, name, spans)` for a scope (`Use(null, null)` reads by rules only), or the `model` argument of `PostalAddress.TryParse(span, country, model, ...)`, `RuleAddressParser.TryParse(..., model, ...)`, `NameParsing.Classify`/`Split(..., hints, model)`. A `null` model means no model, not the registered one.

**JSON.** Each value type is a JSON string holding its canonical form, except where noted.
- `PhoneNumber`: canonical text. The attribute converter reads international numbers only; add `new PhoneNumberJsonConverter(Region.DE)` to `JsonSerializerOptions.Converters` to read national numbers too.
- `EmailAddress`: the canonical form, or `"Display Name" <canonical>` when the value has a display name, so the display name round-trips.
- `WebAddress`: the canonical form, which leaves out the fragment and tracking parameters; they do not come back. `CompanyName`, `PersonName`: canonical text; a legal form chosen with a hint is found again by parsing with the same hint.
- `PostalAddress`: one object shape (`canonical`, `country`, `language`, the fields, `streetLines`, `flags`, `sourceStart`, `sourceLength`; parsed addresses add `text` and `hint`). Names are fixed camelCase. A plain JSON string is also read, as free text; add `new PostalAddressJsonConverter(Region.ES)` to `JsonSerializerOptions.Converters` to read strings as text of a country (an object carries its own stored hint).
- `Region`: the two-letter code; `ZZ` is `Region.Unknown`.
Errors are `JsonException`s whose message names the reason id and the byte offset of the token, with the `ContactParseException` as `InnerException`; the serializer adds the path and line.

**Fragments.** `WebAddress` has no `Fragment` member. The canonical form, the key and `==` leave the fragment out, and a value does not keep it, so a property would be empty for every parsed value or would make a value carry text its canonical form hides. Keep the original text when you need a link that scrolls to a section; `WebFacts.FragmentRemoved` tells you there was one.

**Extraction names.** `ContactExtractor` has two kinds of members, told apart by their names. The static `Extract(...)` overloads are the happy path: they take HTML, bytes or a set of pages, use default options and return the records. An instance (`new ContactExtractor { ... }`) carries options (`Sources`, `DefaultRegion`, `Limits`) and has `ExtractHtml`, `ExtractText`, `ExtractSite`, `ExtractSiteAsync` and `Extract(Document)`, which return the fuller `ExtractResult` with flat fields and provenance. The static and instance `Extract` overloads differ in their parameter types and are never ambiguous; the format-specific instance names exist so a string is never guessed to be HTML or text.

**Limits.** Names and companies longer than `NameParsing.MaxInputLength` (1,000 UTF-16 code units) are refused (`person.too-long`, `company.too-long`). Extraction input limits are `ContactExtractor.Limits` (see extraction.md).
