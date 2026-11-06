# Provenance: address training data, Tier 2 group C (`address-tier2-c`), first part

Training examples for the address-field head: Norway, the Czech Republic, Latvia and Estonia, rendered from open registers through our own templates (`tools/data/AddressTier2C`, `tools/data/templates/tier2c-names.tsv`). How the examples look: `docs/training-data-address-tier2-c.md`. This file is the committed record; `manifest.json` (every file hash, the seed, the rates, the counts) is written by `render` and kept with the generated data. Licences were read at the publisher on 2026-11-05. No ODbL, OpenStreetMap, OpenAddresses, G-NAF or crawled text is used.

## Registers

| Country | Register and file | Pin | Terms (quoted) | Verdict |
|---|---|---|---|---|
| NO | **Matrikkelen - Adresse**, Kartverket, CSV for the whole country, 2026-10-31 | 157,167,173 bytes, SHA-256 `f1350db41f88e242a41f849da0eb01f9b9a9f1b9927176ded4fb65dff5291c82`; `https://nedlasting.geonorge.no/geonorge/Basisdata/MatrikkelenAdresse/CSV/Basisdata_0000_Norge_25833_MatrikkelenAdresse_CSV.zip` | data.norge.no dataset 8f1151bf-ad63-3e47-adbb-143f876776ee, field "License": "Creative Commons Attribution 4.0 International" ("Open license") | OK with attribution |
| CZ | **RÚIAN address places**, CSV by municipality, 2026-10-31 | 63,371,330 bytes, SHA-256 `56dc1392033cc6ab21e7f2bafc553c2837d49e62049a60a0f238a0ba83c27877`; `https://vdp.cuzk.gov.cz/vymenny_format/csv/20261031_OB_ADR_csv.zip` | ČÚZK geoportal metadata `CZ-00025712-CUZK_SERIES-MD_RUIAN-CSV-ADR-OB`: "Datová sada je poskytována jako otevřená data (licence CC-BY 4.0). Data vychází z RÚIAN" | OK with attribution |
| LV | **VARIS buildings and land parcels**, `aw_eka.csv`, resource modified 2026-11-05 | 141,786,302 bytes, SHA-256 `42fdfcba04fd49fc0ffcb26ca37dbd700298ca2bbf93b8d2013025056c92dea1`; `https://data.gov.lv/dati/dataset/6b06a7e8-dedf-4705-a47b-2a7c51177473/resource/a510737a-18ce-400f-ad4b-04fce5228272/download/aw_eka.csv` | data.gov.lv dataset "varis-atvertie-dati" (publisher: Valsts zemes dienests), CKAN `license_id`: "CC-BY-4.0" | OK with attribution |
| EE | **ADS address versions with components**, `AADRESSID_KOMPONENTIDEGA.csv`, extract of 2026-11-05 from the Land and Spatial Board's public SFTP (login published in its `readme.pdf`) | 1,320,502,209 bytes, SHA-256 `38677267cc00861ef501f0334ab8d098b4b875b8f89e7af3bfc6f66be2f9940b`; `sftp://aksfailid.maaruum.ee/public/ADS_valjavotted/ADS_valjavotted/AADRESSID_KOMPONENTIDEGA.csv` | geoportaal.maaruum.ee/est/abi-ja-juhised/andmed: "Kõik maa- ja ruumiameti kogutavad ruumiandmed (näiteks katastriandmed, topograafilised andmed, aadressiandmed, …) on avaandmed … andmete kasutamisele kohaldub maa- ja ruumiameti avatud ruumiandmete litsents, mille järgi võib andmeid tasuta kasutada mistahes seaduslikul (isiklikul või ärilisel) eesmärgil, viidates alati andmete päritolule." The same page asks for a credit such as "Maa- ja Ruumiamet 2025" | OK with attribution. The licence text itself was read through this summary; the licence page was not found separately (follow-up) |

The Latvian, Norwegian and Czech files are published at "latest" URLs, so a later fetch finds different bytes and stops on the hash mismatch; the pinned files were kept in the data cache.

Attribution lines for the model notices: "Matrikkelen - Adresse, Kartverket (CC BY 4.0)"; "RÚIAN, Český úřad zeměměřický a katastrální (CC BY 4.0)"; "Valsts adrešu reģistra informācijas sistēma, Valsts zemes dienests (CC BY 4.0)"; "Aadressiandmete süsteem, Maa- ja Ruumiamet 2026".

## Rendered output (seed 20261105, renderer `tier2c-2026-11-05.1`, dev 2 %)

| Country | Usable records | Train | Dev |
|---|---|---|---|
| NO | 2,570,275 | 244,721 | 5,003 |
| CZ | 2,617,012 | 244,673 | 4,940 |
| LV | 549,725 | 147,235 | 3,082 |
| EE | 1,375,329 | 146,380 | 3,005 |

Span check: 799,039 examples, 0 failures. A second render gave byte-identical files and manifest. File hashes: `manifest.json` beside the generated data. Selection file `selection.tsv`: SHA-256 `9ee638bb8e6e4308a194c147a98a95ca59511f8dd154775be932dbfba23f0e1f`. Rejected records, with reasons, are in `rejects-*.tsv` beside the output (Estonia: 1,992,829 superseded or cancelled address versions; Czech Republic: 406,573 registration numbers).

## Found out about the countries not yet loaded (2026-11-05)

* **Lithuania.** data.gov.lt lists the Registrų centras address register text data ("Adresų registro ... tekstiniai duomenys su koordinatėmis", updated 2026-01-22) and gives other Registrų centras sets as "Licencija — CC BY 4.0"; the dataset's own licence line and a download link were not reached.
* **Mexico.** INEGI's DENUE state files (`https://www.inegi.org.mx/contenidos/masiva/denue/denue_<nn>_csv.zip`) download with a browser user agent; the "términos de libre uso" text was not read yet.
* **Poland.** PRG address points and streets are distributed as SHP packages per municipality, county and voivodeship through geoportal.gov.pl ("Dane do pobrania") and listed as free data by GUGiK (gugik.gov.pl "dane bez opłat"). The licence wording was not read. The attribute table is a .dbf, which a loader can read without a shapefile library.
* **Slovenia.** The Register prostorskih enot (house numbers, streets, postal districts) is listed on podatki.gov.si and e-prostor.gov.si; a third-party repository describes it as CC BY 4.0 (GURS). The licence line on the publisher's own page and a download link were not reached.
* Slovakia, Croatia, Hungary, Romania and Australia: not yet investigated.

## Denmark, Sweden, Finland (with Åland) and Iceland (read 2026-11-05)

Finland and Iceland come from their open registers; Denmark and Sweden are synthesized, because their registers are only distributed behind an account. Rendered with the same tool, seed (20261105), renderer and dev split as above; `SHA256SUMS` and `manifest.json` are kept beside the generated data.

| Country | Source and file | Pin | Terms (quoted) | Verdict |
|---|---|---|---|---|
| FI | **DVV building addresses** (`Rakennusten osoitetiedot`), the shapefile table `osoiteet_2025.dbf` of the extract of 2025-02-03 (3,872,156 buildings), as mirrored by CSC/Funet at `http://www.nic.funet.fi/index/geodata/dvv/osoitteet/2025/`. DVV ended its own distribution on 2025-03-14 (`rakennukset_readme.txt` in the same folder), so this is the last release | 872,827,638 bytes, SHA-256 `7ff48ffaa3db0c45f6ff38f4b966b6400fb89117b9595711ade6f53ef60c88cf`, `pins/fi.pin` | Licence file `http://www.nic.funet.fi/index/geodata/dvv/DVV_ehdot_CC.txt`: "Tämä Väestörekisterikeskuksen aineisto on lisensoitu Creative Commons Nimeä 4.0 Kansainvälinen -lisenssillä." and "This dataset by Population Register Centre is licensed under a Creative Commons Attribution 4.0 International License." The record layout is the publisher's description `rakennusten-osoitetiedot-avoin-data-kuvaus-tiedoista-2020-02-14.pdf` | OK with attribution |
| IS | **Staðfangaskrá** (address register), `Stadfangaskra.csv` from the HMS download page `https://hms.is/gogn-og-maelabord/grunngogntilnidurhals/stadfangaskra`, file `https://hmsstgsftpprodweu001.blob.core.windows.net/fasteignaskra/Stadfangaskra.csv` (Last-Modified 2026-11-05, updated every Sunday, pinned by bytes) | 38,143,751 bytes, SHA-256 `962d00c2d8954bca3dcb2132c1ce7ede09e12cceb93679dede36380d6abbf3bf`, `pins/is.pin` | The register's entry in the national open data catalogue (`data.europa.eu`, dataset `stadfangaskra`, publisher Registers Iceland, catalogue "Open Data Iceland") lists the distribution with licence `http://www.opendefinition.org/licenses/cc-by`. The HMS page itself states no licence; it gives the column descriptions. Not quoted from the HMS page: that is the one weak point | OK with attribution (accepted on the catalogue entry; the licence is not on the download page) |
| DK, SE, FI, IS | **GeoNames postal codes** `DK`, `SE`, `FI`, `IS`, `AX` | `pins/nordic.pin` (5 files, sizes and SHA-256 there) | As logged for group B: GeoNames postal codes, Creative Commons Attribution 4.0 | OK with attribution |
| DK | Danmarks Adresseregister (DAR), file download on Datafordeler | not used | datafordeler.dk, "DAR Fildownload": "Hent prægenererede filer med entiteterne ved at logge på Datafordelerens Administration, oprette en bruger og et IT-system med tilhørende API key"; terms of use: "Som bruger af frie grunddata er du underlagt CC BY 4.0 licens". The INSPIRE address download is an ATOM service whose files are fetched with a user name and password (FME support article, 2026-08). DAWA is shut down (HTTP 410) | **Not used.** The licence is open, but every bulk route needs an account and API key, which this build does not create. Denmark is synthesized (below) |
| SE | Lantmaterietets **Belägenhetsadress Nedladdning** (location address download, free since 2025-02-03) | not used | lantmateriet.se product page: "Produkten är avgiftsfri och får användas enligt användningsvillkor för värdefulla datamängder som innehåller personuppgifter"; opendata.lantmateriet.se: "för att ladda ner Lantmäteriets öppna data ... behöver du ha ett användarkonto"; Geoforum, 2025-04-24: "Många kräver att användningen först prövas i enlighet med till exempel gdpr" | **Not used:** it is not the CC0 open data of the same agency but a separate set under personal-data use conditions, reached through an account. Sweden is synthesized (below) |

Attribution lines for the model notices: "Rakennusten osoitetiedot, Digi- ja väestötietovirasto (Väestörekisterikeskus), CC BY 4.0"; "Staðfangaskrá, HMS / Þjóðskrá Íslands, CC BY"; "GeoNames postal codes, www.geonames.org, CC BY 4.0".

**What is real and what is drawn.** FI: street names, numbers and postcodes are the register's; the post office name is looked up by postcode in GeoNames (Åland in `AX.zip`). Only addresses with exactly one language are used: Finnish-only (3,096,662 rows) and Swedish-only (47,978 rows, 28,456 of them Åland); the 630,605 bilingual rows are used in Finnish, because GeoNames has no Swedish post office names, so a Swedish street next to a Finnish town would be invented. The Swedish-only rows outside Åland therefore carry the GeoNames (Finnish) town name. Rows with no number (196,866), no street name (156,409), a number other than `12`, `12 B`, `12-14` (1,305) or a postcode GeoNames lacks (172) are dropped. IS: street or farm name (nominative), number and letter are the register's; plots, land, substations and named institutions (13,153) are dropped; the town is looked up by postcode in GeoNames, which writes some in the dative (`Seltjarnarnesi`) and numbers the Reykjavík districts (the number is cut). DK and SE: the postcode area and place name are real (GeoNames, drawn uniformly over the postcodes), the street name is a whole name (35 %) or a stem plus a suffix (65 %) from `templates/tier2c-streets-nordic.tsv`, the number (1–200, letter in 10 %) is drawn, and so is any flat designation (Danish floor and side, Swedish `lgh`). 165,000 records are drawn per country before sampling. Synthetic Danish and Swedish rows therefore teach the layout and the tagging of real place names, not real street names.

### Rendered output

| Country | Records before sampling | Train | Dev |
|---|---|---|---|
| DK | 165,000 | 146,517 | 2,991 |
| SE | 165,000 | 147,019 | 3,059 |
| FI | 3,576,902 | 226,770 | 4,662 |
| IS | 126,368 | 121,374 | 2,491 |

Sampling targets 150,000 (DK, SE, IS) and 250,000 (FI) per country, even over the regions (Denmark's five regions, Sweden's counties, Finland's 19 regions including Åland with all 26,705 of its records at rate 0.49, Iceland's municipalities, small ones whole); repeated addresses dropped (21,849). Span check: 654,883 examples, 0 failures. A second render gave byte-identical files and manifest. The earlier outputs are unchanged: NO, CZ, LV and EE re-rendered with the earlier selection file are byte-identical to the earlier files, and Luxembourg and Liechtenstein of group A re-rendered by the group A tool are byte-identical (the manifest of the group C tool records a different `templatesSha256` now because the names are read from two template files). `selection.tsv` SHA-256 `9e4e60c21b371514a6b85123b62a9dac6ec4826cd7893eba7615829ef654c97d`; `manifest.json` SHA-256 `daa0572ccac9e9253ab21d4366e8772cc6b9dd1db3af35f0c982cea3eccefbb2`; per-file hashes in `SHA256SUMS` beside the output. Rejected records with reasons: `rejects-*.tsv`.

### Rule sample (information)

The library's rule parser over 300 dev examples per country, and the structural layer on the gold fields:

| Country | No hint: parsed | Hint: parsed | Structural check of the gold fields |
|---|---|---|---|
| DK | 0 (country-undetermined 240, no-last-line 59, model-required 1) | 0 (country not yet supported by the rules, 300) | 284/284 sound |
| SE | 1 | 0 (unsupported, 300) | 286/286 sound |
| FI | 3 | 0 (unsupported, 300) | 286/286 sound |
| IS | 0 | 0 (unsupported, 300) | 287/287 sound |

The rules do not know these four countries yet (that work is the standardization units'), so the rows say nothing about the model; they show the gold fields satisfy the structural layer's required fields and postcode patterns.
