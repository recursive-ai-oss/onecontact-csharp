# Address rules: the United Kingdom with Jersey, Guernsey and the Isle of Man, Ireland, Spain, Portugal, Italy, San Marino, the Vatican, Malta, Andorra

How the rule parser (`Addresses/Tier2`) reads and standardizes these addresses. They are rows in the same tables as the first Tier 2 group (`Addresses/Data/T2/*.tsv`), read by the same reader; `docs/address-rules-tier2-a.md` describes the shared parts. This page lists what is particular to these countries. The sources are the UPU country sheets of the postal authorities; the authorities' own guides (Royal Mail's Programmers' Guide, An Post, Correos, CTT, Poste Italiane, MaltaPost) were not used, and no paid standard (BS 7666, UNE, UNI) was bought or read. Royal Mail's PAF data is not used.

## What the rules decide

1. **Last line.** The postcode and locality end the address. They come in two orders:
   - *postcode first*: Spain and Italy (five digits), San Marino (47890-47899), the Vatican (00120), Portugal (`1234-567`, a hyphen or en dash), Andorra (`AD500`): `28013 Madrid`, `2725-079 Mem Martins`, `AD700 Escaldes-Engordany`;
   - *locality first*: the United Kingdom, Jersey, Guernsey and the Isle of Man (six formats and `GIR 0AA`, UPU United Kingdom sheet), Ireland (Eircode: three characters, a space, four characters), Malta (three letters, four digits). The post town is on the line above the postcode or on the same line before it: `London SW1A 2AA`, `LONDON` / `SW1A 2AA`. A line that has digits, or starts a place inside a building, is not taken for a post town (Dublin postal districts, `DUBLIN 24`, are the exception); without a post town the address is refused.
2. **Admin area.**
   - Italy: a province code after the locality (`00122 ROMA RM`), checked against the 107 codes (the UPU Italy sheet lists 101; Caserta, Gorizia, Nuoro, Pordenone, Trieste and Udine are added from ISO 3166-2:IT).
   - Spain: the province on a line of its own after the postcode line, or in parentheses after the locality. Its name must be one of the Correos list (UPU Spain sheet, with the current co-official spellings) and the postcode must start with the province's own two digits; otherwise the line is not taken.
   - Ireland: the county, a line that starts `Co.` or `County` above the Eircode.
3. **Country.** From the country line, the postcode form that fits one country only (`SW1A`, Eircode, `AD500`, `1234-567`, `HMR 1428`, `00120`, `4789x`), a country letter before the postcode (`E-28013`, `I-00122`, `V-00120`, `SM-47890`), or a hint. The UK postcode area decides between the United Kingdom, Jersey (`JE`), Guernsey (`GY`) and the Isle of Man (`IM`); `CHANNEL ISLANDS` names Jersey or Guernsey, whichever the area says, and conflicts with any other area. Five digits are shared by France, Germany, Spain and Italy: the reader takes Spain only with a province line whose number is the postcode's prefix, Italy only with a province code, and does not read street words (`Plaza`, `Camino`, `Via`) as evidence, because American addresses use them too. Otherwise `address.country-undetermined`.
4. **Delivery lines.**
   - *Number first* (UK, Ireland, Malta; Andorra at the French post office): `49 Featherstone Street`, `38 Triq it-Tempji Neolitici`, `12 AVINGUDA TORRENT PREGO`. A line without a number is not a street line in the UK and Ireland (it may be a house name or a locality); in Malta and Andorra a line that starts with a road type (`Triq`, `Carrer`) is.
   - *Name then number* (Spain, Portugal, Italy, San Marino, Vatican, Andorra at the Spanish post office): the line starts with a road type (`Calle`, `Plaza`, `R`, `Av`, `Via`, `Viale`, `Piazza`, `Carrer`), then the name, then the house number, then staircase, floor and door. The house number is the first number after the name that leaves only floor and door words behind it, so `Calle 5 de Mayo 12` has number 12, and `Rua 25 de Abril` has no number. `s/n`, `sn` and `snc` stand for no number. Without a road type the line is left to the model.
   - *Boxes*: `P.O. Box` (UK, Ireland, Malta, Crown dependencies), `Apartado` (Spain, Portugal), `CCI` (Portugal), `Casella postale` (Italy, San Marino, Vatican), `Kaxxa Postali` (Malta), `BP`, `AP`, `Apartat` (Andorra).
   - Lines above the first delivery line are kept as written; lines after it (sub-locality, post office) are further delivery lines. In the UK and Ireland up to six lines without digits above the post town are accepted (house names, organizations), and nothing is warned about them.
5. **Everything else** is refused with `address.model-required`. Nothing is invented: no post town from a postcode, no county or province from a locality, no country from a street word.

## Postal and Display

`Postal` follows each authority's own example lines; `Display` writes the same tokens in natural title case (`R Leal da Câmara 31 RC ESQ`; floor and door words are kept as written).

| Country | Postal | Source |
|---|---|---|
| GB, JE, GG, IM | Lines as written; the post town in capitals; the postcode in capitals with one space, on a line of its own; country line `UNITED KINGDOM`, `CHANNEL ISLANDS` (Jersey, Guernsey) or `ISLE OF MAN`. No type is abbreviated: Royal Mail's free sheet publishes no abbreviation table. | UPU United Kingdom sheet 11/2011 pp. 1-2; Jersey, Guernsey, Isle of Man sheets 03/2005 |
| IE | The addressee line as written; every other line (street, locality, county) in capitals; the Eircode on its own line, `T37 F8HK`; `CO.` kept. | UPU Ireland sheet 06/2017 p. 1-2 |
| ES | Lines as written; the locality and the province in capitals; the commas before floor and door kept (`Calle Huertas 18, 4º, C`), none between street and number; the province on its own line. | UPU Spain sheet 01/2013 |
| PT | Every line in capitals; accents kept; the postcode with a hyphen (`2725-079`). | UPU Portugal sheet 10/2024 ("address preferably printed in capitals") |
| IT | Every line in capitals; no punctuation from the street line on (`VIA ROMA, 12` is `VIA ROMA 12`); a point or comma between digits stays (`KM 15,500`); the province code after the locality. | UPU Italy sheet 04/2020 |
| SM, VA | Lines as written, locality in capitals; the Italian postcode system, no province code. | UPU San Marino sheet 10/2010, Vatican sheet 03/2005 |
| MT | Lines as written; the locality in capitals; the postcode `HMR 1428` on its own line. | UPU Malta sheet |
| AD | Lines as written; the locality in capitals; the postcode `AD500` (the letters are part of it) before the locality. | UPU Andorra sheet 09/2004 |

The country line is printed only when asked. Country lines: `SPAIN`, `PORTUGAL`, `ITALY`, `IRELAND`, `MALTA`, `SAN MARINO`, `VATICAN`, `ANDORRA`.

## Where the sheets disagree or stop short

- The UPU San Marino sheet prints `REP. OF SAN MARINO` in the example and `SAN MARINO` in the contact block; Postal prints `SAN MARINO`. The Vatican sheet prints `Città del Vaticano` in the example and capitals in the diagram; Postal prints capitals.
- The UPU Andorra sheet prints `ANDORRE` for the Spanish post office; Postal prints `ANDORRA` for both.
- The UPU Portugal sheet prints the postcode dash as an en dash in its text; Postal writes the hyphen the sheet shows in `2785-174`.
- The Irish sheet's first example is in capitals for every line including the addressee, its second example only for the lines after the addressee; Postal leaves the addressee as written.
- The UPU Italy sheet's list of provinces lacks six codes (see above); example `FRAZIONE MASSIGNANO SNC / 60129 MOSSO BI` pairs a postcode of Ancona with Biella; the reader does not look postcodes up, so it reads it.
- The Spain sheet's province list uses older spellings (`LA CORUÑA`, `ORENSE`, `GUIPÚZCOA`, `BALEARES`, `GUADELAJARA`); both these and the current ones are accepted.
- The UPU Guernsey sheet's contact block has no postcode (`St. Peter Port / GUERNSEY / CHANNEL ISLANDS`); it is not read.

## Not covered by rules (these go to the model)

Addresses without a postcode; the UK county line when a county is written between the post town and the postcode (it would be taken for the post town); street lines without a road type in Spain, Portugal and Italy; Italian `P.zza`-style abbreviations that are not in the type table; Gibraltar and the other British territories (they are not in the group); personal postcodes of Malta that do not follow the format.

## Structural checks

`PostalAddress.Validate` uses the structural layer's postcode patterns for each country (libaddressinput data); the Spanish province prefix and the Italian CAP ↔ province are not checked beyond what that data holds. The reader's own checks are the ones above: the UK area decides the dependency, a Spanish province line must agree with the postcode prefix, San Marino and the Vatican have their own postcode ranges.

## Keys and expansion

Keys use the same segments as the first group (country, one segment per delivery line, locality, admin area, postcode) with the country's languages' dictionaries applied: Spanish, Catalan, Basque and Galician for Spain, Portuguese, Italian, Maltese with English, Irish with English, Catalan with Spanish and French for Andorra. A road type is the same word written in full or abbreviated (`C/` and `Calle`). `AddressExpander` takes `Spanish`, `Portuguese`, `Catalan`, `Basque`, `Galician`, `Maltese` and `Irish` besides the earlier languages; a street type leads the name in French, Italian, Spanish, Portuguese, Catalan, Basque, Galician and Maltese, so it expands in first position there. The words that libpostal lists as ambiguous in a new language are used only when that language is asked for, so the keys of the earlier countries are unchanged.

## Reading real-world text (recovery fixes)

Measured on the register dev examples, these rules were added to what the sheets show:

- English type last (`Barry Jackson Tower`, `High Street`) for GB, JE, GG, IM, IE and MT; the type's span excludes a closing period.
- Unit lines: a prefix (`Flat 30`, `Interno 2`, `17º B`) or a unit alone on a line; unit tails split into designator and number. Italian unit lines are moved above the street line (Poste Italiane, Standard di composizione indirizzi 1.1).
- Provinces: Italian and Spanish provinces in parentheses or after a comma, read without the parentheses; an Italian house-number letter (`25 D`); a province code with no locality (`33070 (pn)`) is refused rather than guessed.
- Malta and Andorra read the number on either side of the name (`27 Andilina`, `Triq San Guzepp 27`); `C/name` written glued; `AD-500` is the postcode `AD500`; more Spanish, Italian, Portuguese and Andorran road types (CTT Anexo I abbreviations), `Apartado de Correos`, `CP`; more country names.
- Britain and Ireland: when the post town is on a line of its own above a postcode line and another line without digits stands between it and the street, either could be the post town or a county, so the rules refuse (`address.model-required`) unless the post town is the only line in capitals. A post town on the postcode's own line (`Birmingham B6 5BA`) is never ambiguous. A nation or country name above the postcode (`ENGLAND`) is not a post town.
- A hint of Italy keeps `97xxx` and `980xx` as Italian postcodes.

## Departures of Postal from the sheets (from the posts' own guides)

Applying the post's own rule: (a) Portugal drops the closing period of an abbreviation (`R LEAL`, CTT Manual de Endereçamento p. 7, over the UPU sheet's `R. LEAL`); (d) Italy moves the unit line above the street and drops the parentheses around the province (Poste Italiane).
Choices between two authorities, flagged: (b) Jersey locality printed as written (Jersey Post asks for capitals on the postcode only) while Guernsey, the Isle of Man and Britain print capitals; (c) Irish street lines left as written; (e) `PO Box` kept as the writer wrote it for GB, IE and MT (Royal Mail), where the UPU sheets print `P.O. Box`; (f) the Maltese postcode stays on its own line (UPU sheet), although the regulator's page writes locality and postcode on one line.

## Posts' own guides (retrieved 2026-11-06)

Royal Mail PAF Programmers' Guide Ed. 7 v6 (e74e47fa…; guide only, no PAF data); Jersey Post addressing guidelines January 2025 (e2efb3d3…); Guernsey Post Guide to Services 2018/19 (8504e22a…); Poste Italiane, Standard di composizione indirizzi, August 2023 (5ac3c9f9…); CTT Manual de Endereçamento, October 2014, text copy (1eda0a44…; the official 2018 edition is image only); An Post sending guide (ab32456e…, no address examples); Malta Communications Authority page on addressing letters. Not used: Royal Mail's clear-addressing PDF (403), the Isle of Man Post page (JavaScript, no examples), Correos and MaltaPost (no guide found). The 54 examples taken from these guides are in `testdata/addresses/authority-examples-tier2b-posts.tsv`.

## Recovery on the register dev examples (rules alone, with the country as hint; every labelled field recovered; before then after)

| GB | JE | GG | IM | IE | ES | PT | IT | SM | VA | MT | AD |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 8.4 → 62.6 | 0.5 → 73.6 | 1.4 → 73.4 | 0 → 75.5 | 1.6 → 87.0 | 52.0 → 88.7 | 68.7 → 89.1 | 36.7 → 83.0 | 69.9 → 92.0 | 61.7 → 96.7 | 29.4 → 85.8 | 62.4 → 79.8 |

Samples of 2000 (all of them when fewer: JE 220, GG 207, IM 192, SM 163, VA 60, MT 855, AD 367). The GB figure was measured before the inline post town fix above and counts 443 refusals for the county ambiguity. Without a hint, Spanish and Italian text with a five-digit postcode and no province evidence is refused as `address.country-undetermined` unless country inference finds evidence. The remaining refusals are mostly single-line text without a comma between street and locality. Known gaps for the model: county-ambiguous GB lines, typos in road types, Welsh and Gaelic bilingual names, the Italian dependent-locality line.

## Web forms (counties, island names, number markers, province lines)

Read for web pages, from the Royal Mail PAF Programmers' Guide (the county is optional; a sub-building such as `Unit 4` comes before the building name), Jersey Post and Guernsey Post, the UPU Ireland sheet, CTT *Manual de Endereçamento* p. 7 (a number marker before the door number is unnecessary; lote and loja are units), Correos, Poste Italiane and the UPU Malta, San Marino and Andorra sheets:

- A county, region or nation name on its own line, after `Co.` or `County`, or glued to the word (`Co.Louth`) is the admin area; the post town is the line above it. The county names are our own list (`t2-counties.tsv`). A bare county needs a post town and a delivery line before it, so `Dublin 8` is never a county.
- A postcode written without its space (`CT161QD`) is split in the standard form. A one-line `14 Mill Lane Henley-on-Thames RG9 2AA` is split after the first road type. An Eircode alone ends an Irish address that has no post town.
- Jersey, Guernsey and Isle of Man: the island name before the postcode is the country text and the parish or town above it is the locality; the printed layout stays as the sheets have it. `Jersey, Channel Islands` and `British Isles` are read through the postcode area.
- `nº`, `n.º`, `Nr` and `Nº` before a number are dropped from the standard form; `CL.MAYOR` is split into type and name; Catalan ordinals (`3r 2a`), `baixos`, `bis`, `ter`, `km 5`, `nave`, `parcela`, `lote`, `loja`, polígono and zona industrial types, and the Basque `Name Kalea 5` order are read.
- A country in parentheses after the locality (`(Italy)`, `(RSM)`, `(AD)`) is the country text. `AD-500` is read as AD500. `80134 Napoli, NA` and `Napoli NA` give the province; an Italian address with a province code needs no postcode.

Not read yet, and refused to the model: `28013 Madrid, Calle Mayor 5` (postcode first), a country name between the post town and the postcode (`Belfast, UK BT1 1GH`), `Surrey, England` as two levels, an untyped Spanish street (`Nagusia 5`), and an Italian `Via` address with no hint and no province code (shared with Catalan).

## Before and after the web-form work (all 38 Tier 2 countries; register dev examples, rules alone, 2000 per country or all of them)

Every labelled field recovered (every-field) and share of labelled fields recovered (field), as percent, then refusals and wrong-country reads, before then after. With the country as hint (hint) and without (no hint). "Before" is the tree before this work.

| Country | hint every-field | hint field | hint refused | hint wrong | no hint every-field | no hint field | no hint refused | no hint wrong |
|---|---|---|---|---|---|---|---|---|
| AD | 79.8 → 91.3 | 81.7 → 93.4 | 68 → 25 | 0 → 0 | 79.8 → 91.3 | 81.7 → 93.4 | 68 → 25 | 0 → 0 |
| AT | 83.0 → 83.0 | 90.0 → 90.0 | 117 → 117 | 0 → 0 | 20.7 → 20.7 | 27.8 → 27.8 | 1487 → 1487 | 0 → 0 |
| AU | 90.6 → 90.6 | 91.5 → 91.5 | 188 → 188 | 0 → 0 | 85.9 → 85.9 | 86.7 → 86.7 | 280 → 280 | 12 → 12 |
| BE | 41.9 → 41.9 | 70.7 → 70.7 | 218 → 218 | 0 → 0 | 32.0 → 32.0 | 43.4 → 43.4 | 1022 → 1022 | 6 → 6 |
| CH | 62.4 → 62.4 | 84.2 → 84.2 | 115 → 115 | 0 → 0 | 21.1 → 21.1 | 37.5 → 37.5 | 1232 → 1232 | 3 → 3 |
| CZ | 71.9 → 71.9 | 88.9 → 88.9 | 121 → 121 | 0 → 0 | 33.8 → 33.8 | 47.8 → 47.8 | 1040 → 1040 | 1 → 1 |
| DE | 80.7 → 80.7 | 89.1 → 89.1 | 126 → 126 | 0 → 0 | 59.9 → 59.9 | 66.3 → 66.3 | 615 → 615 | 2 → 2 |
| DK | 93.7 → 93.7 | 95.4 → 95.4 | 105 → 105 | 0 → 0 | 60.9 → 60.9 | 63.1 → 63.1 | 766 → 766 | 1 → 1 |
| EE | 74.4 → 74.4 | 86.8 → 86.8 | 233 → 233 | 0 → 0 | 73.1 → 73.1 | 85.2 → 85.2 | 275 → 275 | 2 → 2 |
| ES | 88.7 → 88.7 | 90.8 → 90.8 | 184 → 184 | 0 → 0 | 25.0 → 25.0 | 27.9 → 27.9 | 1490 → 1490 | 0 → 0 |
| FI | 93.0 → 93.0 | 94.3 → 94.3 | 127 → 127 | 0 → 0 | 82.1 → 82.1 | 83.7 → 83.7 | 342 → 342 | 6 → 6 |
| FR | 68.9 → 68.9 | 83.4 → 83.4 | 175 → 175 | 0 → 0 | 52.3 → 52.3 | 63.4 → 63.4 | 649 → 649 | 11 → 11 |
| GB | 62.6 → 83.7 | 61.5 → 90.8 | 718 → 129 | 0 → 0 | 62.6 → 83.7 | 61.5 → 90.8 | 718 → 129 | 0 → 0 |
| GG | 73.4 → 83.1 | 76.6 → 89.3 | 46 → 19 | 0 → 0 | 73.4 → 83.1 | 76.6 → 89.3 | 46 → 19 | 0 → 0 |
| HR | 61.5 → 61.5 | 85.7 → 85.7 | 152 → 152 | 0 → 0 | 26.8 → 26.8 | 37.7 → 37.7 | 1220 → 1220 | 1 → 1 |
| HU | 92.6 → 92.6 | 94.0 → 94.0 | 128 → 128 | 0 → 0 | 75.6 → 75.6 | 77.4 → 77.4 | 471 → 471 | 0 → 0 |
| IE | 87.0 → 96.4 | 88.5 → 97.8 | 246 → 21 | 0 → 0 | 87.0 → 93.7 | 88.5 → 95.6 | 246 → 77 | 0 → 0 |
| IM | 75.5 → 90.1 | 77.8 → 93.8 | 40 → 9 | 0 → 0 | 75.5 → 90.1 | 77.8 → 93.8 | 40 → 9 | 0 → 0 |
| IS | 84.3 → 84.3 | 93.5 → 93.5 | 103 → 103 | 0 → 0 | 12.7 → 12.7 | 16.8 → 16.8 | 1716 → 1716 | 0 → 0 |
| IT | 83.0 → 84.3 | 91.3 → 92.5 | 144 → 116 | 1 → 1 | 57.8 → 59.1 | 65.3 → 66.5 | 699 → 671 | 31 → 31 |
| JE | 73.6 → 87.7 | 76.4 → 93.2 | 50 → 11 | 0 → 0 | 73.6 → 87.7 | 76.4 → 93.2 | 50 → 11 | 0 → 0 |
| LI | 86.0 → 86.0 | 92.1 → 92.1 | 21 → 21 | 0 → 0 | 82.9 → 82.9 | 88.7 → 88.7 | 31 → 31 | 0 → 0 |
| LT | 93.6 → 93.6 | 95.6 → 95.6 | 95 → 95 | 0 → 0 | 92.8 → 92.8 | 95.0 → 95.0 | 108 → 108 | 3 → 3 |
| LU | 81.1 → 81.1 | 89.7 → 89.7 | 135 → 135 | 0 → 0 | 54.8 → 54.8 | 63.6 → 63.6 | 753 → 753 | 0 → 0 |
| LV | 71.4 → 71.4 | 79.2 → 79.2 | 390 → 390 | 0 → 0 | 71.4 → 71.4 | 79.2 → 79.2 | 390 → 390 | 0 → 0 |
| MC | 79.6 → 79.6 | 87.7 → 87.7 | 12 → 12 | 0 → 0 | 79.6 → 79.6 | 87.7 → 87.7 | 12 → 12 | 0 → 0 |
| MT | 85.8 → 85.8 | 87.4 → 87.4 | 112 → 112 | 0 → 0 | 85.8 → 85.8 | 87.4 → 87.4 | 112 → 112 | 0 → 0 |
| MX | 73.3 → 73.3 | 87.1 → 87.1 | 174 → 174 | 17 → 17 | 67.8 → 67.8 | 80.8 → 80.8 | 312 → 312 | 53 → 53 |
| NL | 90.9 → 90.9 | 94.1 → 94.1 | 112 → 112 | 0 → 0 | 85.8 → 85.8 | 89.0 → 89.0 | 218 → 218 | 0 → 0 |
| NO | 93.4 → 93.4 | 95.2 → 95.2 | 108 → 108 | 0 → 0 | 65.0 → 65.0 | 67.0 → 67.0 | 681 → 681 | 4 → 4 |
| PL | 92.4 → 92.4 | 95.1 → 95.1 | 107 → 107 | 0 → 0 | 89.0 → 89.0 | 92.1 → 92.1 | 162 → 162 | 76 → 76 |
| PT | 89.1 → 91.7 | 90.7 → 93.1 | 195 → 143 | 0 → 0 | 89.1 → 91.7 | 90.7 → 93.1 | 195 → 143 | 0 → 0 |
| RO | 51.3 → 51.3 | 71.1 → 71.1 | 397 → 397 | 0 → 0 | 51.3 → 51.3 | 71.1 → 71.1 | 397 → 397 | 0 → 0 |
| SE | 93.3 → 93.3 | 94.7 → 94.7 | 117 → 117 | 0 → 0 | 62.6 → 62.6 | 64.7 → 64.7 | 734 → 734 | 1 → 1 |
| SI | 89.9 → 89.9 | 93.0 → 93.0 | 149 → 149 | 0 → 0 | 64.6 → 64.6 | 68.9 → 68.9 | 660 → 660 | 0 → 0 |
| SK | 91.3 → 91.3 | 94.4 → 94.4 | 108 → 108 | 0 → 0 | 50.0 → 50.0 | 53.6 → 53.6 | 957 → 957 | 9 → 9 |
| SM | 92.0 → 92.0 | 93.1 → 93.1 | 12 → 12 | 0 → 0 | 92.0 → 92.0 | 93.1 → 93.1 | 12 → 12 | 0 → 0 |
| VA | 96.7 → 96.7 | 96.9 → 96.9 | 2 → 2 | 0 → 0 | 96.7 → 96.7 | 96.9 → 96.9 | 2 → 2 | 0 → 0 |

No row drops by more than 0.5 and no wrong-country count rises. The web forms are not in the register renders, so rows for countries other than the British Isles, Portugal and Italy do not move; the forms are covered by the unit tests.

## Sources

UPU country sheets (addressing unit, upu.int, `PostalEntitiesFiles/addressingUnit/<iso3>En.pdf`): United Kingdom 11/2011, Jersey 03/2005, Guernsey 03/2005, Isle of Man 03/2005, Ireland 06/2017, Spain 01/2013, Portugal 10/2024, Italy 04/2020, San Marino 10/2010, Vatican 03/2005, Malta, Andorra 09/2004. Facts and short examples only, cited by sheet and page; the examples used as tests are in `testdata/addresses/authority-examples-tier2b.tsv`. The libpostal dictionaries of `es`, `pt`, `ca`, `eu`, `gl`, `mt` and `ga` at the pinned tag, through the ingest tool.

## Island lines without commas

Jersey, Guernsey and the Isle of Man are also written on one line with no commas: `Victoria Road Douglas IM1 1AA`, `Tromode Road Douglas Isle of Man IM2 5AA`, `Hill Street Jersey JE4 7BR`, `Douglas Isle of Man IM1 2AB`. The street ends at the last road type that leaves one to three words for the post town (Royal Mail PAF Programmers' Guide: the thoroughfare, then the post town); the island's name after the town is the country text, and when only the island name follows the road it is read as the town. Nothing is added to the input.
