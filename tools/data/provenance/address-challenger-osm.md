# Provenance: address training data for the challenger model (`address-challenger-osm`)

Training examples for a challenger address-field model, converted from the OpenStreetMap address file of the published libpostal parser training set (`tools/data/AddressChallenger`). How the examples look and the label mapping: `docs/training-data-address-challenger.md`. This file is the committed provenance log.

**Lineage: challenger.** The data is ODbL. It is built and measured only for a challenger; it is not used by, and never enters, the default models (`RecursiveAI.OneContact.Models.Tier1`, `RecursiveAI.OneContact.Models`). Every example carries `"set": "challenger-osm"`, the manifest says `challenger`, and the output lives under its own folder. **Nothing ships from this data yet.**

## Source

| | |
|---|---|
| Item | Internet Archive `libpostal-parser-training-data-20170304`, uploaded 2018-01-17 by libpostal's author; the file name and format are documented in libpostal's README ("Training data", "Parser training sets"). The item has no licence field; the licence statements are libpostal's own README lines below. |
| File used | `formatted_addresses_tagged.random.tsv.gz`, 8,033,490,172 bytes, SHA-256 `3851ba7c9c6a0a2f5b27e419343faa9f01a571f7f44303bd123c6e4f45a83a69`, SHA-1 `5c3ec670347257081fc8cb40e8fe7165bb680de0` (the host's own listing; checked), `https://archive.org/download/libpostal-parser-training-data-20170304/formatted_addresses_tagged.random.tsv.gz`. 320,115,455 lines. Pinned in `tools/data/pins/challenger-osm.pin`. |
| Format | `language TAB country TAB tokens`, a token being `word/label`, with `|/FSEP` and `,/SEP` separators; libpostal's component labels. Settled by reading the file (first lines of each file of the item) and libpostal's README. No libpostal source was opened. |
| Fetched | 2026-11-06, `tools/data/fetch-challenger.sh`, to `~/onecontact/data/challenger/osm/` with `LICENSE-ODbL-1.0.txt` (SHA-256 `607680718977f6f6c9607972afd98f208573f19251315ed1362a8589b51beaf5`), `OSM-copyright.html` (`e4becbc81b501d999d8ce2a05e4a98bea4c6a9e65d4ec4a5ff0495f21ec6712a`) and `ATTRIBUTION.txt` beside it. |

## Terms and verdicts

| Component | Terms (quoted) | Verdict |
|---|---|---|
| `formatted_addresses_tagged.random.tsv.gz` (OpenStreetMap addresses) | libpostal README, Parser training sets: "**formatted_addresses_tagged.random.tsv.gz** (ODBL): OSM addresses. Apartments, PO boxes, categories, etc. are added primarily to these examples". openstreetmap.org/copyright: "OpenStreetMap is _open_ data, licensed under the Open Data Commons Open Database License (ODbL) … You are free to copy, distribute, transmit and adapt our data, as long as you credit OpenStreetMap and its contributors. If you alter or build upon our data, you may distribute the result only under the same license." and "Where you use OpenStreetMap data, you are required to do the following two things: Provide credit to OpenStreetMap by displaying our attribution notice. Make clear that the data is available under the Open Database License." ODbL 1.0: §3.1 "These rights explicitly include commercial use, and do not exclude any field of endeavour"; §4.3 "if you Publicly Use a Produced Work, You must include a notice … that Content was obtained from the Database … and that it is available under this License" ("Contains information from DATABASE NAME, which is made available here under the Open Database License (ODbL)"); §4.4 share-alike for a Derivative Database that is Publicly Used; §4.5(c) "Use of a Derivative Database internally within an organisation is not to the public and therefore does not fall under the requirements of Section 4.4". | **OK with attribution** (owner's training-data rule for ODbL). Challenger lineage only. The converted example files are a Derivative Database and stay internal; any publication of them would be under the ODbL. Credit line: "© OpenStreetMap contributors, ODbL". |
| `openaddresses_formatted_addresses_tagged.random.tsv.gz` (12.9 GB) | libpostal README: "(various licenses, mostly CC-BY): most of the address data sets from OpenAddresses, which in turn come directly from government sources". openaddresses.io: "All data is openly licensed. Most sources only require attribution." The file has no source column (only language and country), so the licence of a line cannot be told. | **Not used.** "Mostly" is not enough where a non-commercial, share-alike or ToS-restricted source may be among the rest; the same lack of a source column means Australian lines cannot be separated from any G-NAF-derived source, which is excluded outright. |
| `uk_openaddresses_formatted_addresses_tagged.random.tsv.gz` | libpostal README: "(CC-BY): addresses from OpenAddresses UK" . | Not used (not needed: the OpenStreetMap file covers the UK; the file's lineage is not described beyond the README line). |
| `geoplanet_formatted_addresses_tagged.random.tsv.gz` | libpostal README: "(CC-BY): every postal code in Yahoo GeoPlanet … The GeoPlanet admins have been cleaned up". | Not used (postcode and admin names only; not requested; Yahoo's own terms not read). |
| `formatted_places_tagged.random.tsv.gz`, `formatted_ways_tagged.random.tsv.gz` | libpostal README: "(ODBL)" each: every toponym, every street of OSM. | Not used here (fragments of one field each; ODbL like the addresses file; can be added later by the same tool). |
| OpenCage address-formatting templates (used by libpostal's authors to render the text) | Repository licence reported by the host as MIT. | Not shipped, not used by us: the examples carry text rendered by the source's authors. Noted for completeness. |

Alternative not taken: render OpenStreetMap ourselves from Geofabrik country extracts (ODbL, each country's `.osm.pbf`) through our own templates. It would give current data and our own labels (street type and name separate, our layouts) but needs an OSM reader, per-country tag handling, and tens of GB of extracts for 50 countries. The published file was the brief's first choice and is converted in one pass; the extract route stays open if the challenger's rows justify it.

## Attribution lines (to be packed with any model trained on this data)

> Contains information from OpenStreetMap, which is made available under the Open Database License (ODbL). © OpenStreetMap contributors, https://www.openstreetmap.org/copyright.
> Training examples derived from the libpostal parser training data of 2017-03-04 (https://archive.org/details/libpostal-parser-training-data-20170304), OpenStreetMap addresses.

## What was made

`AddressChallenger` (converter version `challenger-osm-2026-11-06.1`), seed 20261106, selection salt `0x4348414C4C534C`, split salt `0x4348414C535031`, dev = `hash(line id, split salt) mod 1000 < 20`. Target 250,000 per country (selected lines, before the duplicate drop and the split). Run on the build worker 2026-11-06 (single thread, three passes of 9 to 17 minutes): `count` (selection SHA-256 `aebf256b642a63eaa7c485824f870a75dcf257e1ea41e194e9f2b23cd63600f7`), `render` (manifest SHA-256 `cd0bbe84c9960847264af43db8c5fc3a407352330e3e9a66a0e65f90e2d97eea`), `check` (span check **0 failures** on 5,788,675 lines; 0 against the split rule), `verify` (a second render gave identical bytes in all 100 data files, the manifest and the reject counts). Output SHA-256 of every file: the manifest `manifest.json`.

**5,672,707 train and 115,968 dev examples in 50 country files.**

| Country | Lines in file | Usable | Train | Dev |
|---|---|---|---|---|
| US | 44,714,019 | 38,255,501 | 204,190 | 4,128 |
| CA | 13,596,561 | 13,131,885 | 199,658 | 4,041 |
| PR | 303,930 | 291,840 | 175,750 | 3,558 |
| GU / VI / AS / MP | 6,647 / 7,412 / 1,477 / 678 | 4,967 / 2,833 / 1,379 / 514 | 3,182 / 2,044 / 486 / 389 | 67 / 46 / 13 / 10 |
| MX | 505,679 | 402,137 | 171,622 | 3,589 |
| AU | 1,345,117 | 1,110,676 | 186,350 | 3,901 |
| GB | 7,924,880 | 6,809,682 | 202,989 | 4,140 |
| JE / GG / IM | 0 / 829 / 19,653 | 0 / 772 / 15,418 | 0 / 527 / 10,480 | 0 / 15 / 235 |
| AT | 5,767,865 | 5,403,727 | 180,877 | 3,725 |
| BE | 6,036,490 | 5,145,731 | 183,247 | 3,879 |
| HR | 358,429 | 317,544 | 153,243 | 3,064 |
| CZ | 11,828,659 | 11,745,285 | 187,634 | 3,716 |
| DK | 9,437,695 | 9,388,100 | 169,128 | 3,456 |
| EE | 1,114,168 | 1,102,270 | 154,116 | 3,073 |
| FI | 1,121,221 | 1,009,557 | 170,370 | 3,485 |
| FR | 7,663,748 | 7,509,802 | 193,763 | 3,887 |
| GP / MQ / GF / RE / YT | 12,767 / 10,365 / 22,026 / 112,236 / 1,692 | 10,371 / 8,607 / 19,041 / 105,466 / 1,340 | 7,086 / 5,932 / 12,373 / 67,967 / 934 | 134 / 95 / 225 / 1,357 / 20 |
| DE | 60,429,237 | 57,017,399 | 202,755 | 4,135 |
| HU | 1,021,142 | 891,898 | 161,348 | 3,370 |
| IE | 855,140 | 694,914 | 181,350 | 3,747 |
| IT | 6,583,054 | 6,500,989 | 186,875 | 3,815 |
| LV | 424,215 | 405,903 | 145,465 | 3,039 |
| LT | 730,097 | 726,046 | 164,980 | 3,340 |
| LU | 198,278 | 180,294 | 119,207 | 2,433 |
| MT | 11,133 | 10,970 | 7,107 | 157 |
| NL | 42,204,324 | 41,984,369 | 215,739 | 4,498 |
| PL | 27,255,347 | 25,981,739 | 181,025 | 3,733 |
| PT | 416,868 | 403,578 | 174,726 | 3,648 |
| RO | 1,136,122 | 1,126,383 | 179,391 | 3,588 |
| SK | 2,148,350 | 2,016,011 | 142,525 | 2,853 |
| SI | 461,357 | 440,454 | 148,321 | 3,079 |
| ES | 3,206,656 | 3,065,612 | 187,656 | 3,909 |
| SE | 2,402,849 | 2,154,008 | 181,921 | 3,682 |
| CH | 3,408,121 | 3,245,541 | 182,964 | 3,703 |
| NO | 10,298,708 | 10,230,247 | 179,573 | 3,723 |
| IS | 133,346 | 128,883 | 78,307 | 1,517 |
| LI | 7,086 | 5,984 | 3,843 | 78 |
| AD | 3,087 | 2,529 | 1,665 | 31 |
| MC | 1,599 | 1,295 | 977 | 19 |
| SM | 535 | 462 | 382 | 10 |
| VA | 398 | 350 | 268 | 2 |

(Train and dev are after the 2,445,036 duplicates dropped and the conversion rejects; the largest countries land at 140,000 to 216,000 train rather than 250,000 because the duplicates are about 30 % of the lines drawn.)

**Left out of the file as a whole** (320,115,455 lines): lines with a `category` token 14,204,870, `near` 268,064, `country_region` 318,180, `island` 11,545, `world_region` 132 (the first such token found is the reason counted); a word outside the Latin script 1,434,411; no field other than the country 3,670; empty word 37; a word over 64 characters 50; over 40 tokens 30 (counts over the countries in scope; `rejects-count.tsv`). Countries outside the list, including Greece, Bulgaria and Cyprus, are never read.

## Gaps stated plainly

* **Jersey has no lines** in the file (it is coded under another country there, or absent); Guernsey (527), the Isle of Man, Liechtenstein, Andorra, Monaco, San Marino, the Vatican, the US territories other than Puerto Rico, and the French overseas departments other than Réunion are small, and their dev files hold between 2 and 235 lines.
* The file is from March 2017; its labels are noisy (street numbers glued into the road, a venue name tagged as a street, a postcode tagged with its prefix). Nothing was repaired.
* The country coverage, noise and label set differ from the register-based sets; the road is not split into type and name.
* No evaluation item of ours is in this data: it contains no crawled text and no private sample. A check against our evaluation sets is not possible from here (they are private); the OpenStreetMap lines and our Common Crawl-based evaluation hosts are different populations.
