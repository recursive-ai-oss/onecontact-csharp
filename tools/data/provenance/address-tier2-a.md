# Provenance: address training data, Tier 2 group A (`address-tier2-a`)

Training examples for the address-field head: France (with the overseas departments), Belgium, the Netherlands, Luxembourg, Germany (North Rhine-Westphalia), Austria, Switzerland, Liechtenstein and Monaco, rendered from open registers through our own templates (`tools/data/AddressTier2A`, `tools/data/templates/tier2a-names.tsv`). How the examples look: `docs/training-data-address-tier2-a.md`. This file is the committed record; `manifest.json` (every file hash, the seed, the rates, the counts) is written by `render` and kept with the generated data.

Every register was fetched on 2026-11-05 by `tools/data/fetch-tier2a.sh` (sizes and SHA-256 in `tools/data/pins/<register>.pin`). Licences were read at the publisher on that date. No ODbL, OpenStreetMap, OpenAddresses or crawled text is used; the BeST files used are the BeST CSV exports named `openaddress-*` on the BeST download page, which are the BeST registers themselves, not OpenAddresses data.

## Registers

| Country | Register and file | Pin | Terms (quoted) | Verdict |
|---|---|---|---|---|
| FR | **Base Adresse Nationale (BAN)**, `adresses-france.csv.gz`, as published 2026-11-05 | 944,722,728 bytes, SHA-256 `acc4d57d4de8f67e6511bf0aa74e6257f157396740361f3c3a4f56190fdb9bfe`; `https://adresse.data.gouv.fr/data/ban/adresses/latest/csv/adresses-france.csv.gz` | data.gouv.fr dataset "Base Adresse Nationale" (id `5530fbacc751df5ff937dddb`), licence field `lov2` (Licence Ouverte v2.0); adresse.data.gouv.fr: "Sauf mention explicite de propriété intellectuelle détenue par des tiers, les contenus de ce site sont proposés sous licence etalab-2.0" | OK with attribution |
| BE | **BeST Address**, CSV exports `openaddress-bebru.zip`, `openaddress-bevlg.zip`, `openaddress-bewal.zip` (updated 2026-11-04) | 18,159,622 / 153,213,005 / 59,370,144 bytes; hashes in `pins/best.pin`; `https://opendata.bosa.be/download/best/` | opendata.bosa.be, above the BeST download links: "Licentie: Creative Commons 4.0 Attribution" (the page's GeoPackage and WMS lines say "enkel niet-commercieel gebruik"; those are not used) | OK with attribution |
| NL | **BAG** (Basisregistratie Adressen en Gebouwen), members `9999NUM`, `9999OPR`, `9999WPL` of the PDOK extract `lvbag-extract-nl.zip`, stand 2026-10-08 | 356,519,790 / 11,314,323 / 23,172,581 bytes; hashes in `pins/bag.pin`; read out of the zip by `fetch-bag.py` | PDOK Atom feed "Adressen ATOM": `<rights>https://creativecommons.org/publicdomain/zero/1.0/deed.nl</rights>` | OK (CC0) |
| LU | **Adresses géoréférencées (BD-L-TC)**, `addresses.csv` of 2026-11-02, data.public.lu | 28,209,815 bytes, SHA-256 `f163ed856200eedd8b501da2d7787e01ee373be1a55a4d79abcf57c96982a99c` | data.public.lu API: licence `cc-zero`; site footer: "Unless otherwise stated, all content of this site is available under Creative Commons CC0 license" | OK (CC0) |
| AT | **Österreichisches Adressregister**, relational tables, Stichtagsdaten 01.10.2026 (BEV) | 118,545,233 bytes, SHA-256 `3696f8de70d69847d9f305330500f45836cea3e59884693bdd52fa6220f8f0e1`; `https://data.bev.gv.at/download/Adressregister/Adresse_Relationale_Tabellen_Stichtagsdaten.zip` | In the zip, `Nutzungsbedingungen.txt`: "werden uneingeschränkte Nutzungsrechte unter der Bedingung eingeräumt, dass in allen Abgabeformen und allen Folgeprodukten auf die Schutzrechte des Adressregisters und den Stichtag des Datenauszuges in folgender Form hingewiesen wird: „© Österreichisches Adressregister, Stichtagsdaten vom TT.MM.JJJJ"."; the BEV catalogue record: "Für dieses Produkt gilt die Standardlizenz CC-BY-4.0" | OK with attribution |
| CH, LI | **Amtliches Verzeichnis der Gebäudeadressen** (swisstopo, ID197), CSV for Switzerland and for Liechtenstein, built 2026-11-04 | 143,454,035 bytes (`…_ch_2056.csv.zip`, SHA-256 `4e9e1280090253366680217a86fdd4f2428900a4c867af414f9bbbfbca582404`), 386,473 bytes (`…_li_2056.csv.zip`, `6458d8948aeccce0b55d7edb3cdfebb7dacb9a088dd9fd95ea67ab7ea634c903`); `https://data.geo.admin.ch/ch.swisstopo.amtliches-gebaeudeadressverzeichnis/` | swisstopo "Terms of use for free geodata and geoservices" (linked from the dataset's STAC record): "The free geodata and geoservices of swisstopo may be used, distributed and made accessible. Furthermore, they may be enriched and processed and also used commercially." and "A reference to the source is mandatory." | OK with the source reference. The terms add that for some data sets the exact wording is in that data set's documentation; the STAC record names these terms and no other. |
| DE (NRW) | **Gebäudereferenzen** (Geobasis NRW), ASCII package for all of NRW, 2026-07-28 | 97,015,231 bytes, SHA-256 `8f3cc3398799c2e6358f22e1272e435c8dcc3375de1597da1b3339251d4692a8`; `https://www.opengeodata.nrw.de/produkte/geobasis/lk/akt/gebref_txt/gebref_EPSG25832_ASCII.zip` | opengeodata.nrw.de directory page: "Geobasis NRW stellt diese Daten unter der Datenlizenz Deutschland - Zero - Version 2.0 bereit; jede Nutzung ist ohne Einschränkungen oder Bedingungen zulässig." | OK (dl-de/zero-2-0) |
| MC | none: synthesized (`Monaco.cs`: public street names, hash-drawn numbers, postcode 98000) | — | no source data | — |

**Attribution lines for the model notices:** "Source : Base Adresse Nationale (BAN), licence ouverte 2.0." · "BeST Address, FPS Policy and Support (BOSA), with the address registers of the Brussels-Capital Region (Paradigm), Flanders (Digitaal Vlaanderen) and Wallonia (SPW), CC BY 4.0." · "© Österreichisches Adressregister, Stichtagsdaten vom 01.10.2026 (BEV), CC BY 4.0." · "Quelle: Bundesamt für Landestopografie swisstopo, Amtliches Verzeichnis der Gebäudeadressen." · BAG, BD-L-TC and Geobasis NRW (CC0, CC0, dl-de/zero) need no attribution; credit is given anyway: "BAG, Kadaster", "Administration du cadastre et de la topographie (data.public.lu)", "Geobasis NRW".

## What was used

Counts come from `selection.tsv` (usable records after cleaning) and the manifest (rendered examples). A per-region sampling rate gives every region the same number of records (small regions whole) up to the country's target; a record whose address repeats one already kept is dropped (8,458 in all).

| | Usable records | Regions (sampling strata) | Train | Dev | Languages (examples) |
|---|---|---|---|---|---|
| FR | 26,025,435 | 101 départements (971–976 included) | 302,644 | 6,180 | fr |
| BE | 6,659,849 | BRU, VLG, WAL | 205,992 | 4,233 | fr 108,414; nl 100,420; de 1,391 |
| NL | 11,233,493 | 90 postcode areas | 246,529 | 5,118 | nl |
| LU | 179,752 | 100 communes | 127,140 | 2,648 | lb (French-typed streets are number-first) |
| DE | 4,509,543 | 5 Regierungsbezirke of NRW | 254,329 | 5,166 | de |
| AT | 2,509,227 | 9 federal states | 254,853 | 5,189 | de |
| CH | 2,468,406 | 26 cantons | 254,969 | 5,337 | de 194,721; fr 52,691; it 12,894 |
| LI | 12,733 | LI (taken whole) | 12,475 | 258 | de |
| MC | 6,000 synthetic draws | MC | 5,887 | 113 | fr |

Rows left out and why are counted in `rejects-count-*.tsv` (France: collectivities outside the départements 74,441, no house number 5,113, missing street/postcode 16,462; Belgium: status not current 521,245; Netherlands: status not "Naamgeving uitgegeven" 736,433, missing number, postcode or street 1,407,234; Switzerland: a number written with a dot, `31.1`, 715,208, not a real address 51,294, missing fields 72,804; Austria: no house number or postcode 19,995; Liechtenstein: not real 233; Luxembourg: 16).

**Gaps:** Germany is one state, and its postcodes are synthetic (the file has none): two leading digits from the municipality inside NRW's real ranges (32–33, 40–48, 50–53, 57–59), three from the street, a fixed hash. Other states publish under `dl-de/by-2-0` in other formats (GML, per-county files) and are not fetched. Monaco is synthetic. The French collectivities (975, 977, 978, 98x) are not rendered.

## Generator

`tools/data/AddressTier2A` (renderer version `tier2a-2026-11-05.1`), seed 20261105, dev = `hash(record id, split salt) mod 1000 < 20` taken before rendering, selection = `hash(record id, selection salt) < rate`. Name parts for recipient and organisation lines are our own (`tier2a-names.tsv`, SHA-256 in the manifest). Run 2026-11-05 on the build worker: `selection.tsv` SHA-256 `9c3a40e065f8bf61fcbc320e59c84299b07306f4832836b7d902fe4716839aa3`; 1,699,060 examples (1,664,818 train, 34,242 dev), span check 0 failures; a second render from the same selection gave identical bytes in all 19 files and the manifest. The United States and Canada tool shares the tokenizer, random generator, example type and span rules (moved into their own files, same code): a Canada re-render with the changed tool is byte-identical to the one from the unchanged tool (`ca.train.jsonl` SHA-256 `9932d4a36ed2151e39e5c8ee0bd491d2904e0f50cdd714fdff269ee4d6409bb9`, 369,786 lines; `ca.dev.jsonl` `eb448336b025982e6ab07177655059512f00310a0f8e053fd7bdefe8143b4053`, 7,562).

Evaluation data never trains: these files are `train` and `dev`; the Tier 2 evaluation sets are separate and drawn from records held out before rendering when they are made.

## Rule parser and structural layer on a sample

300 dev examples per country (all of LI's 258, MC's 113), through the library's current rule parser (`RuleAddressParser.TryParse`) with no hint and with the country as the hint, and `PostalAddress.Validate` on the labelled fields:

| Country | No hint | With the country as hint | Structural check of the labelled fields |
|---|---|---|---|
| AT, BE, LU, MC, NL | all refused, `address.no-last-line` | all refused, `address.unsupported-country` | all sound |
| CH, DE, LI | refused: `address.no-last-line` for all but 3, 2, 1 (`address.model-required`) | all refused, `address.unsupported-country` | CH, DE sound; LI 229 of 237, the 8 are postcode 9498 (Planken), which the pinned libaddressinput pattern `(948[5-9])\|(949[0-7])` leaves out |
| FR | refused, `address.no-last-line`, except 3 parsed (none with the right country) | all refused | all sound |

So outside the United States and Canada the rule parser finds no last line, never names the country, and with a hint says the country is unsupported; the structural layer accepts every labelled postcode except the one it lacks. The full table is in `rule-sample.md` beside the data.
