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

## Attribution lines for the model package notices

- Address points: U.S. Department of Transportation, National Address Database (public domain).
- "Contains information licensed under the Open Government Licence – Canada." Source: Statistics Canada, The Open Database of Addresses (ODA), version 1.0, compiled from the open address data of the provincial, regional and municipal governments named in the ODA's `Data_Sources.csv`, each under its own open licence (attribution statements listed there).
