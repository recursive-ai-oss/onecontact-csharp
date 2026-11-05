# Name and company training data: US and Canada (English and Canadian French)

Provenance of the training examples the person-name and company-name models learn from. The data is rendered by `tools/data/names` from open name
lists and company registers through our own templates. Nothing here is a real person's record: given names and family names are drawn independently
from frequency lists.

## What it contains

JSON Lines, one example per line:

```json
{"id":"p00000042","text":"Dr. Maria G. van der Berg","class":"person","pattern":"gpf","noise":"lower","lang":"en","region":"US","src":"name-lists","elf":"…","parts":[["Prefix",0,3],["Given",4,9],["Particle",10,19],["Family",20,24]]}
```

- `class`: `person`, `company`, `multiple` (several people in one string) or `unknown` (job titles, departments, placeholders, bare words, digits, long text).
- `parts`: `[label, start, end]` in UTF-16 characters of `text`; in `multiple` strings a fourth item lists the people (1-based) a part belongs to (a shared family name lists all).
  Person labels: `Prefix`, `Given`, `Middle`, `Nickname` (inner text, quotes not included), `Particle`, `Family`, `SecondFamily`, `Suffix` (generational), `ProfessionalSuffix`.
  Company labels: `LeadingArticle`, `Base`, `LegalForm`, `TradingAs` (after the d/b/a marker), `CareOf` (after the c/o marker). Markers and connectors are not labelled.
- `elf`: the ISO 20275 code of the legal form, when the register or the form list pins it to one code.
- `noise`: a case or accent change applied to the whole string (`upper`, `lower`, `title`, `no-accents`); offsets stay valid because the change is character for character.
- `src`: `name-lists`, `templates`, `gleif`, `companies-house`, `edgar`.
- Labels come from the templates and the registers, never from a parser: a person's parts are known because the renderer placed them; a company's legal form is the
  place where a written form of the register's ELF code (from the ISO 20275 list) stands at the end of the name.

Rendered output is a directory with `train/`, `dev/` (250,000-line shards), `text-hashes.u64`, and `manifest.json`. It lives on the build machine under
`$ONECONTACT_DATA/train/names-us-ca-v1/` and is never committed.

## Sources

Fetched by `tools/data/names/fetch.sh` from `tools/data/names/pins/names.pin`, each file pinned by size and SHA-256 (the pin file is the record; hashes below are
abbreviated). Licences were read at the publisher on 2026-11-05.

| Source | File (SHA-256) | Licence, as the publisher states it | Verdict and conditions |
|---|---|---|---|
| US Census Bureau, Frequently Occurring Surnames from the 2010 Census, file B (surnames occurring 100 or more times, 162,253 names) | `census/names.zip` (117c41cb…) — www2.census.gov/topics/genealogy/2010surnames/names.zip | US Government work. 17 U.S.C. §105: "Copyright protection under this title is not available for any work of the United States Government" (copyright.gov/title17/92chap1.html). | OK. Public domain; credit as courtesy. |
| US Social Security Administration, Popular Baby Names, national data (births 1880–2025, names with at least 5 births per year and sex) | `ssa/names.zip` (cd78e975…) | US Government work (17 U.S.C. §105, above). The SSA's edge refuses scripted downloads (HTTP 403), so the file is fetched from the Internet Archive snapshot of 2026-11-02 of the same URL (web.archive.org/web/20261102233045id_/https://www.ssa.gov/oact/babynames/names.zip). | OK. Public domain. Only birth years 1940 and later are used. |
| Retraite Québec, Banque de prénoms (filles, garçons), 1980–2025 | `qc/grande_listef_csv.csv`, `qc/grande_listeg_csv.csv` (08a0768f…, de0a0396…) — donneesquebec.ca | Données Québec lists both datasets under "Attribution (CC-BY 4.0)" (donneesquebec.ca/licence/#cc-by); open.canada.ca: "Licence Creative Commons 4.0 Attribution (CC-BY) – Québec". CC BY: "distribuer, remixer, arranger et adapter votre œuvre, même à des fins commerciales, tant qu'on vous attribue le crédit". | OK with attribution (below). |
| INSEE, Fichier des prénoms (births in France 1900–2022, `nat2022.csv`) | `insee/nat2022_csv.zip` (c13b45e7…) — insee.fr/fr/statistiques/7633685 | insee.fr/fr/information/2008466: "Sauf mention contraire, les informations publiques diffusées sur ce site … sont mises à disposition sous la Licence Ouverte / Open Licence version 2.0 (Etalab). Cette licence autorise la réutilisation libre, y compris à des fins commerciales, sous réserve de mentionner la source sous la forme « Source : Insee », de mentionner la date de dernière mise à jour des données … et de ne pas altérer le sens des informations". | OK with attribution (below). |
| INSEE, Fichier des noms de famille (births in France 1891–2000, national file `noms2008nat`) | `insee/noms2008nat_txt.zip` (c8693ff6…) — insee.fr/fr/statistiques/3536630 | Same INSEE page and licence as above. | OK with attribution. |
| GLEIF, Level 1 LEI golden copy, CSV of 2026-11-05 08:00 (3,485,407 records) | `gleif/20261105-0800-gleif-goldencopy-lei2-golden-copy.csv.zip` (de9a5fe2…) — goldencopy.gleif.org | gleif.org/en/meta/lei-data-terms-of-use: "The data available through the Access Service are provided under the CC0 licence". | OK. CC0; credit line below. Only the legal name, legal address country, jurisdiction, entity category and legal form code are read. Sole proprietors (736 records) are dropped: they are individuals. |
| GLEIF, ISO 20275 entity legal forms code list v1.6 (2026-02-19) | `upstream/gleif-elf` (c55edc42…), already pinned for the library | CC0 (same terms). | OK. |
| UK Companies House, Free Company Data Product, basic company data snapshot of 2026-11-01 (5,711,780 rows) | `ch/BasicCompanyDataAsOneFile-2026-11-01.zip` (b6f8e091…) — download.companieshouse.gov.uk | The download page: "This snapshot is provided free of charge and will not be supported." data.gov.uk lists "Basic Company Data" (publisher Companies House) with licence "Creative Commons Attribution"; gov.uk pages are "available under the Open Government Licence v3.0". | **Unclear which of two permissive licences applies; both allow this use with attribution.** Attribution below covers both. Reported for a ruling. Only the company name, number (used to pick a share of rows by hash) and status are read. |
| SEC EDGAR, company names (`cik-lookup-data.txt`, 1,066,136 lines) | `sec/cik-lookup-data.txt` (f95d9fb4…) — sec.gov/Archives/edgar | sec.gov/about/privacy-information: "Information presented on sec.gov is considered public information and may be copied or further distributed by users of the web site without the SEC's permission. Please consider appropriate citation to the SEC as the source." | OK. Citation given below. |

Not used: G-NAF (not applicable here); OpenStreetMap, OpenAddresses and Common Crawl (not needed for names). No open Canadian surname list with permissive terms was
found, so Canadian English surnames come from the Census list and Canadian French surnames from INSEE plus a short list of our own (`templates/fr-ca-surnames.tsv`,
51 common Quebec names with their accents). The English-Canada given names are the SSA list. Provincial English baby-name lists (Alberta, BC, Ontario) were not needed
and are spreadsheets; they can be added as another pool.

Hosted-LLM labels: none. Hand-written lists: `tools/data/names/templates/*.tsv` (forms of address, suffixes, particles, nicknames, Spanish given names, Quebec surnames, job
titles, departments, placeholder strings, trade words, legal forms for synthesized names), written from general usage; their SHA-256 are in the manifest.

## Attribution lines (to pack in the model package notices)

- US Census Bureau, "Frequently Occurring Surnames from the 2010 Census"; US Social Security Administration, "Popular Baby Names" (US Government works).
- Gouvernement du Québec, Retraite Québec, "Banque de prénoms – Filles" and "– Garçons", licence CC BY 4.0.
- Source : Insee, "Fichier des prénoms" (données 1900–2022, publié en 2023) et "Fichier des noms de famille" (naissances 1891–2000, édition 2008), Licence Ouverte / Open Licence v2.0 (Etalab). The data was reduced to counts per name and combined; the meaning of the information is not altered.
- Global Legal Entity Identifier Foundation (GLEIF), Level 1 golden copy and ISO 20275 code list, CC0 1.0.
- Contains public sector information from Companies House (Free Company Data Product, 2026-11-01), licensed under the Open Government Licence v3.0 (Creative Commons Attribution).
- US Securities and Exchange Commission, EDGAR company names (cik-lookup-data.txt), public information.

## How the examples are made

Tool: `tools/data/names` (`OneContact.Data.Names`, version `names-1`), seed 20261105, one deterministic generator per example seeded from the run seed and the example id,
so re-rendering gives byte-identical files (checked: two renders of the final tool produced identical `manifest.json`, which holds the SHA-256 of every output file).

**Persons.** 1.25 million attempts. Locale: English-US 64%, English-Canada 14%, French-Canada 17%, Spanish-US 5%. Given names: SSA (births 1940 on, at least 100 births) for
English; Québec list and INSEE (at least 100 births since 1940) for French, with compound given names ("Jean-Pierre") in 12%; a 49-name list of our own for Spanish.
Family names: Census for English; INSEE, the Quebec list and Census (45/35/20%) for French; Census names that are at least 75% Hispanic in the Census table for Spanish. Names are drawn half in
proportion to their count and half in proportion to its square root, so that common names dominate and rare ones still appear. Titles are drawn by language and
paired with the sex of the sampled given name only to look natural; the sex is not a label and is not stored.
Patterns (25): Given Family; Given Middle Family; Given M. Family; J. Family; J. R. Family; Family, Given; Family, Given Middle; Family, Given M.; title + name (3 forms);
generational suffix; professional suffix (with and without dots, "P.Eng."); both suffixes with title; nickname in quotes or parentheses; particle between given and
family names; trailing particle after a comma ("Berg, Jan van den"); hyphenated family names; two family names (Spanish); a title-suffix-everything form. A family name
that opens with a particle word in the lists ("Le Gall", "De La Cruz") is labelled `Particle` + `Family`, the way the rule parser reads it. Noise: all capitals (7%), all lower case (7%),
double spaces or odd comma spacing (1.5% each), accents removed for French (2%).

**Several people.** 160,000 attempts: "Mr & Mrs John Smith", "Mr & Mrs Smith", "John and Jane Smith", "John Smith and Jane Doe", semicolon or line-break lists,
"John, Jane and Bob Smith", "Dr. John and Dr. Jane Smith", with English "and"/"&", French "et"/"&" and Spanish "y". Shared parts list all their people.

**Unknown.** 160,000 attempts: job titles (level + area + role), departments, placeholders ("N/A", "Current Resident"), single given or family names, titles alone, digits and
addresses, e-mail-like strings (reserved example domains), and 9–14 filler words. Duplicates are kept here (small lists); a text always falls in one split.

**Register companies.** All GLEIF legal entities whose legal address is in US, CA, GB, FR or IE (869,693 after de-duplication), 8% of Companies House active companies picked
by a hash of the company number (405,373), and the EDGAR names that end in a legal form (573,525 of 1,066,136 lines). Each name is split into leading "The", base name, joiner and
legal form using the ISO 20275 forms of the register's code (GLEIF) or of the country (others); records whose code cannot be placed in the name are skipped (210,416), as are
personal-trust and settlement names (111,045) and names that read as a bare person name without a form (2,333). Patterns per record: as registered; re-cased (title, upper or lower); a
different written form of the same code; legal form dropped (only when the base does not read as a person's name); "The" added; trading-as ("dba", "d/b/a", "doing
business as"; "t/a", "trading as" for GB and IE) with another record's base name; care-of ("c/o", "care of") with a sampled person or a base name; comma and spacing noise.

**Synthesized companies.** 170,000 attempts built from the family-name pools and our trade words: "John Smith Plumbing LLC", "Smith & Sons", "Smith, Jones & Lee LLP",
"Law Offices of …", "Smith's Pizza", "Smith Family Dentistry", "The Smith Group", French "Gestion Tremblay inc.", "Tremblay et Fils ltée", and numbered companies ("9123-4567 Québec inc.",
"1234567 Canada Inc.", "1234567 Ontario Inc.").

**Splits.** Chosen before any model sees the data, by a stable hash (FNV-1a 64 with salt `0x73706C6974303031`, then one SplitMix64 round) of the folded key (lower-case letters and digits): the base
name for companies, the whole string before case changes for persons. Bucket 0–89 is `train`, 90–94 is `dev`, 95–99 is **reserved**: those examples are not written anywhere,
and evaluation sets for these models are to be drawn from records and strings in the reserved bucket (or checked against `text-hashes.u64`, the sorted 64-bit hashes of every train and dev text). The same company
name always lands in one split, whatever its form or casing. Evaluation sets built by other tools are separate from this data.

## Counts (final render)

Train 2,936,960 and dev 162,606 examples; 165,223 examples fell in the reserved bucket and were not rendered; 42,285 duplicate person/company texts were redrawn.
Output size 594 MB in 14 files.

| class | train | dev |
|---|---|---|
| person | 1,123,795 | 62,466 |
| company | 1,525,126 | 84,687 |
| multiple | 143,767 | 7,982 |
| unknown | 144,272 | 7,471 |

Train patterns, person: gf 376,885; gmf 129,389; gif 111,095; fg 91,653; tgf 84,056; gpf 40,556; fgm 34,481; if 29,773; gfs 29,040; gfp 28,545; gf-hyphen 23,210; gfsf 21,353;
fgi 21,120; iif 20,662; tf 18,842; tgmf 16,458; tgfp 9,268; fgp 8,054; gmfsf 7,453; full 5,339; fgs 4,646; tgfsf 4,335; gnf 2,972; fsfg 2,731; gnf-paren 1,879.
Company: register 706,382; recased 234,322; dba 118,790; no-form 114,362; care-of 74,483; punct-noise 54,423; article-added 44,880; person-trade 36,389; form-alt 25,112; fr-prefix-surname 14,341;
surname-partners 14,297; possessive-shop 13,923; numbered-quebec 13,122; surname-and-sons 11,317; numbered-province 10,012; law-offices 9,756; family-practice 9,231; numbered-canada 8,291;
the-surname-group 6,082; fr-surname-et-fils 5,611.
Multiple: mr-mrs-gf 39,080; g-and-g-f 36,615; gf-and-gf 26,022; gf-semicolon-gf 13,131; mr-mrs-f 12,417; list-g-g-g-f 9,849; dr-and-dr 6,653.
Unknown: job-title 47,269; bare-given 22,396; long-text 18,862; bare-family 18,442; email 12,560; department 11,473; generic 6,636; title-only 3,401; digits 3,233.
Train noise: none 2,504,064; lower 155,817; title 151,457; upper 122,420; no-accents 3,202.

## What the rule parser gets right alone (information for the model unit)

A deterministic 5,118-string sample of the dev split (every 32nd string by id hash) was run through `NameParsing.Split` with the string's country and language as hints.
"Parts right" means the set of (label, start, end) spans equals the gold set. The person gold is made from the same pattern vocabulary the rules were written against,
so the person and several-people rows are close to a ceiling by construction; the company rows come from real register names and are the informative ones.

| class | n | class right | parts right (given gold class) | class and parts right |
|---|---|---|---|---|
| person | 2,034 | 99.0% | 98.5% | 98.5% |
| company | 2,637 | 93.4% | 90.1% | 90.1% |
| multiple | 230 | 99.6% | 94.3% | 94.3% |
| unknown | 217 | 50.2% | (no parts) | 50.2% |
| all | 5,118 | 94.1% | 94.0% | 91.9% |

What is left for the model: company names without a legal form (class right 43%, all parts right 34%: "Bestline Imports" reads as a person); job titles and placeholder strings (0%:
"Senior Analyst" reads as a person); long free text (43%); generational suffix `II` after a name reads as a legal form ("Ashley Snow II" is a company, 82% on `gfs`); a comma list of given names
("Paula, Kelvin and Emmaleigh Rodriguez", parts 0%); "Family Dentistry"/"Smith's Pizza" style names (76% and 68%); Spanish initials without dots (94%). Generic ELF words are the
largest disagreement on real names: the rules call "Fund", "Trust", "Society" and "Bank" a legal form, while the labels here follow the register's own code (an ELF code that includes them) or,
for registers without codes, leave them in the base name. Per-label exact-span recall: Given 98.5%, Family 98.8%, Middle 98.2%, Prefix 98.7%, Particle 100%, Suffix 77.5%,
ProfessionalSuffix 96.0%, Base 90.1%, LegalForm 99.4%, TradingAs and CareOf 100%.

## Limits to know

- Names are drawn independently, so families like "Ilan du Fortin" (a French particle attached to a name from the list) are plausible in structure, not records of real people.
- Real private individuals appear only through the frequency lists. Personal-trust and settlement names, sole-proprietor LEIs and EDGAR filers without a legal form were removed; a legal-entity name that contains a person's name
  ("Jane Baker Photography LLC") is a company name as registered.
- The labels follow the ISO 20275 list for legal forms; a form the list does not carry is not labelled.
- 210,416 register records whose ELF code could not be placed in the name (about a quarter of the GLEIF records read) were dropped rather than guessed.
- Canadian English surnames are the US Census list; no open Canadian surname list was found.
