# Provenance: address training data, United States and Canada (`address-us-ca`)

Training examples for the address-field head, rendered from two open registers through the templates in `tools/data/templates/`. This file is the committed record; the run's full manifest (`manifest.json`: every file hash, seed and count) is produced by `render` and kept with the generated data.

## Registers

| Register | Version | Terms (quoted) | Verdict |
|---|---|---|---|
| US DOT **National Address Database**, comma-delimited text file | compiled 2026-09-30; `NAD_TXT.zip`, 8,477,543,836 bytes, SHA-256 `efac5c350b4fefc9412b601e54f4a26e740049b251a6f9136ea5cc0f86a4ca83`; fetched from `https://data.transportation.gov/download/fc2s-wawr/application%2Fx-zip-compressed` (dataset `fc2s-wawr`); metadata XML SHA-256 `07e1cffb…cbfa` | "The NAD is a work of the federal government and as such is not subject to copyright protection in accordance with 17 U.S.C. § 105. It is available for re-use without limitation or restriction." (disclaimer page, retrieved 2026-11-05, and the same text in the file's metadata). One caution: "Although the NAD is open data, it is not intended for use as a mailing list and is subject to individual state statutes that prohibit it's use as a mailing list." | Go. Training a model is not a mailing list and the records are not redistributed; only model weights are. The file documents no per-state terms, so no state is dropped for licence reasons (states with no usable rows are listed below). |
| Statistics Canada **Open Database of Addresses (ODA)** v1.0, catalogue 46-26-0001, issue 2021001, ten province and territory files | released 2021-04-29; hashes in `tools/data/pins/oda.pin`; `https://www150.statcan.gc.ca/n1/en/pub/46-26-0001/2021001/ODA_<PR>_v1.zip` | "The Open Database of Addresses (ODA) is a collection of open address point data and is made available under the Open Government License - Canada." Open Government Licence – Canada 2.0: "The Information Provider grants you a worldwide, royalty-free, perpetual, non-exclusive licence to use the Information, including for commercial purposes … Copy, modify, publish, translate, adapt, distribute or otherwise use the Information in any medium, mode or format for any lawful purpose." (condition: acknowledge the source). | Go, with the attribution below. The ODA was compiled with the processing code of OpenAddresses, but its content is the 99 government datasets, not OpenAddresses data. |

**ODA providers.** `Data_Sources.csv` inside every ODA zip (SHA-256 `675a60ac213173f975465fe4f55180e8c9efa589ade2a88d5cdc2040b3275523`) names, for each of the 99 providers, its own open licence (Open Government Licence variants, municipal open data licences, CC BY 4.0 and CC0 for some). The ODA's release states these are compatible with the Government of Canada licence. Five providers name no licence, or only a disclaimer or a custom licence, and are **left out** (`tools/data/templates/oda-excluded-providers.tsv`): City of Fredericton, Cornwall, Columbia-Shuswap, City of West Kelowna, Middlesex County. Their 101,475 rows are not used. The individual municipal licences were not each read; the ODA release is relied on for their compatibility.

## What was used

| | US (NAD) | CA (ODA) |
|---|---|---|
| Usable records after cleaning | 88,880,747 | 7,930,990 |
| Rendered (a per-region sampling rate gives even coverage, see `selection.tsv`) | 1,295,856 | 377,348 |
| Train examples | 1,270,061 | 369,786 |
| Dev examples | 25,795 | 7,562 |
| Regions | 45 states and DC, plus VI (2,695 train); each of the others has about 27,000 train examples | AB BC MB NB NS NT ON PE QC SK (about 40,000 each; NT 6,803) |
| Language | English | English 326,740 train; French 43,046 train |

After cleaning, the NAD text file leaves no rows for HI, MI, MS, NH, NV, AS, GU, MP or PR; FL keeps 42,331 (most of its rows lack a ZIP). The ODA has no NL, YT or NU. Puerto Rico and the other territories therefore have no register rows here.

Rows left out and why are counted in `rejects.tsv` (NAD: no five-digit ZIP 7.2 M; a street type inside the name 1.2 M; a house prefix 0.4 M; …).

## Generator

- Tool `tools/data/AddressUsCa` (`count`, `render`, `check`), template version `74baca1530eae5f8` (SHA-256 prefix over `layouts.tsv` and `names.tsv`), generator seed `20261105`. Each record gets its own random source seeded from the run seed and the record's id, so output does not depend on reading order.
- Rates (written into the manifest): all-capitals 30 %, title case 45 %, lower case 8 %, mixed 17 %; PO box 3.4 % (US) / 3.0 % (CA), rural route 1.1 % / 1.2 %, highway contract route 0.5 % (US), military 1.0 % (US), general delivery 0.8 %; synthetic unit 9 % of street addresses with none; recipient line 45 % of labels and 14 % of single lines; organisation 18 % / 6 %; country 6 % / 8 %; missing postal code 5 %, missing region 4 %, missing city 0.5 %; ZIP+4 at 12 % (50 % when the register has the +4); typing or OCR edit in 6 % of examples (1–2 edits in a labelled span); a lost space between two spans 1.2 %; a doubled space 1 %.
- Recipients and organisations are invented from the word lists in `templates/names.tsv` (no real person or business is intended; the lists are our own). ZIP+4 digits are random when the register has none, and military ZIPs, PO box, route and unit numbers are random: none of these is a register fact.
- Split: an example is **dev** when `hash(record id, split salt) mod 1000 < 20`, else train. The split depends only on the register's record id, is fixed before rendering, and no record is in both. Identical addresses with different ids are rendered once (5,604 duplicates skipped), so train and dev share no address text.
- The examples are not evaluation data. The evaluation sets are separate and are never rendered from these registers' training selection.

## Checks on the run

- Re-render with the same seed gives byte-identical files (two runs, same hashes; the two manifests are identical too):

  | File | Examples | SHA-256 |
  |---|---|---|
  | `us.train.jsonl` | 1,270,061 | `bd5ec81bfc7c16342395f2c53ee2a7729250d623d333e58ff20f5fae3bf3ebf3` |
  | `us.dev.jsonl` | 25,795 | `e2f59d49fab566db90d2caa20fd05bc676e9810f923da3328297fad8711a2a02` |
  | `ca.train.jsonl` | 369,786 | `9932d4a36ed2151e39e5c8ee0bd491d2904e0f50cdd714fdff269ee4d6409bb9` |
  | `ca.dev.jsonl` | 7,562 | `eb448336b025982e6ab07177655059512f00310a0f8e053fd7bdefe8143b4053` |

  Manifest SHA-256 `51619188e120e499e54e8c1e36a34fbad64ffc7e68338ca83c7aa744a7a89f82`; selection file `2cfc44e731642f0bdd15cd55d9630d2c238e7d1b3072c181b86cf019e610d758`.
- Every example: spans ordered, non-overlapping, inside the text, on token edges, no leading or trailing space, and every letter and digit of the text inside a span. 1,673,204 examples, 0 failures.
- A sample of about 5,000 training examples per country through the library's rule parser (information): see `docs/training-data-address-us-ca.md`.

## Regions the National Address Database lacks: Census TIGER/Line (`address-us-tiger`)

The National Address Database text file has no rows for PR, AS, GU, MP, HI, MI, MS, NH and NV and few for VI. Those regions are synthesized from Census TIGER/Line address ranges and street names, rendered by the same tool (`render --tiger <dir> --countries us --dataset address-us-tiger`) through the same templates, label set, tokenization, noise and split method. This is a separate output set (`us.train.jsonl`, `us.dev.jsonl` in its own directory with its own `manifest.json`); the NAD/ODA set above is unchanged.

| Source | Version | Terms (quoted) | Verdict |
|---|---|---|---|
| Census **TIGER/Line Shapefiles**, county address range features (`ADDRFEAT`) and, for AS and MP, county edges (`EDGES`) | TIGER2025, files dated 2025-09; 281 county files and the relationship file, pinned by size and SHA-256 in `pins/tiger.pin`; fetched by `tools/data/fetch-tiger.sh` from `https://www2.census.gov/geo/tiger/TIGER2025/ADDRFEAT/` and `.../EDGES/` | Technical documentation chapter 1 (2025): "Copyright protection is not available for any work of the United States Government (Title 17 U.S.C., Section 105). Thus, you are free to reproduce census materials as you see fit. We would ask, however, that you cite the Census Bureau as the source." The same chapter: "No warranty, expressed or implied, is made with regard to the accuracy of the data in the TIGER/Line Shapefiles", and "TIGER/Line cannot be used as or within the proprietary product names of any commercial product" | go: public domain, cite the Census Bureau; the models are not named TIGER/Line |
| Census 2020 **ZCTA to place relationship file** `tab20_zcta520_place20_natl.txt` (9,811,563 bytes, SHA-256 `698a5dad…285d`) | 2020 | Census Bureau data, same terms (17 U.S.C. 105) | go |

What comes from the data and what is synthetic:

- From TIGER: street names (the Census spelling with its type and directional abbreviations, split with the Publication 28 tables), the ZIP Code of each side of an address range, the house-number range and its parity, and the county (state or territory).
- Synthetic: the **house number** is drawn at random inside the range, even or odd as the parity column says, from a hash of the range id and a draw index (three draws per range side; 16 in VI, AS and MP, which have few ranges). In AS and MP, which have **no address ranges** in TIGER (the edges files hold road names only, apart from five ranged edges on Saipan), the house number is drawn from 1 to 998 and the ZIP follows the island (USPS: 96799 for American Samoa, 96950 Saipan, 96951 Rota, 96952 Tinian); the records are counted as `source-tigeredge` in the manifest. The **city** is a place from the relationship file that overlaps the ZIP in the same state, drawn with probability proportional to the overlapped land area, so it is a plausible locality for the ZIP and not the postal city of that street. Puerto Rico uses the urban zones (`zona urbana`) as cities and the communities (`comunidad`) of the ZIP as urbanization names. Recipients, organizations, unit numbers, ZIP+4 digits, PO boxes, routes and military lines are invented as for the other sets.
- Puerto Rico (USPS Publication 28 appendix I, sections 51.1 to 51.4): the street prefix stays in the street name (`CALLE AMAPOLA`, `AVE FD ROOSEVELT`, `CARR 115`), the Census abbreviations `Cll`, `Cam`, `Pso`, `Plz` are written out, `Ave`/`Avenida` and `Carr`/`Carretera` are drawn by hash, and state routes (`Pr- 8177`) are written `PR-8177` or `CARR 8177`. The urbanization line comes before the delivery line and is written in 75 % of examples when the record has one; its whole text (`URB`, `Urbanizacion`, `Urbanización` or none, then the name) is one `urbanization` span, a label the rule parser's field list also names. Layouts: `templates/layouts-pr.tsv`, version `797561056fa22c8b`; the base template version stays `74baca1530eae5f8`.
- Rows left out and why are counted in `rejects.tsv`: no usable ZIP-to-place link (rural ZIPs outside every place), an address range that does not parse (letters that differ at its two ends), a road class that is not a street (only primary, secondary, local and private roads are kept), and similar.

Seed `20261105`, dev split `hash(record id, split salt) mod 1000 < 20` of the range-draw id (`tiger:<state>:<range id>:<draw>`). The split is by record, as for NAD and ODA: a street can appear in train and dev with different house numbers, so the dev loss measures the head's fit to unseen records, not to unseen streets. Evaluation sets are not drawn from this data.

Run (worker, 2026-11-05): `count --caps PR=80000,VI=24500,*=27000 --per-range *=3,VI=16,AS=16,MP=16`, then `render`; twice with the same seed:

| File | Examples | SHA-256 (both runs) |
|---|---|---|
| `us.train.jsonl` | 248,662 | `637dbf7077f09b4db031a76fb4a4acaa86c816abba2e64ec9a91357c5c710f9f` |
| `us.dev.jsonl` | 4,988 | `439197188103c68b36f4c837a55e55b2f5af32efddcaee9b1926364bd083612e` |

Manifest SHA-256 `fee680bda4fcc109cc6a92657b41e0640bb9c54620ec542b12f3d6350a1a8ceb`; selection file `ab9d4d0088bcdb62867b5ff73be0dfa8c1e216a9ca9875a8d69d5bc6034069b7`. 2,482 repeated addresses were skipped. All 253,650 examples pass the span checks (0 failures).

| Region | Train | Dev | Note |
|---|---|---|---|
| PR | 76,691 | 1,539 | cap 80,000 |
| GU | 26,368 | 506 | |
| HI | 26,172 | 525 | |
| MI | 26,351 | 536 | |
| MS | 26,680 | 541 | |
| NH | 26,411 | 554 | |
| NV | 26,296 | 521 | |
| VI | 4,160 | 81 | the three counties hold few ranges; draws collide on short ranges |
| MP | 4,938 | 91 | road names only (edges); few distinct names |
| AS | 4,595 | 94 | road names only (edges); few distinct names |

VI, MP and AS stay far below the 27 k of the other regions because the source has few distinct streets, not because of a cap; more draws only repeat the same addresses. VI also has 2,695 NAD rows in the first set.

What the library's rule parser recovers from about 600 training examples per region (every labelled field, same label and span; information): HI 77.3 %, MI 70.3 %, NH 67.2 %, MP 57.6 %, NV 55.4 %, VI 51.0 %, MS 48.6 %, GU 40.7 %, AS 7.3 %, PR 6.3 %. AS and PR are low, probably because the rules do not read `Rte 114` (a Census route abbreviation), Spanish prefixes or the urbanization line; the rules never return an `urbanization` field (0 of 424 in the first sample). Detail: `check.txt` next to the data.

## Attribution lines for the model package notices

- Address points: U.S. Department of Transportation, National Address Database (public domain).
- Street names and address ranges for Puerto Rico, Guam, the U.S. Virgin Islands, the Northern Mariana Islands, American Samoa, Hawaii, Michigan, Mississippi, Nevada and New Hampshire: U.S. Census Bureau, TIGER/Line Shapefiles 2025 and the 2020 ZCTA to place relationship file (public domain).
- "Contains information licensed under the Open Government Licence – Canada." Source: Statistics Canada, The Open Database of Addresses (ODA), version 1.0, compiled from the open address data of the provincial, regional and municipal governments named in the ODA's `Data_Sources.csv`, each under its own open licence (attribution statements listed there).
