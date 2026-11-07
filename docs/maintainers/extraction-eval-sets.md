# Extraction evaluation sets and baselines

How the real-text sets for contact extraction are built, labelled and scored, and how the reference systems are run on them. Like the address and name sets (`evaluation-sets.md`), the sets hold text copied from public web pages, including personal data, so they are kept private: only the tools, the method and aggregate scores are published.

## The two sets

| Set | Items | Offsets count | What an item is |
|---|---|---|---|
| Web | about 120 North American pages | bytes of the stored page (the HTTP body as received) | a whole page: footers, contact pages, staff and location listings, pages with and without JSON-LD, microdata or h-card, obfuscated emails, pages with no contact data |
| Text | about 115 free-text items | UTF-16 code units of the item | one to six visible lines of a forum post, classified ad, comment, signature or footer, at most 900 characters; the free-text side of extraction (CRM notes are not available, so web text stands in) |

Both come from one pinned Common Crawl crawl (`CC-MAIN-2026-43`), through the public index and WARC byte ranges only, with the tools in `tools/Eval/extraction/`:

1. `select.py` draws random blocks of the crawl's `cluster.idx` (fixed seed) from United States hosts (`.com .org .net .us .edu .gov .biz .info`) and `.ca` hosts, and keeps HTML captures with status 200, at most two per host per block. Web: contact, about, team, staff and location paths, the site root, and a small share of other paths. Text: forum, thread, classified, comment, review and blog-post paths. **Every host that appears in the address and name sets' draw is skipped**, so no page or host is in both.
2. `fetch.py` reads the captures and stores each response body unchanged.
3. `pick-web.py` keeps pages that look North American (English, or French on `.ca` hosts, with US or Canadian shaped contact data) and draws them by what they carry, rarest first: obfuscated emails, microdata or h-card, JSON-LD, no contact data, `mailto:`/`tel:` links, text only; one page per host. `pick-ugc.py` cuts items out of the fetched pages, drops runs of short menu-like lines and prose that does not look English, and draws by what the item carries (address, phone, email, website, signature, several, nothing).
4. The reviewer reads each item beside the extractor's pre-label (`eval-extract review`), writes corrections (drop a field, add a field by its text, regroup), and `eval-extract gold` resolves every added text to a span of the page or item. Items that were not usable (another country, empty or parked pages, machine-made spam, template placeholders) were dropped at this step. No hosted model labels anything.
5. `freeze.py` writes each gold file in canonical form (sorted by id, sorted keys, compact JSON) with a manifest: role `eval`, item count, SHA-256, and for the web set the SHA-256 of every stored page. Training code loads only `train` and `dev` files.

The pre-label is OneContact's own rules-only extractor (markup and visible text), so the rules' scores on these sets are optimistic: most gold fields are pre-labelled fields the reviewer accepted. Each gold item records whether the pre-label was accepted unchanged (`rules_agreed`) and each gold field whether it came from the pre-label (`pre`). The reference systems never saw the pre-label.

## Label conventions

A field is one **mention**: a phone number written in the header and again in the footer is two fields. Types and what counts:

| Type | Counts | Does not count |
|---|---|---|
| `person` | a person named as a contact: staff, owner, agent, practitioner, signature, the person a page is about | bylines and commenters of articles, reviewers, people only mentioned in running text |
| `org` | the page's or item's own organization (heading, copyright line, markup name, address-block heading, every occurrence of the entity's own name) and organizations named as the party to contact | organizations only mentioned in passing, tribunals and publishers named in citations, site-builder and theme credits |
| `address` | a postal address, from the street through the postal code or country; partial addresses (a street line, a city and postal code) when that is all the text gives | the address of a place named in an event notice or a news sentence, template placeholders |
| `phone` | any telephone or fax number written out, in text or markup (`tel:`, JSON-LD) | placeholders such as `555-555-5555`, numbers outside North America when the page is outside it |
| `email` | any email address, in text or markup (`mailto:`, JSON-LD), obfuscated or not | a Cloudflare placeholder with no recoverable address; template addresses (`info@mysite.com`) |
| `website` | a URL or domain the entity gives as its own presence: written in text, JSON-LD `url` and `sameAs`, microdata, h-card, social profiles | navigation, site-builder and theme credits, cited sources, advertising and spam links, author-archive links |

The `person` and `org` conventions are contact conventions, not named-entity conventions: a general NER system finds many more people and organizations than these sets count, so its precision here is low by design and its recall is the number to read.

A **record** groups the fields that belong to one person or organization. Gold records are the page's or item's own grouping; the pre-label's grouping is kept where the reviewer accepted it.

## Scoring

`tools/Eval/score-extraction.py` prints counts, scores and intervals only.

- **Field F1**, per type, micro-averaged over items, two ways. *Span*: a predicted field is right when its type, start and end equal a gold field's. *Value*: the fields of an item are compared as sets of (type, normalized text); phones by their digits (a leading 1 dropped from eleven), emails lower-cased, websites without scheme, `www.`, query, fragment and trailing slash, everything else folded to letters and digits.
- **Record F1** (systems that group): a record is its set of (type, normalized text); a predicted record is right when it equals a gold record exactly. **Link F1** is the softer measure over unordered pairs of distinct values that share a record.
- **Mentions** `all` scores every field, markup included. `text` scores only fields read from the visible text (gold `src` Text), which is what the reference systems can see.
- **Intervals**: percentile bootstrap over items, 1000 resamples, seed 20261105, the same resamples for every system, so the difference to the reference column is paired.
- A system is scored only on the types it produces. The pooled `all` row covers only those types, so it is not comparable between systems; compare per type.

## Reference systems

Each is only run (a released jar or package installed by its package manager); none of their source or installed files is read. Web pages are given to them as the visible text the extractor reads, and their character spans are mapped back to bytes of the page through the same text view.

| System | Version, artifact | Run as | Mapped to |
|---|---|---|---|
| libphonenumber matcher | `libphonenumber` 9.0.43, SHA-256 `54a0d94c…f36` (Maven Central jar) | `PhoneNumberUtil.findNumbers(text, region, VALID)`, region `US` or `CA` by the item's country (`tools/Oracles/Extraction/Matcher.java`) | `phone` |
| Microsoft.Recognizers.Text | NuGet `Microsoft.Recognizers.Text.Sequence` 1.8.13 (latest stable), `.nupkg` SHA-512 `kajZCWMCELq5G/LIJfburyIqn7UZ55IlqO9rzWZMQv/S0OGq1pcIJgZ6eDtGYqAXHmdRRSJAb6O9hT4a/CDYdg==` | `SequenceRecognizer.RecognizePhoneNumber`, `RecognizeEmail`, `RecognizeURL`, English culture (`eval-extract recognizers`) | phone number → `phone`, email → `email`, URL → `website` |
| spaCy | spaCy 3.8.18 with `en_core_web_trf` 3.8.0 (wheel SHA-256 `272a31e9d8530d1e075351d30a462d7e80e31da23574f1b274e200f3fff35bf5`), `spacy-curated-transformers` 0.3.1, torch 2.15.0 (CPU), Python 3.13, set up by `tools/Oracles/Extraction/setup-spacy.sh` | `nlp.pipe` over the text (`run-spacy.py`) | `PERSON` → `person`, `ORG` → `org`; other labels dropped |
| OneContact rules | this repository | `ContactExtractor` with markup and text, region by host (`.ca` → CA, else US) | all six types |

## First measurement

Aggregates, 1000-resample bootstrap 95% intervals. Gold fields: web 1,098 (person 25, org 261, address 126, phone 323, email 227, website 136), text 183 (person 21, org 41, address 29, phone 55, email 31, website 6). The rules' rows are optimistic (above); the person and website rows rest on few fields and have wide intervals.

Value F1, visible text only:

| Type | Set | OneContact rules | libphonenumber | Recognizers | spaCy trf |
|---|---|---|---|---|---|
| phone | web | 99.0 [97.4, 100] | 99.0 [97.4, 100] | 90.0 [85.4, 93.9] | — |
| phone | text | 100 [100, 100] | 100 [100, 100] | 92.0 [86.1, 97.3] | — |
| email | web | 98.9 [97.0, 100] | — | 92.2 [85.2, 96.8] | — |
| email | text | 100 [100, 100] | — | 96.6 [88.9, 100] | — |
| website | web | 58.2 [41.6, 73.3] | — | 58.2 [41.2, 73.7] | — |
| website | text | 34.5 [9.5, 56.0] | — | 34.5 [9.5, 56.0] | — |
| address | web | 84.0 [76.6, 90.3] | — | — | — |
| address | text | 46.2 [24.4, 64.0] | — | — | — |
| person | web | 0 (rules read no names from text) | — | — | 8.4 [3.7, 14.0], recall 86.7 |
| person | text | 0 | — | — | 61.3 [45.8, 75.8], recall 95.0 |
| org | web | 0 (rules read no names from text) | — | — | 11.0 [8.3, 14.5], recall 72.5 |
| org | text | 0 | — | — | 32.9 [21.3, 46.4], recall 60.5 |

Records (web, rules, all mentions): record F1 37.9 [28.7, 46.6], link F1 56.4 [29.9, 87.5]; text: record F1 22.8 [11.5, 34.8], link F1 40.9 [26.0, 54.7]. Span-level rows, the markup-included rows (`--mentions all`) and precision/recall for every cell are in the scorer's output. The libphonenumber matcher misses phone numbers that only a markup reader sees (a `tel:` link, JSON-LD), so against all mentions its recall on the web set is 52%.

## Running it

`run-baselines.sh web|text <set> <work dir>` produces one prediction file per system; `score-extraction.py` scores them; `freeze.py` freezes a gold file. `eval-extract` (`tools/Eval/Extract`) is the C# side: the extractor over a set, the pages' visible text, span mapping, the Recognizers driver, and the reviewer view and gold builder. `tools/Eval/tests/test_score_extraction.py` checks the scorer on synthetic data.
