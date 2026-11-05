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

## Visible text

`ContactExtractor.Sources` defaults to `Markup | Text`. The text half reads what a reader would see with rules, and with a model package referenced it adds what the rules left (below).

**What is read.** Text nodes of the OneHtml document, in order, split into lines at block-level elements, `<br>`, list items and table cells. Each run keeps its landmark (`header`, `footer`/`contentinfo`, `main`, and so on, from the accessibility tree) and its nearest heading, and every character maps back to a byte offset in the original page (a UTF-16 offset when the page was given as a string). Non-text elements (`script`, `style`, `template`, `noscript`, `iframe`, `svg`, `head`, form lists and the like) are never read.

**Cascade.** A node is skipped when its computed `display` is `none`, its `visibility` is `hidden` or `collapse`, it has the `hidden` attribute, a zero font size (also written without a unit), or an ancestor has `aria-hidden="true"` (that prunes the subtree). `visibility` is inherited, so a child that sets `visibility: visible` inside a hidden parent is read. Structured markup is read by the markup readers regardless of visibility, as it is meant for machines.

**Plain text.** `ExtractText` takes a CRM note or comment. Lines are the unit; a blank line or a line of dashes, underscores or equals signs starts a paragraph. Offsets are UTF-16 offsets of the string as given, line ends included.

**Candidates.** Emails (our parser), websites (our parser and the PSL; a bare domain needs an ICANN suffix and must not look like a file name), phones (the core matcher at `Valid` leniency when the page or caller region is known, plus the shape rules for labelled numbers and `+` numbers), US/CA addresses (the rule parser over block-bounded windows) and names only where a label (`Name:`, `Contact person:`) makes them certain. The region is never guessed: an address without a region stays unnormalized with `address.country-required`, and an unlabelled national phone without a region is dropped.

**Confidence** is fixed per rule: emails 0.9 (0.8 when decoded), phones 0.65–0.85, websites 0.6–0.85, addresses 0.7, labelled names 0.8, Cloudflare emails 0.9.

## The span model

When a model package that carries a contact-span model is referenced (`ContactModels.Spans`, `docs/models.md`), the text half also runs it over the visible text, in pieces of up to 40 lines or 3,000 characters. Rules first: the model's spans are considered only after every rule candidate has been taken, and a span that touches any character a rule took is dropped, so a rule's field is never replaced, shortened or split.

- A model span becomes a field of the same kinds the rules make: `person` a name, `company` an organization (the grouping decides whether it names the record or its employer, as for a `Company:` label), `address`, `phone`, `email`, `website`. It must have a mean probability of at least 0.3 (chosen on development blocks, see below).
- The core parsers still decide: a model email or website that does not parse (a website needs a registrable domain) is not a field, and a model phone is kept only when the numbering plan accepts it (and a region is known). A model address is parsed by the rules and then the address model like any other free-text address; nothing is invented.
- Such a field has `Decision` = `DecisionSource.Model` and `Confidence` = the model's (halved when the value could not be normalized, as for every field); every other field says `Rule`. Its `Source` is `Text`.
- **Blocks.** The model also says where a contact block begins. Grouping treats a block start as a hard edge: fields on or after the start line are not put in a group with fields before it. The layout rules (containers, landmarks, headings, four lines, a second email or address) still apply as before, so the model can only split a group, never join two.
- **Without a model package** extraction is rules and markup as before, and `ExtractResult.TextModelPackage` names `RecursiveAI.OneContact.Models.Tier1` when the text half ran, so a caller can see what would add text spans. Nothing is downloaded.

Development-block rows (field F1 of `ExtractText` with a US default region, exact span / overlap, 6,000 blocks, rules only → rules + model): address 43.9 → 96.6 / 46.2 → 99.2; company 0 → 93.2 / 0 → 96.8; person 0 → 95.2 / 0 → 97.4; phone 96.6 → 97.0 / 97.9 → 98.4; email 99.3 → 99.3; website 99.7 → 99.7. The rules find no names or companies without a label. The confidence cut was chosen on these blocks: from 0.0 to 0.3 scores are within 0.2 points, 0.6 loses a fifth of the company names and 0.75 most addresses.

## Obfuscation

Each decoded field carries `Deobfuscated`; its offsets span the text as written (the obfuscated form).

- Token substitution: `[at]`, `(at)`, `{at}`, `-at-`, ` at `, `AT`, and `[dot]`, `(dot)`, ` dot ` for the dot. A bare ` at ` counts only when a dot word also appears, so "meet me at example.com" is not an email. Decoded addresses need a real ICANN suffix.
- Entity-encoded letters, digits, `@` and `.`.
- Cloudflare `data-cfemail`: hex, the first byte is the XOR key for the rest.
- Reversed text: `direction: rtl` with `unicode-bidi: bidi-override` is read in display order (UAX #9).
- A value split by inline markup (`john<span>@</span>example.com`), hidden junk or zero-width characters.

## Text grouping

Fields group into a record when they share a qualifying container (`tr`, `article` and `address` always; other containers only when they hold two kinds of field), the same landmark and heading, and sit at most four lines apart. A second different email or address starts a new group. A group needs two kinds of field. A group with no certain name has kind `Unknown`; a `Name:` label makes a person, and a lone `Company:` label an organization. Unknown groups merge into a markup record that shares an email (role accounts count for organizations only), a website or an address (organizations only); a lone text field joins the single record that already holds its key. Disagreeing values stay, flagged `Disputed`.

## Pages of one site

The fourth input is a set of pages the caller supplies from one site. Nothing is fetched: the extractor reads what it is given, and the id of a page is only a label (normally its URL) that must be unique in the set.

```csharp
IReadOnlyList<ContactRecord> records = ContactExtractor.Extract(new[]
{
    ContactPage.FromHtml("https://example.com/", homeHtml),
    ContactPage.FromHtml("https://example.com/contact", contactBytes, contentType: "text/html"),
    ContactPage.FromText("note-17", crmNote),
});

SiteExtractResult site = new ContactExtractor { DefaultRegion = Region.US }.ExtractSite(pages);
SiteExtractResult streamed = await extractor.ExtractSiteAsync(pageStream);      // IAsyncEnumerable<ContactPage>
SiteExtraction run = extractor.BeginSite();  run.Add(page);  SiteExtractResult done = run.Complete();   // the engine form
```

**Each page is read on its own**, with the readers and region rules above, so a field's `Span` is in its page and in that page's `SitePage.OffsetUnit` (bytes for a page given as bytes, UTF-16 code units for a string or a text item). `ExtractedField.PageId` names the page. Pages are parsed and released one at a time; what stays is the values read (strings and spans), plus the input of a page that needs the site's region (below), held by reference until `Complete`.

**Records merge across pages** on the keys a single page uses: a name key, or a non-role email, or (organizations) a non-social website; phones never decide. Text groups of kind `Unknown` join as they do on a page (a shared email, an organization's website or address, a role account an organization lists). One addition applies to a page too: two organizations whose addresses share none are branches, and a shared website or email alone does not merge them (a shared name does). The result lists records in the order of their first page, then their markup; `ContactRecord.Pages` lists the pages that contributed and `Span` is the position on the first of them.

**Fields.** A value seen more than once for a record is one field; the other sightings are in `Duplicates`, each with its own page, source and span. Across pages the sighting in markup is the primary one and the same text found in the visible text of another page is its echo. A value found in text that no record claims joins the one record that holds it, from any page (a `mailto:` link, which only suggests a role, does not). Fields come in page order, then document order, and `Id` counts across the site.

**Disagreements** are kept, never picked. They are computed per (page, reader): a record's phones, emails or websites disagree when two of those groups each report values and share none (a stale number on one page against the one on another), and two different names are always a disagreement. All the values stay, flagged `Disputed`. Two different values on different pages are visible this way even when each page is consistent.

**The site's organization.** `SiteExtractResult.Organization` is the organization record found on the most pages (the first on a tie), which is what footer, contact and about pages describe together; markup and text sightings merge into it by the rules above. Other organizations (branches, partners) are records of their own.

**Persons are linked** (`WorksFor` / `HasMember`) only where the pages say so: through the markup or the same text block, as on a page, or when the person names an employer that is a record's name on any page. Sharing a page with an organization links nothing (a quoted customer is not staff); `Pages` still shows where a person came from.

**Region.** `SiteExtractResult.Region` is the one region the site's own content points to: the `lang` regions of its pages and the countries its markup gives for addresses, when they all agree. A page that has values needing a region (a national phone number, a free-text address) and names none is read a second time in that region; its values carry `RegionInferred` and its `SitePage.RegionFromSite` is true. A site with two regions has none; a region is never guessed, and a `DefaultRegion` the caller gave always stands in place of it.

**Errors and limits.** A default `ContactPage`, a duplicate id or a null set throws `ArgumentException` / `ArgumentNullException`; a page over OneHtml's parse limits throws as for a single page. Problems of a page (`MalformedJsonLd`, `LimitExceeded`) come back in `Issues` with the page id. The single-page limits apply per page; the number of pages is the caller's to bound.
