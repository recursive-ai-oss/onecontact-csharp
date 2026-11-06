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

* **Denmark.** DAWA is shut down: `https://api.dataforsyningen.dk/adresser` answers HTTP 410 "Gone ... will not be available again". The register (DAR) is distributed by Datafordeleren: "Som bruger af frie grunddata er du underlagt CC BY 4.0 licens" (datafordeler.dk/vejledning/brugervilkaar/danmarks-adresseregister-dar/); a no-login bulk file was not found.
* **Finland.** The DVV building address file ended distribution on 2025-03-14 according to a user's note quoting the notice; the 2024-11-14 release is a 7z archive. Licence and a stable URL were not confirmed.
* **Lithuania.** data.gov.lt lists the Registrų centras address register text data ("Adresų registro ... tekstiniai duomenys su koordinatėmis", updated 2026-01-22) and gives other Registrų centras sets as "Licencija — CC BY 4.0"; the dataset's own licence line and a download link were not reached.
* **Mexico.** INEGI's DENUE state files (`https://www.inegi.org.mx/contenidos/masiva/denue/denue_<nn>_csv.zip`) download with a browser user agent; the "términos de libre uso" text was not read yet.
* **Poland.** PRG address points and streets are distributed as SHP packages per municipality, county and voivodeship through geoportal.gov.pl ("Dane do pobrania") and listed as free data by GUGiK (gugik.gov.pl "dane bez opłat"). The licence wording was not read. The attribute table is a .dbf, which a loader can read without a shapefile library.
* **Slovenia.** The Register prostorskih enot (house numbers, streets, postal districts) is listed on podatki.gov.si and e-prostor.gov.si; a third-party repository describes it as CC BY 4.0 (GURS). The licence line on the publisher's own page and a download link were not reached.
* Sweden, Iceland, Slovakia, Croatia, Hungary, Romania and Australia: not yet investigated.
