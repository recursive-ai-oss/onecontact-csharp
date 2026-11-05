# Provenance: contact-block training data, United States and Canada (`blocks-us-ca`)

Training text for the extraction span head: short pieces of text (page footers, contact sections, staff lists, email signatures, business cards, CRM notes, free user text and look-alike negatives) in which person names, company names, addresses, phones, emails and websites are labelled with exact character offsets, and where each record's block begins is marked. English (US and Canada) and Canadian French. The generator is `tools/data/blocks` (`OneContact.Data.Blocks`, version `blocks-1`). This file is the committed record; the run's `manifest.json` (every file hash, the seed and all counts) stays with the generated data.

## What it contains

JSON Lines, one example per line:

```json
{"id":"b00000007.crm","block":"b00000007","kind":"ugc","loc":"us-en","view":"crm","neg":false,
 "text":"Call 303-602-6521","spans":[[5,17,"phone",0]],"bounds":[0]}
```

- `spans`: `[start, end, label, record]` and, when the shown text is not the source value itself, a fifth item, the mode. Offsets are UTF-16 code units into `text`. Labels: `person`, `company`, `address`, `phone`, `email`, `website`. `record` counts the records of the example from 0 (`-1` for a mention that belongs to none).
- `bounds`: the offset where each record's block begins, in record order. Navigation, headings and boilerplate before a block belong to no record.
- Modes: `u` shown in capitals; `n` one space shown as a line break inside the field; `o` obfuscated (emails written `jane [at] example [dot] com`, `(at)`, `{at}`, `AT`/`DOT`; phone digits spaced out, `2 0 2 5 5 5 …`), labelled at the visible text; `c` first letter of an email capitalised. Combinations are concatenated (`un`).
- `view`: `page` is the visible text a page reader produces (one item per line, no markup); `crm` is the same block as notes or pasted text: items joined by a newline, `;`, `|`, `,`, ` - `, ` / ` or spaces. Both views of a block share its people, company and values, and its split. Footers exist as `page` only; notes and free text as `crm` only.
- `neg`: the example has no field. Two kinds: `negative-numeric` (order, tracking, invoice, case and reference numbers, versions, IP addresses, dates, prices, coordinates, ISBNs, part numbers shaped like phone numbers but not valid ones, mixed with ordinary sentences) and `negative-prose` (product and how-to sentences). The positive kinds also carry such lines unlabelled.
- Not labelled: job titles, departments, headings, office names, greetings (a greeting's given name is labelled `person`), navigation and legal boilerplate. Job title is not an extraction field.

Text is rendered, not scraped: no HTML is involved, so offsets are exact by construction (the page reader's own offsets into markup are a different matter, and the head is applied to text the reader produces).

## What goes in

| Input | Source and licence | Use |
|---|---|---|
| Person strings, company strings | `names-us-ca-v1` (`tools/data/provenance/names-us-ca.md`): frequency-list names (public domain, CC BY, Licence Ouverte), GLEIF (CC0), Companies House, SEC EDGAR | Persons whose class is `person` and companies of the US and CA regions (Companies House rows are British and not used here). Given and family names are drawn independently from frequency lists, so no person is a real record. |
| Address strings | `address-us-ca` (NAD, public domain; ODA, Open Government Licence – Canada and the providers' own open licences) and its TIGER/Line addition (public domain) | Examples without a recipient or organization line, 3 or more labelled fields, at most 160 characters; the labelled address is the span from the first to the last address field. |
| Phone numbers | Generated: an area code the library's phone metadata accepts for the region (Quebec codes only for French blocks), a random exchange and line, kept only when the metadata says the number is valid for the region | No subscriber list is used. A number may coincide with a real one by chance; none is attached to a real person. |
| Emails, websites | Generated: mailbox from the drawn person (`first.last`, `flast`, `first_last`, …), role mailboxes, free-provider addresses in CRM text; domains from the company's words or generated syllables, 10% under the reserved `example.com`, `.org`, `.net` | An address may exist at a real domain by chance; none is built from a real person. |
| Words around the fields | Our own: `tools/data/blocks/Lexicon.cs` (labels, headings, navigation, boilerplate, job titles, note and free-text templates, filler sentences, English and French), `Kinds.cs`, `Fields.cs` | Hand-written from general usage. |

Not used: OpenStreetMap, OpenAddresses, Common Crawl, G-NAF, hosted-LLM text. No scraped page text.

## Splits

A block's split is fixed by a hash of its id before anything is rendered (FNV-1a 64-bit over the UTF-8 id with salt `0x626C6F636B303031`, then a SplitMix64 round; bucket = hash mod 100): 0–92 `train`, 93–94 `dev`, 95–99 reserved and never rendered. Train blocks draw people, companies and addresses only from the train files of their sources and dev blocks only from the dev files, so no dev string is a train string. The reserved buckets are the pool evaluation sets can be drawn from. No evaluation set is derived from this data and none overlaps it: the later extraction evaluation sets are private real pages and CRM text.

## Run `blocks-us-ca-v1`

Seed 20261106, 560,000 block ids tried, 28,150 of them in the reserved buckets. Two renders from the same seed gave byte-identical `manifest.json` (it holds the SHA-256 of every output file). Manifest SHA-256 `4f200f04…b2857f`. Inputs by manifest: `names-us-ca-v1` `fab62fc5…195fcb`, address `out` `51619188…a89f82`, TIGER `out1` `fee680bd…a1a8ceb`. Generator sources hashed in the manifest: `Lexicon.cs` `4b1019a0…`, `Kinds.cs` `cdaeb49e…`, `Fields.cs` `683e88d4…`.

**Span check.** While rendering, every span is compared with the value it was built from (identical text; or, by mode, the upper-cased value, the value with a space for the line break, or the deobfuscated email / digits of the number), and record boundaries must be increasing. Failures over the full render: **0** in 873,510 examples.

| | train | dev |
|---|---|---|
| Blocks | 520,771 | 11,079 |
| Examples (page + crm views) | 855,319 | 18,191 |
| Examples with no field (negative blocks) | 119,702 (14.0 %) | 2,586 (14.2 %) |
| Locale (examples) us-en / ca-en / ca-fr | 512,788 / 188,820 / 153,711 | 10,904 / 4,055 / 3,232 |
| View crm / page | 466,198 / 389,121 | 9,916 / 8,275 |

Blocks per kind (train / dev): business-card 33,002 / 720; contact-us 65,821 / 1,446; crm-note 76,388 / 1,606; footer 54,573 / 1,163; multi-location 33,190 / 703; negative-numeric 32,562 / 716; negative-prose 27,289 / 577; signature 77,054 / 1,576; staff-list 65,630 / 1,374; ugc 55,262 / 1,198.

Spans per field (train / dev): person 1,405,730 / 29,286; phone 1,588,970 / 33,566; email 1,297,638 / 27,355; company 503,380 / 10,522; address 655,025 / 14,051; website 269,645 / 5,772. Per locale (train, address / company): us-en 391,226 / 300,714; ca-en 143,224 / 111,091; ca-fr 120,575 / 91,575. Modes (train): obfuscated email 77,708 and phone 47,801; capitals 22,110 addresses, 13,847 companies, 68,631 persons; line break inside the field 6,331 addresses, 4,569 companies, 11,696 persons; capitalised email 48,470. The per-kind, per-field and per-locale counts for every field are in `manifest.json`.

Known limits: the dev pools are small for Canadian French (507 address examples, 2,106 company strings), so dev French blocks repeat strings; company strings are legal names from registers, so company text is more formal than shop signs; phones are North American only; the page view is plain lines, not the reader's real output for nested markup.

## What rules alone find

The library's current rules over 5,000 dev examples taken by stride (information for the span-head recipe; `rule-report.md` is produced by `report`). Phones through `PhoneNumber.FindAll` at valid leniency; emails and websites through token scans checked by the core parsers; addresses by whether the labelled text parses on its own with no hint. The visible-text reader's finders (not merged when this was run) are not included, and no rule-only finder exists for names and companies.

| Field, view | labelled | exact recall | overlap recall | precision (exact) |
|---|---|---|---|---|
| phone, page / crm | 4,322 / 4,759 | 99.1 % / 97.8 % | 100.0 % / 99.9 % | 98.3 % / 96.9 % |
| phone, spaced-out digits | 264 | 98.1 % | 99.6 % | counted with the plain rows |
| email, page / crm | 3,460 / 3,687 | 100 % / 100 % | 100 % / 100 % | 100 % / 100 % |
| email, obfuscated | 393 | 0.0 % | 0.0 % | – |
| website, page / crm | 786 / 769 | 100 % / 100 % | 100 % / 100 % | 72.2 % / 71.4 % |

Addresses that parse on their own: us-en 95.6 % of 2,279, ca-en 91.4 % of 894, ca-fr 82.3 % of 733. Negative examples where rules still find a phone, email or website: 70 of 708 (9.9 %). So the head has to add: obfuscated emails, website precision (dotted tokens such as version numbers and file names), names and companies (all of them), and the address start and end inside running text.

## Attribution lines for the model package notices

- US Census Bureau, "Frequently Occurring Surnames from the 2010 Census"; US Social Security Administration, "Popular Baby Names" (US Government works).
- Gouvernement du Québec, Retraite Québec, "Banque de prénoms – Filles" and "– Garçons", licence CC BY 4.0.
- Source : Insee, "Fichier des prénoms" (données 1900–2022, publié en 2023) et "Fichier des noms de famille" (naissances 1891–2000, édition 2008), Licence Ouverte / Open Licence v2.0 (Etalab). The data was reduced to counts per name and combined; the meaning of the information is not altered.
- Global Legal Entity Identifier Foundation (GLEIF), Level 1 golden copy and ISO 20275 code list, CC0 1.0.
- US Securities and Exchange Commission, EDGAR company names (cik-lookup-data.txt), public information.
- Address points: U.S. Department of Transportation, National Address Database (public domain).
- Street names and address ranges for Puerto Rico, Guam, the U.S. Virgin Islands, the Northern Mariana Islands, American Samoa, Hawaii, Michigan, Mississippi, Nevada and New Hampshire: U.S. Census Bureau, TIGER/Line Shapefiles 2025 and the 2020 ZCTA to place relationship file (public domain).
- "Contains information licensed under the Open Government Licence – Canada." Source: Statistics Canada, The Open Database of Addresses (ODA), version 1.0, compiled from the open address data of the provincial, regional and municipal governments named in the ODA's `Data_Sources.csv`, each under its own open licence.

## Running it

```
dotnet run --project tools/data/blocks -c Release -- render --names <names-us-ca-v1> --addr <address out> --tiger <tiger out> --out <dir> [--seed 20261106] [--count 560000]
dotnet run --project tools/data/blocks -c Release -- report --out <dir> [--sample 5000]
```

`render` exits non-zero when any span fails its check. Adding a locale means adding its pools to `Pools.cs`, a `LexSet` to `Lexicon.cs` and a weight in `Program.cs`; the block kinds and field generators do not change.
