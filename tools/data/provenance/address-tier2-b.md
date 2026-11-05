# Provenance: address training data, Tier 2 group B (`address-tier2-b`)

Training examples for the address-field head: the United Kingdom (Great Britain), Spain, Italy, Portugal, Ireland, Malta, San Marino, the Vatican, Andorra, Jersey, Guernsey and the Isle of Man, rendered through our own templates (`tools/data/AddressTier2B`, `tools/data/templates/tier2b-names.tsv`, `tier2b-streets.tsv`). How the examples look: `docs/training-data-address-tier2-b.md`. This file is the committed provenance log for them.

Every source was fetched on 2026-11-05 by `tools/data/fetch-tier2b.sh` (sizes and SHA-256 in `tools/data/pins/<source>.pin`). Licences were read at the publisher on that date. No ODbL, OpenStreetMap, OpenAddresses or crawled text is used; no Royal Mail PAF, OS AddressBase, Eircode database or G-NAF.

## Sources

| Country | Source and file | Pin | Terms (quoted) | Verdict |
|---|---|---|---|---|
| GB | **OS Open Names**, `opname_csv_gb.zip` (product version 2026-10: roads, postcode units and populated places of Great Britain) | 103,312,081 bytes, SHA-256 `81ee5ba2c96d8fe5ddbf83fb4f9928e8740563618c6c5eb9376280b399a74532`; `https://api.os.uk/downloads/v1/products/OpenNames/downloads?area=GB&format=CSV&redirect` | In the zip, `Doc/licence.txt`: "Your use of OS OpenData is subject to the terms at http://os.uk/opendata/licence." and the acknowledgements "Contains OS data © Crown Copyright and database rights 2026. Contains Royal Mail data © Royal Mail copyright and database right 2026. Contains National Statistics data © Crown copyright and database right 2026". ordnancesurvey.co.uk product support: "OS OpenData is licensed under the Open Government Licence, which allows you to use the OS OpenData datasets in any way and for any purpose." | OK (OGL v3, with the three acknowledgements) |
| ES | **Catastro INSPIRE addresses**, 917 municipal files `A.ES.SDGC.AD.<code>.zip` (feed of 2026-08-21; 12 % of the municipalities, chosen by a hash of the code in `make-catastro-pin.py`) | 83,288,948 bytes in all; each file's size and hash in `pins/catastro.pin` (pin file SHA-256 `552de359ecbf8223…`); `https://www.catastro.hacienda.gob.es/INSPIRE/Addresses/` | The provincial Atom feeds' `<rights>`: "This service can be used free of charge in every instance, as long as that the D. G. of the Cadastre (Ministry of Finance) is mentioned as author and owner of the information" | OK with attribution |
| IT | **ANNCSU indirizzario**, regional files `INDIR_<region>.zip` (20 files, built 2026-11-02; Agenzia delle Entrate and Istat) | 9.9 MB to 37.1 MB each, 336,353,181 bytes in all; hashes in `pins/anncsu.pin`; `https://anncsu.open.agenziaentrate.gov.it/age-inspire/opendata/anncsu/getds.php?INDIR_<REGION>` (links from `anncsu.gov.it/it/consultazione-dellarchivio/open-data/Accedi-ai-servizi-di-dowload-massivo-in-Open-data/`) | anncsu.gov.it, Ricerca strade e numeri civici: "Il servizio di consultazione dei dati dell'ANNCSU è disponibile con licenza CC-BY 4.0." The bulk-download page itself states no licence; the same page family describes the files as open data under the EU high-value-datasets regulation. | OK with attribution; **the bulk page's silence is raised for confirmation** (the Italian examples are one file pair and can be dropped) |
| IT | **ISTAT, Elenco dei comuni italiani**, `Elenco-comuni-italiani.csv` | 1,126,843 bytes, SHA-256 `57eaf945182fc64fa05f80f1a5fb2a84cff55ebde2af6e3947a731776e7809e0`; `https://www.istat.it/storage/codici-unita-amministrative/Elenco-comuni-italiani.csv` | istat.it, Note legali: "Licenza CC-by Creative Commons 4.0 … Salvo diversa indicazione, tutti i contenuti pubblicati su questo sito sono soggetti alla licenza Creative Commons – Attribuzione – versione 4.0" | OK with attribution |
| IT, PT, IE, MT, SM, VA, AD, JE, GG, IM | **GeoNames postal codes**, `<CC>.zip` (IT 256,926 bytes; PT 1,078,711; IE 3,716; MT 2,922; SM 1,791; VA 1,483; AD 1,629; JE 1,543; GG 1,681; IM 2,240) | hashes in `pins/geonames.pin`; `https://download.geonames.org/export/zip/<CC>.zip` | download.geonames.org/export/zip/readme.txt: "This work is licensed under a Creative Commons Attribution 4.0 License. This means you can use the dump as long as you give credit to geonames (a link on your website to www.geonames.org is ok)". The Royal Mail line in the readme is for the full United Kingdom file, which is not used. | OK with attribution |
| PT, IE, MT, SM, VA, AD, JE, GG, IM | none for streets: our own vocabulary (`tier2b-streets.tsv`, 311 lines) and name parts (`tier2b-names.tsv`) | — | written for these templates | — |

**Attribution lines for the model notices:** "Contains OS data © Crown copyright and database right 2026. Contains Royal Mail data © Royal Mail copyright and database right 2026. Contains National Statistics data © Crown copyright and database right 2026." · "Fuente: Dirección General del Catastro (Ministerio de Hacienda), INSPIRE Direcciones." · "Archivio Nazionale dei Numeri Civici e delle Strade Urbane (ANNCSU), Agenzia delle Entrate e Istat, CC BY 4.0." · "Istat, Elenco dei comuni italiani, CC BY 4.0." · "GeoNames postal codes, www.geonames.org, CC BY 4.0."

Not used, and why: the Portuguese Base Nacional de Moradas (dados.gov.pt does not complete a TLS handshake from the build network, so the terms and files could not be read); the ONS Postcode Directory (a third-party guide reports different terms for Northern Ireland postcodes, and OS Open Names already carries the postcode units); Eircode and the Irish Local Road Network (no street names usable as addresses); the live ANNCSU query API (a per-street service, not a bulk dataset).

## What was used

Counts come from `selection.tsv` (usable records after cleaning) and the manifest (rendered examples). A per-region sampling rate gives every region the same number of records (small regions whole) up to the country's target; outside the synthetic countries a record whose address repeats one already kept is dropped (5,370 in all).

| | Usable records | Regions (sampling strata) | Train | Dev | Languages (examples) |
|---|---|---|---|---|---|
| GB | 1,632,649 (two postcode pairings per named road) | 11: nine English regions, Scotland, Wales | 292,728 | 5,930 | en 297,026; cy 1,589; gd 43 |
| ES | 907,660 | 41 provinces (and three office codes) | 289,637 | 5,920 | es 259,061; ca 19,775; gl 16,721 |
| IT | 26,084,963 | 20 regions | 293,153 | 6,056 | it 295,897; de 3,312 |
| PT | 200,000 draws | 20 distritos and regions | 195,900 | 4,100 | pt |
| IE | 100,000 draws | 4 provinces | 97,944 | 2,056 | en |
| MT | 40,000 draws | MT | 39,145 | 855 | mt 26,226; en 13,774 |
| SM | 8,000 draws | SM | 7,837 | 163 | it |
| VA | 3,000 draws | VA | 2,940 | 60 | it |
| AD | 20,000 draws | AD | 19,633 | 367 | ca 17,613; es 1,440; fr 947 |
| JE | 10,000 draws | JE | 9,780 | 220 | en |
| GG | 10,000 draws | GG | 9,793 | 207 | en |
| IM | 10,000 draws | IM | 9,808 | 192 | en |

Rows left out and why are counted in `rejects-count-*.tsv` (Great Britain: roads without a name, place or region 67,738, no postcode unit of the district nearby 10; Spain: a house number with letters or signs 521,277 (`S/N`-style entries and similar), no postcode 323,041, a parenthesised article or an unlisted type code about 450,000, a number over four digits 268; Italy: no CAP for the comune 1,042,919, no street or no plain civic number 281,533, a comune code not in the ISTAT list 4,337).

**Gaps:** see `docs/training-data-address-tier2-b.md`: no Basque Country or Navarre, no Northern Ireland, Italian CAPs drawn for cities, synthetic streets in nine countries, no counties in Ireland.

## How it was made

`tools/data/AddressTier2B` (renderer version `tier2b-2026-11-05.1`), seed 20261105, dev = `hash(record id, split salt) mod 1000 < 20` taken before rendering, selection = `hash(record id, selection salt) < rate`. Name parts for recipient and organisation lines are our own (`tier2b-names.tsv`, SHA-256 `2d13562254b057ab…`; `tier2b-streets.tsv` `7d83d9c493dc47e3…`). `selection.tsv` SHA-256 `fea066d91b2f17d3…`. Run 2026-11-05 on the build worker: 1,294,424 examples (1,268,298 train, 26,126 dev), span check 0 failures; a second render from the same selection gave identical bytes in all 24 data files and the manifest. Output file SHA-256 (the manifest `manifest.json`, `5cec8340b6befcf0…`, lists all of them): `gb.train.jsonl` `e7f8724aec85…`, `es.train.jsonl` `20dd7344808a…`, `it.train.jsonl` `f5e3f935f77a…`, `pt.train.jsonl` `365b18ab7550…`, `ie.train.jsonl` `5447cf1671b7…`. The group A and United States/Canada tool files were not edited; a Monaco re-render with the group A tool is byte-identical to the one in its provenance (`mc.train.jsonl` `3bc262e747c2…`).
