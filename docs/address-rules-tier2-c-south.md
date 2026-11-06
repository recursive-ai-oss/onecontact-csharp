# Address rules: Poland, the Czech Republic, Slovakia, Slovenia, Croatia, Hungary, Romania, Mexico, Australia

How the rule parser (`Addresses/Tier2`) reads and standardizes these addresses. They are rows in the same tables as the earlier Tier 2 groups (`Addresses/Data/T2/*-c-south.tsv`, read next to the base files), read by the same reader (`Tier2Reader.Central.cs` holds what is particular to this group); `docs/address-rules-tier2-a.md` and `-b.md` describe the shared parts.

## What the rules decide

1. **Last line.** Postcode and locality end the address.
   - *Postcode first*: Poland `00-950 WARSZAWA` (five digits with a hyphen), Czech Republic and Slovakia `190 16 PRAHA 916` / `921 01 PIEŠŤANY` (three digits, a space, two digits), Slovenia `1000 Ljubljana` and Croatia `10000 Zagreb` (four and five digits), Romania `200716 CRAIOVA` (six digits), Mexico (five digits, locality and state abbreviation), and Hungary in two orders (below).
   - *Hungary*: the authority order puts the locality on the line above the street (`BUDAPEST` / `Kossuth Lajos utca 12.` / `1037`), and the common "1037 Budapest, Kossuth Lajos utca 12." order is accepted too. Postal always prints the authority order.
   - *Australia*: `RICHMOND VIC 3121`, locality, state or territory (`NSW VIC QLD WA SA TAS ACT NT`, capitals only) and four digits on one line.
   - *Mexico*: a state abbreviation (`CDMX`, `JAL`, `NL`, ...) or the colonia line is what makes five digits Mexican; the state is one of the 32 SEPOMEX abbreviations.
2. **Shared digit forms.** Five digits fit Mexico and Croatia, four fit Slovenia and Hungary, three plus two fit the Czech Republic and Slovakia. Without a country line, a hint or a postcode prefix that decides, the reader takes a country only on evidence that belongs to one of them: a state abbreviation or colonia line (MX), Croatian or Slovenian street words, a Hungarian street type. Czech versus Slovak needs the country line or a hint.
3. **Country.** From the country line (in English, the local language or the sheet's abbreviation such as `CZECH REP.`), the postcode form that fits one country only (`00-950`, six digits, an Australian state line), or a hint.
4. **Delivery lines.**
   - *Type, name, number* (Poland `ul. Marszałkowska 12/4`, Czech Republic `Valdštejnské náměstí 17/4`, Slovakia, Slovenia `Cesta v Mestni log 1`, Croatia `Ilica 12`, Hungary `Kossuth Lajos utca 12.`, Romania `Str. Bariera Valcii, nr. 215`): the street type is part of the street name or leads it per country; `č. p.` (Czech house number without a street), `nr.` and `Nr` markers are kept as written. A bare house number with `/` (orientation/descriptive) is a house number.
   - *Number first* (Australia): `12 Smith St`, `Unit 4, 12 Smith St`, `4/12 Smith St`, `Lot 16 Godfrey Ave`, `Level 2, 100 Pitt St`. The unit designator is abbreviated as Australia Post asks (`Unit`, `Lvl`, `Apt`); a street name's own words (`St Kilda Rd`, `McKillop St`, a trailing `West`/`W`) are not changed.
   - *Boxes*: `skr. poczt. Nr 115` (PL), `poštovní přihrádka 87` (CZ), `P.O. Box`/`PO Box`, `Apartado Postal` (MX), `Locked Bag`, `GPO Box`, `Private Bag`, `Reply Paid` (AU), `poste restante`, `dodávací schrána B/52` (CZ delivery box). The box number may carry `-` or `/` (`70-123`, `B/52`).
   - Lines above the first delivery line (titles, addressee, `do rukou p. Ptáčka`, `nar. 1. 4. 1946`) are kept as written.
5. **Postcode ↔ region.** The reader does not duplicate ranges. `Validate()` uses the structural layer's postcode patterns, so a Mexican prefix ↔ state or Australian postcode ↔ state check is exactly what libaddressinput's zip rules for the state hold; the reader itself checks only that an Australian state is one of the eight and in capitals.
6. **Everything else** is refused with `address.model-required`. Nothing is invented: no locality from a postcode, no state from a locality, no country from a street word.

## Postal and Display

| Country | Postal | Source |
|---|---|---|
| PL | Locality in capitals, postcode `00-950` before it; lines above as written; country line `POLAND` | Poczta Polska guide (own); UPU Poland 08/2018 |
| CZ, SK | Locality in capitals, two spaces after the postcode (`190 16  PRAHA 916`); other lines as written; `Poste restante` as written | Česká pošta (own); UPU Czech Republic 02/2018, Slovakia 01/2013 |
| SI | Postcode, locality in capitals | UPU Slovenia 05/2016 |
| HR | Postcode, locality in capitals | UPU Croatia 05/2014 |
| HU | Locality in capitals on its own line above the street; postcode on the last line | Magyar Posta guide (own); UPU Hungary 01/2012 |
| RO | Street line as written, postcode and locality in capitals | UPU Romania 10/2007 |
| MX | Street, colonia, `CP` line with the locality and state abbreviation, capitals as the sheet prints | UPU Mexico 03/2017 |
| AU | Street line, unit and designators in capitals without periods; `LOCALITY STATE 3000` on one line with single spaces | Australia Post "Correct addressing" (2022), Address Presentation Standards (1999); UPU Australia 07/2017 |

`Display` writes the same tokens in title case with the authority's abbreviations (`Sc Pasaj SA`, `Praha 916`, `Motosport, a. s.`); identifiers with digits keep their capitals; particles stay lower case (`Dvory nad Lužnicí`); a legal-form abbreviation and Australian `McKillop`, `St Kilda` keep their shapes. The country line is printed only when asked.

## Where the guides disagree or stop short

- Posts' own guides were found for Poland, the Czech Republic, Hungary and Australia only. For Slovakia, Slovenia, Croatia, Romania and Mexico the UPU sheets stand in. The Polish and Hungarian guides show their examples as images, so only their rules are read; the examples used as tests come from the UPU sheets.
- The sheets print the country as `POLSKA`/`POLAND` and `CZECH REP.`; Postal prints the English country name (`POLAND`, `CZECH REPUBLIC`).
- Polish postcodes are written with a hyphen where the sheet shows an en dash.
- The Czech and Slovak sheets print the locality in mixed case in some examples while the post's own rules say capitals; Postal uses capitals. Czech Post requires two spaces after the postcode (`190 16  PRAHA 916`), the UPU text only recommends it; Postal prints two.
- The Slovak sheet prints `POSTE RESTANTE` in capitals; Postal prints `Poste restante` as Czech Post does.
- The 1999 Australia Post standards print a street line with a period (`17 Jones St.`) in the UPU example and ask for capitals and no punctuation elsewhere; Postal uses capitals without the period. The standards prefer two spaces between locality, state and postcode, the 2022 brochure prints one; Postal prints one. The 1999 text cites AS 4590 as its own source; the standard itself was not read.
- Typos in the printed text: the Slovenian sheet's `Prešemova` is kept as printed; the brochure's `PARKVILLE VIC 305 2` is not transcribed.
- Some AU element examples have no complete address; the test appends a last line and, where needed, a house number of our own.

## Departures of Postal from the printed text

Each applies the country's own postcode or name form, a post's own written rule, or the never-invent rule.

1. Country line in English (`POLAND`, `CZECH REPUBLIC`): the country's English name form.
2. Polish postcode hyphen for the sheet's en dash: the postcode's own form.
3. Two spaces after the Czech/Slovak postcode where the UPU example shows one: Česká pošta's written rule.
4. Locality in capitals where the UPU examples for PL, CZ and SK print mixed case: the posts' own written rule.
5. `Poste restante` as written, not the Slovak sheet's capitals: Česká pošta's usage.
6. AU street line in capitals without the period (`17 JONES ST`): Australia Post's written rule.
7. AU one space between locality, state and postcode: the 2022 brochure's own print.

## Keys and expansion

Keys use the same segments as the earlier groups with each country's dictionaries applied: Polish, Czech, Slovak, Slovenian, Croatian, Hungarian and Romanian from libpostal's `pl`, `cs`, `sk`, `sl`, `hr`, `hu` and `ro`; Spanish for Mexico, English for Australia. `AddressExpander` takes the seven new languages (`ExpansionLanguages.Polish` … `Romanian`); a leading street type expands in first position in Polish, Czech, Slovak, Slovenian, Croatian and Romanian. House-number markers (`Nr`, `nr.`, `č. p.`) are skipped in the key and the Polish box words are normalized. The keys of US/CA and the first two Tier 2 groups do not change.

## Reading real-world text (recovery fixes)

Measured on the register dev examples (2000 per country, rules alone, with the country as hint; every labelled field recovered):

- **Romania.** The postcode may come last (`Pitești, 110040`; `București, Sector 3, 032391`; `Brașov, RO-500483`), and a sector or county after the locality (`011371 București, Sector 1`; `400241 Cluj-Napoca, județul Cluj`), which on one line is a unit of its own after the postcode, is joined to it. A county (`jud.`, `județ`, `județul` and a name) is the admin area, kept as written and printed after the locality, `400241 CLUJ-NAPOCA, JUDEȚUL CLUJ`. A sector stays part of the locality. The comma-below letters `ș` and `ț` now fold to `s` and `t` like the cedilla forms (the fold missed them, so Romanian words never matched their keys).
- **Australia.** The state in full is read and standardized to its abbreviation (`Camira Queensland 4300` is `CAMIRA QLD 4300`); a comma before the state (`Downer, ACT 2602`) is read on one line; a lowercase state abbreviation is read once the country is known to be Australian (without a hint `Sydney nsw 2000` is still refused). On one line without separators the locality starts after the only street type that can end a street name; where two could, the text is refused. `4/12 Smith St` is a unit number and a street number that print as written, in two parts.
- Not changed: a line between the street and the postcode line (a settlement or part of a municipality: `Hodolany`, `Bakovčica`, `Sector 1`) stays an additional delivery line, because Postal and Display print it where the writer put it; the registers label it a dependent locality, and the rules do not yet produce that field. Slash numbers in Croatia and the Czech Republic (`7/6`, `320/62`) stay house numbers: the slash is not always a unit.

| Country | Rules alone, with hint: before | after | Refused to the model |
|---|---|---|---|
| CZ | 71.2 | 71.2 | no-last-line 104 (no postcode or no locality), model-required 17 |
| HR | 61.5 | 61.5 | no-last-line 102, model-required 50 |
| RO | 16.0 | 50.2 | no-last-line 435, model-required 21 |
| HU | 92.6 | 92.6 | no-last-line 128 |
| AU | 56.8 | 90.6 | no-last-line 160, model-required 24, country-conflict 4 |

Without a hint the Czech, Croatian and Hungarian forms are shared with other countries, so most text is refused as `address.country-undetermined` by design (CZ 1594, HR 1181, HU 955 of 2000); Romania and Australia read without a hint as well as with it.

### Second recovery round: Mexico, Poland, Slovenia, Slovakia

Same measurement (2000 register dev examples per country, rules alone). Both columns come from the same recovery tool with the same gold conventions: the marker word of a number (`Núm.`) counts with the house number as `No.` does. Before is the code without this round; "every" is every labelled field recovered, "fields" the share of labelled fields recovered.

| Country | Hint | Every field before → after | Fields before → after | Top refusals after |
|---|---|---|---|---|
| MX | yes | 4.7 → 73.3 | 42.5 → 87.1 | no-last-line 140 (no postcode), model-required 29, country-conflict 5 |
| MX | no | 4.0 → 67.8 | 39.5 → 80.8 | no-last-line 134, country-undetermined 131, model-required 42 |
| PL | yes | 72.3 → 92.4 | 81.0 → 95.1 | no-last-line 107 |
| PL | no | 75.1 → 87.5 | 84.3 → 90.6 | no-last-line 125, country-undetermined 65 |
| SI | yes | 78.5 → 89.9 | 90.2 → 93.0 | no-last-line 143, model-required 6 |
| SI | no | 26.2 → 29.0 | 32.1 → 32.8 | country-undetermined 1249, no-last-line 145 |
| SK | yes | 89.3 → 91.3 | 93.7 → 94.4 | no-last-line 108, model-required 4 |
| SK | no | 13.2 → 13.5 | 16.8 → 16.9 | country-undetermined 1592, no-last-line 123 |

Without a hint the four-digit (SI) and `NNN NN` (SK) forms are shared with other countries and stay `address.country-undetermined` by design. A refusal as `no-last-line` is mostly text without a postcode, which the rules never invent.

What the rules read now, each from the guide named:

- **Poland.** Five digits without the dash (`08530 Dęblin`) are `08-530` when the text is known to be Polish (a hint or the country name); without that they are not read as Polish (France, Germany and others share the form). Poczta Polska, *Zasady poprawnego adresowania*, 1.2: the building number and, after it, the number of the flat or premises. `9/93` is a house number `9` and a unit number `93`, printed together as written; `m. 22` (mieszkanie) and `lok. 5` (lokal) are the designator and the unit number, also when a comma set them apart on one line; `Skrytka 396` is a box.
- **Slovenia.** A single letter after a space belongs to the house number (`213 a`, `4 c`; the UPU Slovenia sheet writes `p.p.` and the letter forms follow Slovenian usage); `p. p. 3795` is a box.
- **Slovakia.** `P.O.BOX 341` and `Poštový priečinok 1084` start a box line (UPU Slovakia sheet: P.O. Box). A box word inside a street line (`Závodie P.O.BOX 1970`) leaves the line to the model instead of reading it as a street name. A legal form after a comma (`Mikuš a spol., v.o.s., Neded 931`) belongs to the company name before it.
- **Mexico** (UPU Mexico sheet: colonia line, postcode, locality and state abbreviation):
  - `C.P.` / `CP` (código postal) stands in front of the digits and is part of the postal-code field as the writer set it; the standard value is the five digits. The code may come after the locality (`León, C.P. 37240`; `…, La Barca, Jalisco, C.P. 47912`) or before it with the locality and state after (`C.P. 38500, Apaseo el Alto, Guanajuato`). The word alone does not make the text Mexican: without a hint, a Mexican state or a colonia line is needed (Spain writes `C.P.` too).
  - A line that starts with `Col.`, `Colonia`, `Fracc.`, `Fraccionamiento`, `Barrio`, `Pueblo`, `Ejido`, `Residencial` or `Unidad Habitacional` is the dependent locality; a colonia that follows the street number on the same line (`… 105B Col. Los Angeles 27140 Torreón`) is a line of its own. A quarter named without such a word stays an additional line as written.
  - `S/N` (sin número) is the house number of a street without one. `Ciudad de México` and `Estado de México` are states, not the country; `Coahuila de Zaragoza` and `Michoacán de Ocampo` are read. A state name after `de` (`Tepatitlán de Morelos`) is part of the town. When the country stands on a line of its own, a `México` left on the locality line is the state. A lone `México` that could be the state or the country (`…, Ecatepec de Morelos, México, C.P. 55418`) is left to the model.
  - A legal form after a comma (`Servicios Díaz, S.C., Calle …`) belongs to the company name.

## Sources

UPU country sheets (`upu.int/UPU/media/upu/PostalEntitiesFiles/addressingUnit/<iso3>En.pdf`, retrieved 2026-11-05): Poland 08/2018, Czech Republic 02/2018, Slovakia 01/2013, Slovenia 05/2016, Croatia 05/2014, Hungary 01/2012, Romania 10/2007 (`rouEn`), Mexico 03/2017, Australia 07/2017. Own guides: Poczta Polska "Zasady poprawnego adresowania i oznakowania przesyłek listowych"; Česká pošta "How to correctly label the consignment" (about 19 recommended formats); Magyar Posta "Tájékoztató a levélküldemények helyes címzéséről"; Australia Post "Correct addressing" (November 2022) and "Address Presentation Standards" (1999). No licence is stated on the guides; facts and short examples only, cited by page. The examples used as tests are the 89 rows of `testdata/addresses/authority-examples-tier2c-south.tsv`: UPU Poland 4, Czech Republic 9, Slovakia 8, Slovenia 3, Croatia 3, Hungary 2, Romania 3, Mexico 2, Australia 6; Česká pošta 19; Australia Post 30 (1999 standards and 2022 brochure, element examples included). The libpostal dictionaries of `pl`, `cs`, `sk`, `sl`, `hr`, `hu` and `ro` at the pinned tag, through the ingest tool.
