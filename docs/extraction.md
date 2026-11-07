# Extraction

`RecursiveAI.OneContact.Extraction` reads the contact information a page states in structured markup, and the contact details in its visible text, and returns them as person and organization records. The first half of this document is what the markup readers do; the visible-text half starts at "Visible text".

```csharp
IReadOnlyList<ContactRecord> records = ContactExtractor.Extract(html);          // string or bytes

var extractor = new ContactExtractor { Sources = ExtractionSources.Markup, DefaultRegion = Region.US };
ExtractResult result = extractor.ExtractHtml(bytes, contentType: "text/html; charset=utf-8");
foreach (ContactRecord r in result.Records) { /* r.Name, r.Phones, r.Emails, r.Websites, r.Addresses, r.Links */ }
```

Nothing is fetched and nothing is stored. **Personal data:** names, emails, phones and addresses are personal data. Extraction runs offline on exactly what the caller passes in, and the library keeps no copy; responsibility for a lawful basis and for compliance with data-protection law (for example the GDPR) for what is extracted and what is done with it sits with the caller.

## Result model

| Type | What |
|---|---|
| `ExtractResult` | `Records`, every `Fields` flat in document order, `Ungrouped` fields, `Issues`, the `Sources` that ran and the `OffsetUnit` |
| `ContactRecord` | `Kind` (`Person`, `Organization`), `Fields`, accessors per `FieldKind`, `Links`, `Disagreements`, `Span`, `Confidence` |
| `ExtractedField` | `Kind`, `Source`, `Raw`, `Text`, `Span`, `Confidence`, `Flags`, `Normalized` (a `ContactValue`), `Issue`, `Facts`, `Label`, `RecordId`, `BlockId`, `Duplicates` |
| `FieldKind` | `Name`, `Organization` (the employer by name), `Phone`, `Email`, `Website`, `Address`. New kinds may be added; consumers keep a default branch |
| `FieldSource` | `JsonLd`, `Microdata`, `Rdfa`, `HCard`, `Link`, `AddressElement`, `Text` |
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

JSON nesting 64; microdata, RDFa and h-card item nesting 32; 200,000 item visits and 100,000 fields per page, after which the result holds what was read and an `ExtractIssue` of kind `LimitExceeded` says so. The input limits are `ContactExtractor.Limits` (`ExtractionLimits`: `MaxInputLength` 64 MiB, `MaxNesting` 4096, `MaxNodes` 4,194,304, `MaxFields` 100,000); an input past the first three throws `ContactExtractionException` (with `LimitName`, `Limit`, `Actual`, and the HTML parser's exception as `InnerException`), and past `MaxFields` the result holds what was read. The readers use explicit stacks, so deeply nested HTML does not exhaust the call stack. A core parser that throws on a hostile value does not cost the page: the field is kept unnormalized.

## Visible text

`ContactExtractor.Sources` defaults to `Markup | Text`. The text half reads what a reader would see with rules, and with a model package referenced it adds what the rules left (below).

**What is read.** Text nodes of the OneHtml document, in order, split into lines at block-level elements, `<br>`, list items and table cells. Each run keeps its landmark (`header`, `footer`/`contentinfo`, `main`, and so on, from the accessibility tree) and its nearest heading, and every character maps back to a byte offset in the original page (a UTF-16 offset when the page was given as a string). Non-text elements (`script`, `style`, `template`, `noscript`, `iframe`, `svg`, `head`, form lists and the like) are never read.

**Cascade.** A node is skipped when its computed `display` is `none`, its `visibility` is `hidden` or `collapse`, it has the `hidden` attribute, a zero font size (also written without a unit), or an ancestor has `aria-hidden="true"` (that prunes the subtree). `visibility` is inherited, so a child that sets `visibility: visible` inside a hidden parent is read. Structured markup is read by the markup readers regardless of visibility, as it is meant for machines.

**Plain text.** `ExtractText` takes a CRM note or comment. Lines are the unit; a blank line or a line of dashes, underscores or equals signs starts a paragraph. Offsets are UTF-16 offsets of the string as given, line ends included.

**Candidates.** Emails (our parser), websites (our parser and the PSL; a bare domain needs an ICANN suffix and must not look like a file name), phones (the core matcher at `Valid` leniency when the page or caller region is known, plus the shape rules for labelled numbers and `+` numbers), US/CA addresses (the rule parser over block-bounded windows) and names and organizations by the rules in "People and organizations by rules" below (a `Name:` or `Contact person:` label still makes a name certain). The region is never guessed: an address without a region stays unnormalized with `address.country-required`, and an unlabelled national phone without a region is dropped.

**Confidence** is fixed per rule: emails 0.9 (0.8 when decoded), phones 0.65–0.85, websites 0.6–0.85, addresses 0.7, labelled names 0.8, Cloudflare emails 0.9.

## The span model

**Plain text only.** The Tier 1 package registers its contact-span head, and `ExtractText` uses it. A web page (`Extract` over HTML) does not: scored on real pages, the head made the record and address rows worse than the rules alone, and no model may make a row worse than rules. Pages are read by the markup readers and the rules, with or without the package.

When a contact-span model is registered (`ContactModels.Spans`, `docs/models.md`), the text half also runs it over the visible text, in pieces of up to 40 lines or 3,000 characters. Rules first: the model's spans are considered only after every rule candidate has been taken, and a span that touches any character a rule took is dropped, so a rule's field is never replaced, shortened or split.

- A model span becomes a field of the same kinds the rules make: `person` a name, `company` an organization (the grouping decides whether it names the record or its employer, as for a `Company:` label), `address`, `phone`, `email`, `website`. It must have a mean probability of at least 0.4.
- The core parsers still decide: a model email or website that does not parse (a website needs a registrable domain) is not a field, and a model phone is kept only when the numbering plan accepts it (and a region is known). A model address is parsed by the rules and then the address model like any other free-text address; nothing is invented.
- Such a field has `Decision` = `DecisionSource.Model` and `Confidence` = the model's (halved when the value could not be normalized, as for every field); every other field says `Rule`. Its `Source` is `Text`.
- **Blocks.** The model also says where a contact block begins. Grouping treats a block start as a hard edge: fields on or after the start line are not put in a group with fields before it. The layout rules (containers, landmarks, headings, four lines, a second email or address) still apply as before, so the model can only split a group, never join two.
- **Without a registered span model** (the default) extraction is rules and markup. Nothing is downloaded.


## People and organizations by rules

The rules find a person or an organization in visible text, with or without a model, and with the same offsets and confidence as every other field. A name is only taken when something beside it says what it is; capitalized words alone are never enough. Job titles are evidence only and are never returned.

- **Person.** Evidence is an email address, phone number or address in the neighbouring lines; a job title beside it; a cue ("contact", "ask for", "signed", "from"); a closing line above ("Best regards,"); a name alone above a job line or a contact line; a single first name after a greeting ("Hi Lucile,"). The span includes an honorific in front and an ending such as `Jr.`, `Esq` or `CPA` behind (the convention of the development labels). Words of web chrome ("Customer Service", "Privacy Policy"), places, region codes and stop words are refused as names.
- **Organization.** A legal form the core company parser accepts (`Acme Widgets, Inc.`), extended over `dba` and `c/o` parts and cut at a connector word in running prose; the name after a copyright sign ("© 2026 Harbor Freight LLC."); an employer after a job title and "at"/"for"; a cue word plus a business word; the company line of a signature. A leading "The" is kept.
- **Side by side.** Two organizations in sibling inline elements do not merge into one name.
- **Markup agrees.** A text name equal to a name that a markup record already holds takes that record's kind, so "Harbor Freight" in a page's text is an organization when its JSON-LD says so.
- **Email.** An address that ends in a file extension (`logo@2x.png`) is not an email unless the ending is a real top-level domain.

### People in typed text

Text a person typed (a string, a note, a comment) is read with more rules than a web page, because the same shapes mean something else on a page: a byline, a testimonial, a product name. `ExtractText` and text items turn them on; a web page keeps only the rules above plus the ones marked "any input" here. Each needs a reason in the text itself; none builds a name from an email address or a domain.

- **A given name and a family name** (two or three words, the first a given name from the lists). Given names from the core parser's list and the 2,200 commonest US given names; family names from the 5,000 commonest (US Census 2010 and SSA lists, public domain, rendered into `NameListData` by `tools/data/names/extraction-lists.py`). Given names that are also everyday words ("Will", "Rose", "Grace") never start a name on their own. A sentence opener in front ("Yesterday Maria Lopez") is left out of the span.
- **A cue and a name the lists know.** A small word that takes a person as object ("to", "with", "by", "for", "from", "per", "via", "told", "met") counts when the first word is a given name or the last a family name. Stronger cues ("spoke with", "ask for", "attendees:", "referred by") count for any name.
- **A verb after the name.** Verbs of answering and writing ("replied", "wrote", "texted", "called") take a person as subject for any two or three capitalized words; other verbs ("said", "was", "will") need a name from the lists.
- **A list.** A name next to a name already found, with only commas, semicolons, "and" or "&" between them, or on the line below a name that stands alone on its line.
- **Lower case.** A given name and a family name in lower case count with a cue in front, a contact field beside them, a speech verb after them, or an email address that spells them.
- **Capitals.** The same rules apply to capitalized text. A label word in front ("CUSTOMER", "CLIENT") and a reference word or verb behind ("RE", "CALLED") are not part of the name.
- **Last, First.** `Lopez, Maria` at the start of a line or after a cue; it needs a given name second or a contact, a job title or a header label after it, and an opener ("Sure, Jim"), a place or a business word before the comma refuses it.
- **A first name alone** after a cue ("spoke with Dan", "Thank you, Dan.") or in front of a verb ("Sofia called"), for a common given name; lower case only after the strongest cues.
- **Plain text only:** a title in front ("Dr. Patel", "Mrs. Alvarez"; "Dr" without a dot after a capitalized word is a street; on a page a title alone is a byline or a testimonial), a leading label word ("CUSTOMER Shane Wilfley"), a verb or reference word at the back ("Elian Messer called"), the greetings "thanks", "good morning" and "thank you", a header label and colon ("From:", "Attendees:", "Ref:"), and an email address in the same or a neighbouring line whose local part spells a word of the name. On a page the same shapes are menus, bylines and addresses of a business, and they added predictions that matched no contact.
- **Never a contact:** a byline ("By Dana Reyes | May 3"), an attribution after a dash, and a name on its own line under a quotation.

Development rows for these rules are in the repository's `tools/Eval/extraction/crm-texts.py` (synthetic notes and comments from our own templates, with negatives that look like names: "Will Call", "Rose Hill", "Lincoln, Nebraska").

Word lists (titles, cues, places, region codes, industry words, legal-form words) are our own, in `NameVocabulary` and `NameLists`.


## The span model on real pages

On a web page the span model is not used: scored on real pages, it lowered the record and address rows below the rules and markup alone, and no model may make a row worse than the rules. Pages are read by markup first, then by the rules over visible text. A gated way of letting the model add people and organizations near fields the rules found was also scored and left off. The measured rows are kept in `docs/maintainers/extraction-measurements.md`.

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

**Errors and limits.** A default `ContactPage`, a duplicate id or a null set throws `ArgumentException` / `ArgumentNullException`; a page past the extractor's limits throws `ContactExtractionException` as for a single page. Problems of a page (`MalformedJsonLd`, `LimitExceeded`) come back in `Issues` with the page id. The single-page limits apply per page; the number of pages is the caller's to bound.
