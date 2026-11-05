# Extraction: structured markup

`RecursiveAI.OneContact.Extraction` reads the contact information a page states in structured markup and returns it as person and organization records. This document is what the markup readers do; the visible-text half is separate and arrives later (`ExtractionSources.Text` is reserved and ignored).

```csharp
IReadOnlyList<ContactRecord> records = ContactExtractor.Extract(html);          // string or bytes

var extractor = new ContactExtractor { Sources = ExtractionSources.Markup, DefaultRegion = Region.US };
ExtractResult result = extractor.ExtractHtml(bytes, contentType: "text/html; charset=utf-8");
foreach (ContactRecord r in result.Records) { /* r.Name, r.Phones, r.Emails, r.Websites, r.Addresses, r.Links */ }
```

Nothing is fetched and nothing is stored. The caller is responsible, under data-protection law, for what it extracts and what it does with it.

## Result model

| Type | What |
|---|---|
| `ExtractResult` | `Records`, every `Fields` flat in document order, `Ungrouped` fields, `Issues`, the `Sources` that ran and the `OffsetUnit` |
| `ContactRecord` | `Kind` (`Person`, `Organization`), `Fields`, accessors per `FieldKind`, `Links`, `Disagreements`, `Span`, `Confidence` |
| `ExtractedField` | `Kind`, `Source`, `Raw`, `Text`, `Span`, `Confidence`, `Flags`, `Normalized` (a `ContactValue`), `Issue`, `Facts`, `Label`, `RecordId`, `BlockId`, `Duplicates` |
| `FieldKind` | `Name`, `Organization` (the employer by name), `Phone`, `Email`, `Website`, `Address`. New kinds may be added; consumers keep a default branch |
| `FieldSource` | `JsonLd`, `Microdata`, `Rdfa`, `HCard`, `Link`, `AddressElement`, `Text` (reserved) |
| `RecordLink` | `WorksFor`, `HasMember`, `ParentOrganization`, `SubOrganization`; always stored in both directions |

**Raw and text.** `Raw` is the source slice at `Span`, exactly as written (character references and JSON escapes undecoded; newlines as HTML parsing normalizes them, CR LF and CR to LF). `Text` is what the normalizer was given: references and escapes decoded, markup removed, white space collapsed, address lines separated by `\n`. Where a value is put together from several pieces of markup (a given and a family name; address parts) the field carries `Composed`, `Raw` spans all the pieces and `Text` is the joined value.

**Offsets.** `Span` counts bytes of the original input when the input was bytes (or a `Document` OneHtml parsed from bytes), through OneHtml's offset map, so it is right for UTF-8 with multibyte text, other encodings and a byte order mark. For a string it counts UTF-16 code units of the string as given, carriage returns included. A `Document` parsed from a string has already had its newlines normalized, and its offsets are into that normalized text. `HasSpan` is false only for text the parser had to rewrite.

**Confidence.** Markup that names the property outright counts 1.0; a `mailto:`/`tel:` link counts 0.9 (a link only suggests the role); an `address` element 0.7; a value that could not be normalized counts half. A record's confidence is its best name's, or 0.5 without a name. No model is involved at this stage.

**Normalization.** Each value goes through the core type of its kind (`PersonName` or `CompanyName` for names by the record's kind, `PhoneNumber`, `EmailAddress`, `WebAddress`, `PostalAddress`). A value the core refuses stays as text with `Unnormalized` and the core's reason in `Issue`. `Facts` lists the fact flags of the normalized value by name.

- **Phones** need a region. An international number (`+`) needs none. Otherwise the region is the country of an address in the same record, else the region of the page's `lang` (`en-US` gives US; `en` gives none), else `ContactExtractor.DefaultRegion`. When one is used the field carries `RegionInferred`; when none exists the field stays unparsed with `phone.region-required`. A region is never guessed.
- **Free-text addresses** (a string `address`, an `address` element) are parsed in the same fallback region, and stay unparsed with `address.country-required` without one. Core parses free text only for the countries it has rules for.
- **Structured addresses** (schema.org `PostalAddress`, address parts of an h-card) are not parsed: the parts are taken as given. The country is the page's `addressCountry` (a code, a name the core country data knows, or a few common aliases such as USA and UK). Without one the address keeps no country and carries `address.country-required` as its issue.
- **Emails** are the `mailto:` address before any `?` header fields, percent-decoded (RFC 6068 §2); a link with several recipients gives one field each. **Phones from `tel:`** are percent-decoded with their parameters (RFC 3966 §3).

## Readers

Every reader reads hidden markup too: structured markup is meant for machines. A field inside markup hidden by a `hidden` attribute, `aria-hidden="true"`, or an inline `display:none` / `visibility:hidden` on it or an ancestor carries `Hidden`. Style sheets are not consulted.

- **JSON-LD.** `script` elements whose `type` is `application/ld+json` (media-type parameters ignored). A strict RFC 8259 reader keeps the offset of every value; a block with a syntax error (trailing comma, comment, raw control character, text after the value, nesting deeper than 64) is rejected whole with an `ExtractIssue` of kind `MalformedJsonLd`, and the other blocks and readers still run. Understood: `@type` (string or array; `schema:Person`, `http://schema.org/Person` and `Person` are the same), `@id` references (resolved across blocks, cycles end), `@graph`, top-level arrays, `@value`, `@list`, `@set`. Contexts are not expanded: a context that renames terms is not honoured.
- **Microdata.** Top-level items are elements with `itemscope` and no `itemprop`. Property values follow the HTML standard's rules (`meta` content, `href` of links, `src` of media, `data`/`meter` value, `time` datetime, otherwise the text). `itemref` is not followed.
- **RDFa Lite.** `vocab`, `typeof`, `property`, `resource`, `prefix`, for schema.org only (the nearest `vocab` names schema.org, or the type is written `schema:Name` or as a schema.org address). Values: `content`, then `href`/`src`/`datetime`, then `resource`, then text.
- **h-card.** microformats2 (`h-card`, `p-`/`u-` property classes) and classic hCard (`vcard` with `fn`, `org`, `adr`, `tel`, `email`, `url`, `street-address`, `locality`, `region`, `postal-code`, `country-name`), with the implied name and url of a link card, `abbr[title]`, and the `value` child of a `tel`. A card nested as another card's `p-org` is that card's organization and no card of its own. A card is an organization when it names itself as its own `org` (`fn org`) or its name carries a legal form; otherwise a person.
- **Links.** `a`/`area` with `mailto:` or `tel:` (scheme case-insensitive; entity-encoded schemes arrive decoded from the HTML parser). `Label` holds the link text when it differs from the value. A link inside a structured item joins that item's record; one that a structured reader already took as a property is not read twice.
- **Address elements.** `address` elements that are not a structured property. The contact links inside are read as links; the rest of the text is the address. `BlockId` (the element's index in the document) lets later grouping tie the loose fields of one block together.

**Types.** Organizations are schema.org's `Organization` hierarchy as schema.org lists it (`LocalBusiness`, `Store`, `Restaurant`, `Corporation`, ...). A type outside it is not read as an organization, and a non-contact item (`Article`, `Product`) is searched for records inside it.

## Grouping and de-duplication

Records follow the markup's nesting. Organization `employee`/`member` give person records linked `HasMember`/`WorksFor`; a person's `worksFor` (object form) gives an organization record, and always an `Organization` field with the name at the name's own position (text form gives the field only); `parentOrganization`, `subOrganization`, `department` give `ParentOrganization`/`SubOrganization` links. A `contactPoint` or `location` adds its phone, email, url and address to the record that holds it (`Label` carries the `contactType`). Direct address parts on a record form its address. Records come in the order their markup begins.

A record is merged into an earlier one of the same kind when they share a name key, or a contact key: an email address that is not a role account, or (organizations) a website that is not a social profile. Phone numbers never decide: a main line is shared by the people behind it. Merged records keep every field. Within a record a value read twice (same kind, same normalized key, or the same text when unnormalized) is one field; the other sightings are in `Duplicates`, each with its own source and span.

## Disagreements

Nothing is silently picked. `ContactRecord.Disagreements` lists, per kind, fields that do not fit together: two different names, or phone/email/website values from different sources whose sets share nothing (a stale JSON-LD number against the one in the visible link). All the values stay in `Fields`, flagged `Disputed`. Addresses and employers are not compared: two spellings of one address have different keys, and a person can have several employers.

## Limits

JSON nesting 64; microdata, RDFa and h-card item nesting 32; 200,000 item visits and 100,000 fields per page, after which the result holds what was read and an `ExtractIssue` of kind `LimitExceeded` says so. OneHtml's own parse limits apply (`HtmlLimitExceededException` propagates). The readers use explicit stacks, so deeply nested HTML does not exhaust the call stack. A core parser that throws on a hostile value does not cost the page: the field is kept unnormalized.
