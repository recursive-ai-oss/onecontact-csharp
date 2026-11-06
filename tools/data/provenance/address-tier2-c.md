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

* Sweden, Iceland, Slovakia, Croatia, Hungary, Romania and Australia: not yet investigated.


## Group C-3: Croatia, Hungary, Romania, Australia

Rendered by the same tool (`tools/data/AddressTier2C`, renderer file `RendererC3.cs`, templates `tools/data/templates/tier2c3-*.tsv`); fetched by `tools/data/fetch-tier2c3.sh` against `pins/{hr,hu,ro,au}.pin`. Terms were read at the publishers on 2026-11-05. No ODbL, OpenStreetMap, OpenAddresses, G-NAF or crawled text is used.

| Country | Source and file | Pin | Terms (quoted) | Verdict |
|---|---|---|---|---|
| HR | **Adrese (INSPIRE Addresses)**, Državna geodetska uprava, `INSPIRE_Addresses_(AD).zip` (GML 3.2.1: `Address`, `ThoroughfareName`, `PostalDescriptor`, `AdminUnitName`), published 2026-11-01 | 89,968,733 bytes, SHA-256 `dd551d3bb88514cf8ed68fa0e2344dc0b15de8f226733999e875d9588811a591`; `https://geoportal.dgu.hr/services/atom/INSPIRE_Addresses_(AD).zip` | dgu.hr/proizvodi-i-usluge/otvoreni-podaci/6596 lists "ADRESE ... URL: https://geoportal.dgu.hr/services/atom/ad/xml" among its open data and says: "Podaci za ponovnu uporabu u nadležnosti Državne geodetske uprave dostupni su korisnicima bez ograničenja i za slobodnu uporabu uz Otvorenu dozvolu." The Otvorena dozvola: "Informacije i metapodaci mogu se koristiti u komercijalnu i nekomercijalnu svrhu, a osobito: reproducirati, distribuirati ... prilagođavati i povezivati s vlastitim podacima ... u svrhu stvaranja novih skupova podataka"; duties: state the source as the body gave it and the date of the last change, link the dataset, mark changes ("Sadrži informacije tijela javne vlasti u skladu s Otvorenom dozvolom" when no source statement is given), no suggestion of official endorsement. **Caveat:** the Atom feed's own `<rights>` element reads "ograničenje javnog pristupa prema članku 13 stavku 1 točki (e) INSPIRE Direktive" (the INSPIRE metadata wording for a limit on public access); the file downloads without login, and the DGU page above is the licence statement. | OK with attribution (the `<rights>` wording is a caveat, above) |
| RO | **Coduri poștale România**, Infocod September 2016 with SIRUTA, Poșta Română (data.gov.ro dataset `coduri-postale-romania`, resource `29a5c89e-0f23-4a42-aa05-765ef04177b6`, workbook last saved 2016-09-06) | 7,845,888 bytes, SHA-256 `c81651e357d85ecf945d249f84a4d4b6a7b41d652274a4dae41c81fe971d7f37`; fetched from the Internet Archive capture of 2025-12-12 of the data.gov.ro file URL (data.gov.ro itself does not answer from our hosts: connection timeouts from both). Converted sheet by sheet to three tab-separated files by `ro-infocod-to-tsv.py` (xlrd 2.0.1), whose hashes are the `derived` lines of `ro.pin` | The dataset page (captured 2025-12-12) carries the licence badge "OGL-ROU-1.0" for the publisher Poșta Română. data.gov.ro/pages/licence (captured 2026-01-03): "Prin OGL-ROU-1.0, utilizatorilor seturilor de date le este permis: să copieze, publice, distribuie informația; să adapteze informația; să refolosească informația în scop comercial sau necomercial", on condition that the source is stated ("Conține informații publice în baza Licenței pentru Guvernare Deschisă v1.0" when the publisher gave none), no suggestion of official status, no distortion; and "Seturile de date publicate anterior sub licența OGL-ROU 1.0 rămân în continuare sub această licență." | OK with attribution. The data is from 2016 (street names and postcodes only). |
| HU | **GeoNames postal codes for Hungary**, `HU.zip` (3,571 postcode and place rows), retrieved 2026-11-05 | 50,309 bytes, SHA-256 `dd3cbf4c11fc7a1d358e6d8979a59977e7e8e9cbcdecb038f1726697a4b969b8`; `https://download.geonames.org/export/zip/HU.zip` | The archive's `readme.txt`: "This work is licensed under a Creative Commons Attribution 4.0 License. This means you can use the dump as long as you give credit to geonames" | OK with attribution. No open street register was found (the KSH gazetteer lists settlements, not streets; the national address register is not published as open data), so streets are our own list. |
| AU | **GeoNames postal codes for Australia**, `AU.zip` (18,566 postcode, place and state rows), retrieved 2026-11-05 | 287,563 bytes, SHA-256 `0c3949dcd227a9ae932fc93dc251a15d2500502503706a375edf531cce86129c`; `https://download.geonames.org/export/zip/AU.zip` | Same `readme.txt` (CC BY 4.0). **Lineage:** the readme names no source for Australia; GeoNames' Australia page (geonames.org/countries/AU/australia.html) lists its national data providers as ABS (CC BY 4.0), Geoscience Australia and data.gov.au (CC BY, "Incorporates or developed using Administrative Boundaries ©PSMA Australia Limited"); the file's Local Government Area column carries ABS codes. G-NAF is not named anywhere. | OK with attribution; the lineage statement cannot show that no G-NAF-derived field is in the file, so the source is flagged |

Not used: the ABS ASGS Edition 3 suburbs and localities and postal areas (CC BY 4.0, abs.gov.au and data.gov.au). Their own page says "The ASGS Edition 3 Suburbs and Localities are based on the May 2021 localities data available from Geoscape", and Geoscape publishes G-NAF, so the lineage is not clear enough for the no-G-NAF rule; they can be added later if the lineage is accepted (the loader would read `CG_SAL_2021_POA_2021`). No state street dataset was used for Australia: street names and numbers are drawn from our own list. Romanian orthography: the 2016 workbook writes the cedilla letters; the loader turns them into the comma-below letters and the renderer puts the cedilla forms back in 20 % of examples.

Attribution lines for the model notices: "Adrese (INSPIRE), Državna geodetska uprava; sadrži informacije tijela javne vlasti u skladu s Otvorenom dozvolom"; "Coduri poștale România (Infocod, septembrie 2016), Poșta Română, data.gov.ro; conține informații publice în baza Licenței pentru Guvernare Deschisă v1.0"; "Postal codes: GeoNames, geonames.org (CC BY 4.0)". Changes to the sources are marked in the model notices (records rendered as free text, Romanian numbers drawn inside the register's ranges, Hungarian and Australian streets and numbers drawn).

The Croatian file and the Romanian workbook are published at addresses that give different bytes later (a "latest" file; an archive capture), so the pinned files are kept in the data cache and a later fetch stops on a hash mismatch.

### Rendered output (seed 20261105, renderer `tier2c3-2026-11-06.1`, dev 2 %)

| Country | Usable records | Train | Dev |
|---|---|---|---|
| HR | 1,683,411 | 242,082 | 4,867 |
| HU | 149,982 | 146,917 | 2,983 |
| RO | 157,923 | 144,249 | 2,942 |
| AU | 259,826 | 245,223 | 4,910 |

Span check: 794,173 examples, 0 failures. A second render gave byte-identical files and manifest. Selection file `selection.tsv`: SHA-256 `7fde194855769ab9931fde7d6ee84d042e35516b0a345feb08731e5f58043050`; the manifest and file hashes are `SHA256SUMS` beside the data (`manifest.json` `b5d02afe…`). Earlier output unchanged: this build, run over the same registers, reproduces the recorded hashes of the Norwegian files and of the selection file of the first four group C countries (`no.dev.jsonl` `80fffa70…`, `no.train.jsonl` `15f307a2…`, `selection.tsv` `9ee638bb…`) and of Liechtenstein and Monaco from group A.

Counts that are not records: Croatia used every register address (1,683,411 of 1,683,411; nothing rejected); Romania left out 2,564 street rows with a block list or an odd number cell and 202 with a street type that is not in the table ("Alee I" and similar); Australia dropped 7 repeated postcode and place rows.

### Rule parser and structural layer on a sample (information)

The group A `sample` command over 300 dev examples per country:

| Country | Rule parser, no hint | Rule parser, country as hint | Structural check of the labelled postcode, locality and street |
|---|---|---|---|
| HR | 0 parsed of 300 (country undetermined 241, no last line 53, model required 6) | 0 (unsupported country 300) | 285/285 sound |
| HU | 0 of 300 (country undetermined 167, no last line 133) | 0 (unsupported country 300) | 292/292 sound |
| RO | 0 of 300 (no last line 300) | 0 (unsupported country 300) | 257/257 sound |
| AU | 138 of 300 parsed, none with the right country (country undetermined 3, no last line 159) | 0 (unsupported country 300) | 0/276 sound: the check passes no state and the structural layer requires one (`AdminArea MissingRequiredField` 276); the labelled state is in the examples |

So, as for the other Tier 2 countries, free text outside the United States and Canada still needs the Tier 2 rules and the model.
