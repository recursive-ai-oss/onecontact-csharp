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

## Group C-1: Mexico, Poland, Slovenia, Slovakia, Lithuania (terms read 2026-11-05 and 2026-11-06)

Fetch: `tools/data/fetch-tier2c1.sh` (MX, PL, SK, LT, pins in `tools/data/pins/{mx,pl,sk,lt}.pin`), `tools/data/fetch-si-rpe.py` (SI, `pins/si.pin`). Poland, Slovakia and Slovenia are read through plain tables written once by `AddressTier2C convert`; Lithuania's geodatabase is converted by `convert-lt-gdb.py` (pyogrio, a packaged GDAL reader, used only as a file-format converter, geometry not read). Derived files and their hashes are `derived` lines in the pins. The registers are published at "latest" URLs; the pinned files were kept in the data cache (a later fetch of different bytes stops on the hash mismatch). Browser user agent sent (the sites refuse a bare curl).

| Country | Register and file | Pin | Terms (quoted) | Verdict |
|---|---|---|---|---|
| MX | **DENUE 05/2026**, INEGI Directorio Estadístico Nacional de Unidades Económicas, bulk CSV, one zip per state (33 files; the State of Mexico is in two); metadata in each zip: "Modified: 2026-05-20", "License: Términos de Libre Uso de la Información del INEGI". Latin-1 text. Only the address columns are read; establishment names and activities are not. | 33 files, 574,199,079 bytes in `pins/mx.pin`; `https://www.inegi.org.mx/contenidos/masiva/denue/denue_<nn>_csv.zip` (`denue_15_1_…`, `denue_15_2_…` for the State of Mexico) | inegi.org.mx/inegi/terminos.html, "Del libre uso de la información del INEGI": "Puede explotar comercialmente la información, utilizándola como insumo para generar otros productos o servicios." and "Debe otorgar los créditos correspondientes al INEGI como autor … “Fuente: INEGI, nombre del producto de donde se extrae la información”", also "Asegurarse de notificar al usuario final de cualquier análisis o transformación que haga a la información y que la misma no sea presentada de tal manera que sugiera que dicho análisis o transformación fue realizada por parte del INEGI." | OK with attribution (commercial use stated) |
| PL | **PRG address points**, Państwowy Rejestr Granic, GUGiK, `PRG_PunktyAdresowe_POLSKA.dbf` inside `POLSKA.zip` (files dated 2026-11-04; 8,640,172 points) | 564,310,338 bytes, SHA-256 `e2177a50ae4c47e3a547ba4dc6366c754e90cdc789ea39f8fb7206942b97b433`; `https://opendata.geoportal.gov.pl/prg/adresy/PunktyAdresowe/POLSKA.zip` | geoportal.gov.pl/pl/dane/panstwowy-rejestr-granic-prg/, "Pobieranie danych PRG": "Dane PRG są dostępne bezpłatnie i do dowolnego wykorzystania." | OK (the page states free use for any purpose; credit line given anyway) |
| SI | **Register prostorskih enot** (house numbers, streets, settlements, post office areas), GURS, through the public INSPIRE addresses WFS `https://ipi.eprostor.gov.si/wfs-si-gurs-ins/ad/wfs`, fetched 2026-11-05/06 (575,773 `ad:Address`, 466 `ad:PostalDescriptor`) | `ad-address.xml.gz` 52,677,105 bytes SHA-256 `1618bb25c01fdb34d6aa6a02735e989e9bb24e04c4180d3c5dc35e1cb0dc19b4`; `ad-postal.xml.gz` 12,917 bytes `56481d858513608997d5be6d84ee7528ccbce74fe7dbd5b50a44d212bf5bcf98` (pages saved as served, gzip with a zero timestamp; one page the server had cut short was fetched again) | eprostor.gov.si metadata record of the register (25e80f41-8348-4759-bac1-ec56c7223509): "Podatki so dostopni pod Creative Commons - Attribution 4.0 International (CC BY 4.0) licenco."; e-prostor.gov.si/dostopi/javni-dostop/, "Pogoji uporabe": "V skladu s to licenco je dovoljeno vsakomur, da podatke iz evidenc Geodetske uprave, nekomercialno in komercialno, uporablja pod pogojem navedbe vira podatkov." | OK with attribution |
| SK | **Register adries**, Ministerstvo vnútra SR, initial batch of 2024-05-21 (HVD series on data.gov.sk, dataset a08562a8-e237-4e7f-a40a-9d0622445c6d): municipalities, parts of municipalities, streets, buildings (`budova`) and entrances (`vchod`), XML in zip | 5 files, 299,940,374 bytes in `pins/sk.pin` (`obec.zip`, `cast.zip`, `ulica.zip`, `budova.zip`, `vchod.zip`; `https://data.slovensko.sk/download?id=…`) | data.gov.sk term-of-use records of the distributions (`https://data.gov.sk/def/ontology/legislation/termsOfUse`, read through the catalogue's SPARQL endpoint): `authorsWorkType`, `originalDatabaseType` and `databaseProtectedBySpecialRightsType` are all `http://publications.europa.eu/resource/authority/licence/CC0`, author "MVSR" | OK (CC0). The record also has `personalDataContainmentType …/personal-data-occurence-type/2`, whose label we did not find; the files hold no names of persons (checked on the element names) |
| LT | **Adresų registras** open data, VĮ Registrų centras, as the geodatabase `RC_adresai_atviri_duomenys.zip` on geoportal.lt (assembled by VšĮ Statybos sektoriaus vystymo agentūra from Registrų centras' open data; entries dated 2026-11-04; 1,132,892 address objects, 1,190,947 flats) | 1,920,014,807 bytes, SHA-256 `c7b3b93cde062e4103f2ed239b08b17dea97170e9c820c1a8037bf48e6e6a56e`; `https://www.geoportal.lt/download/opendata/RC/RC_adresai_atviri_duomenys.zip`; derived `lt-addresses.tsv.gz` `8813ded70c24533981f6cec3cd77d3ce4176b4fe7a1cf8b51a32a8e1f1887e00` (25,354,434 bytes), `lt-flats.tsv.gz` `308b78f0e089df047630b50d0353ff3293f9f3d6f3fec4d91c55ba9b60500bed` (3,377,078 bytes) | geoportal.lt/geoportal/atviri-duomenys, section "VĮ REGISTRŲ CENTRAS ATVIRŲ DUOMENŲ PRODUKTAI": "Duomenys atverti pagal tarptautinę Creative Commons Attribution 4.0 licenciją su naudojimo ir platinimo sąlygomis, kurių ženklinimo simbolis CC BY 4.0. Duomenis galima naudoti ir dalintis neribotai, tačiau privaloma nurodyti atvertų duomenų autorystę, įdėti nuorodą į licenciją bei nurodyti, ar buvo padaryta pakeitimų." The same page lists "Adresų registro atviri duomenys" with this geodatabase. data.gov.lt and registrucentras.lt refused scripted requests (a web-application firewall and a Cloudflare challenge), so their own pages were not read; geoportal.lt is the state geoportal that publishes the file. | OK with attribution (the geoportal page states the licence for the Registrų centras open-data products; the register's own page was not reachable) |

Attribution lines for the model notices: "Fuente: INEGI, Directorio Estadístico Nacional de Unidades Económicas, DENUE 05/2026"; "Państwowy Rejestr Granic, Główny Urząd Geodezji i Kartografii (dane PRG, 2026)"; "Geodetska uprava Republike Slovenije, Register prostorskih enot (naslovi), stanje 2026-11-05 (CC BY 4.0)"; "Register adries, Ministerstvo vnútra Slovenskej republiky (CC0)"; "Adresų registras, VĮ Registrų centras (atviri duomenys per geoportal.lt), CC BY 4.0". Changes made: records reduced to address fields, shuffled into free-text layouts with invented recipient lines.

### Rendered output (seed 20261105, renderer `tier2c-2026-11-05.1`, dev 2 %)

| Country | Usable records | Train | Dev |
|---|---|---|---|
| MX | 5,754,846 | 257,838 | 5,294 |
| PL | 8,614,621 | 244,869 | 5,030 |
| SI | 575,773 | 244,222 | 4,998 |
| SK | 1,696,107 | 245,733 | 5,052 |
| LT | 1,132,627 | 245,130 | 4,905 |

Span check: 1,263,071 examples, 0 failures. A second render gave byte-identical files and manifest. The earlier four countries (NO, CZ, LV, EE) rendered again by this tool are byte-identical to their first output; the group A tool is untouched. Selection file `selection.tsv`: SHA-256 `645a093d92df6328152ab93f14082ea6366cf2479f661174af5066c2f0df3b9f`. Converted tables (`derived` lines in the pins): PL `cf05b3fab6c758082ca9292134c0450e5d7b1e81d1aadd2becdd7dc6b6d3d345`, SK `814ab1c173ea5ab8e7f2152bdc553e5889a495e3bda02599adb5fa3600efa6eb`, SI `0088489b1421184945f017958903fe6ba4e98e37ffdf6d271cb856190a526688`.

Rejected records, with reasons (`rejects-*.tsv` beside the output): MX 304,184 without a street name, 76,111 without a usable outside number, 2,934 without a usable postcode (establishments, many at one address; 67,271 repeated addresses were dropped across the five countries at render time); PL 24,864 house numbers that are not a plain number with an optional letter or part, 687 postcodes; SK 632 entrances whose building or municipality is not in force, 1,955 without a postcode; LT 83 house numbers, 182 without a postcode; SI none.

Coverage notes: MX is a business directory, so its addresses lean to streets with shops and offices. PL localities stand in for post office towns. SK entrances: where the register has no street link for a building (a town building only known by its registration number) the example is a part-of-municipality address; the Slovak share of such examples is 56 % of the register's entrances. The register does not hold names, and nothing here does.

### Rule parser and structural layer on a sample (information)

300 dev examples per country through the library's current rule parser (`RuleAddressParser.TryParse`) and `PostalAddress.Validate` on the labelled fields (the group A tool's `sample`):

| Country | No hint: parsed | With the country as hint | Structural check of the labelled fields |
|---|---|---|---|
| LT | 1 (refused: `address.no-last-line` 273, `address.country-undetermined` 25) | all refused, `address.unsupported-country` | 28/282 sound; `PostalCode InvalidFormat` 254: the postcode span carries `LT-` in 90 % of examples, as `LV-` does for Latvia |
| MX | 7 (refused: `no-last-line` 131, `country-undetermined` 118, `model-required` 44) | all refused | 160/288 sound; `PostalCode InvalidFormat` 128: `C.P.` / `CP` is inside the postcode span in 45 % of examples |
| PL | 1 | all refused | 248/274 sound; `PostalCode InvalidFormat` 26: the 10 % written without the dash |
| SI | 0 | all refused | 287/287 sound |
| SK | 0 | all refused | 286/286 sound |

So the rule parser does not read these countries' free text (they are not yet rule countries), and the structural layer accepts every postcode written in the country's own form; the invalid ones are the deliberate prefixes and the unhyphenated Polish form.

## Found out about the countries not yet loaded (2026-11-05)

* **Denmark.** DAWA is shut down: `https://api.dataforsyningen.dk/adresser` answers HTTP 410 "Gone ... will not be available again". The register (DAR) is distributed by Datafordeleren: "Som bruger af frie grunddata er du underlagt CC BY 4.0 licens" (datafordeler.dk/vejledning/brugervilkaar/danmarks-adresseregister-dar/); a no-login bulk file was not found.
* **Finland.** The DVV building address file ended distribution on 2025-03-14 according to a user's note quoting the notice; the 2024-11-14 release is a 7z archive. Licence and a stable URL were not confirmed.
* Sweden, Iceland, Croatia, Hungary, Romania and Australia: not yet investigated.
