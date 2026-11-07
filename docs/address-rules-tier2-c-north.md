# Address rules: Denmark, Sweden, Finland, Norway, Iceland, Estonia, Latvia, Lithuania

How the rule parser (`Addresses/Tier2`) reads and standardizes these addresses. They are rows in tables of their own (`Addresses/Data/T2/*-north.tsv`), read by the same reader as the other Tier 2 countries; `docs/address-rules-tier2-a.md` and `-b.md` describe the shared parts. This page lists what is particular to these eight. The sources are the posts' free public guidance where we found it, and the UPU country sheets for the rest (below). No paid standard (DS, SS, SFS, NS, LVS, LST) was bought or read.

## What the rules decide

1. **Last line.** The postcode and the locality end the address. The country decides the form:
   - *four digits, left of the locality*: Denmark (`8660 SKANDERBORG`) and Norway (`0025 OSLO`);
   - *three digits*: Iceland (`220 HAFNARFIRĐI`);
   - *five digits with a space after the third*: Sweden (`123 45 LILLBYN`, the joined `12345` is read and written with the space);
   - *five digits*: Finland, Estonia (`11212 TALLINN`; in the country-side form the locality is the region, `69501 VILJANDIMAA`) and Lithuania (`LT-04340 Vilnius`);
   - *locality first*: Latvia, `RIGA, LV-1050`. The comma and the postcode may be on the line below the locality (`SALACGRIVAS NOV.,` / `LV–4035`).

   A country prefix before the postcode (`DK-`, `SE-`, `S-`, `FI-`, `NO-`, `N-`, `IS-`, `EE-`, `LV-`, `LT-`, also with an en or em dash) names the country.
2. **Country.** From the country name (English, the country's own language, French, German: `DENMARK`, `DANMARK`, `SVERIGE`, `SUOMI`, `NORGE`, `ISLAND`, `EESTI`, `LATVIJA`, `LIETUVA` …), the prefix, a hint, or `LV-` (the only form of its kind). The digits alone never decide: five digits belong to Finland, Estonia, Lithuania, Sweden, France, Germany, Spain and Italy; four to Denmark, Norway, Belgium, Austria, Switzerland, Liechtenstein, Luxembourg; and three digits are a postcode only when Iceland is known. Without evidence the result is `address.country-undetermined`. With a named or hinted Finland, Estonia, Lithuania or Sweden, five digits are that country's even where another form would use them (`00120` is Helsinki, not the Vatican; `97xxx` and `98xxx` exist in Finland and Estonia).
3. **Delivery lines.**
   - *Street line*: the street name, then the house number, then floor, door or apartment. The name has no required type word (`Kastanievej`, `Karl Johansgate`, `Laisvės pr.`, `Kr. Barona street`). The number may carry a letter (`25 B`, `10a`) and an apartment after a dash (`6–1`, `40-12`, `90—23`), a slash for a double number or building (`48/50`, `45/4—22`).
   - *Floor and door*: after the number, written as the writer set them: `15, 2` and `34, 1. th` and `34, st. a19` (Danmarks Adresser), `25 B 13` and `4 A 16 Ullakkohuoneisto` (UPU Finland), `lgh 1234` (PostNord), `7, dz. 1` (Latvian rules). Commas the writer set are kept; no period, `sal` or door letter is added or changed.
   - *A supplementary town name* after the last comma of a street line (`Kastanievej 15, 2, Agerskov`) stays on the line; on one-line input it becomes the next delivery line.
   - *Boxes*: `Postboks` (DK, NO), `PO Box` (NO), `BOX` (SE), `PL`, `PB`, `P.O.Box` (FI), `Pósthólf` (IS), `P. d.` (LT). A name after the number (`Postboks 55 Falkum`, a box facility) stays on the line.
   - *Poste restante*: `Poste restante` (FI, NO), `Nõudmiseni` (EE), `Iki pareikalavimo` (LT).
   - Lines above the first delivery line are kept as written (addressee, organization, `Att:`, `c/o`, `v/`, `Bolignummer H0302`, a farm name). A line that starts with `c/o`, `Bolignummer` or `Bnr` is never the street line.
   - *No delivery line at all* is an address: a farm, a village or a place name stands in for the street in Norway (`Vikane`), Estonia (`Allika talu`, `Halliste alevik`) and Lithuania (`Galinių k.`, `Dubingių pšt.`), and a recipient with the postcode alone is a Norwegian address (`9672 INGØY`). Up to six name lines without digits are taken, with no warning.
4. **Everything else** is refused with `address.model-required`. Nothing is invented: no town from a postcode, no street type, no floor, no country from digits.

## Postal and Display

`Postal` follows each sheet's own example lines; `Display` writes the same tokens in natural title case (`123 45 Lillbyn`, `Nyby 10`; an all-capital organization suffix such as `AB`, `AS`, `Oy`, `PL`, `PO` stays).

| Country | Postal | Source |
|---|---|---|
| DK | Lines as written (street, number, floor, side and supplementary town name stay as set); the locality in capitals; box line `Postboks 321`; postcode without country code. | UPU Denmark 05/2024; Danmarks Adresser, *Gengivelse af adresse* A.1-A.4 |
| SE | Lines as written; locality in capitals; `123 45`; box line `BOX 1121`; the apartment number after the street on the same line (`lgh 1234`). | UPU Sweden 11/2024; PostNord, *Adressering* |
| FI | Lines as written; locality in capitals; `PL 900`, `P.O.Box 102`; `c/o` above the street line. | UPU Finland 11/2024 |
| NO | Lines as written; locality in capitals; `Postboks 1181`, `PO Box 15`. | UPU Norway 01/2013; Posten Norge, *Adressering* |
| IS | Lines as written; locality in capitals; `Pósthólf 1550`. | UPU Iceland 07/2002 |
| EE | Lines as written; locality or region in capitals; the house and apartment number as written (`6–1`). | UPU Estonia 06/2014 |
| LV | Lines as written; the locality in capitals, a comma, then `LV-` and the four digits on one line. | UPU Latvia 09/2023; Latvian rules for unified postal addresses (Latvijas Vēstnesis, id 51772) |
| LT | Lines as written; `LT-` and the five digits, then the locality as written (the sheet prints `Vilnius` and `Kaunas` in mixed case and province names in capitals). | UPU Lithuania 06/2018; Lietuvos paštas, *Siuntos* (post.lt) |

The country line (English, in capitals for Postal) is printed only when asked: `DENMARK`, `SWEDEN`, `FINLAND`, `NORWAY`, `ICELAND`, `ESTONIA`, `LATVIA`, `LITHUANIA`.

## Where the sources disagree or stop short

- **Locality case.** The UPU sheets print the locality in capitals; Posten Norge's own page and the Danish address register print it in mixed case (`0161 Oslo`, `1553 København V`). `Postal` follows the UPU sheets; the mixed-case form is the `Display` rendering, and the tests pin it so. Lithuania is the one sheet that prints the locality in mixed case, and its Postal keeps it.
- **Dash.** Latvia and Lithuania print `LV–1050`, `LT–04340` with an en dash. `Postal` writes the hyphen (as the Portuguese postcode does). The house and apartment dash is kept as written.
- **Latvian locality line.** The sheet prints `SALACGRIVAS NOV.,` and `LV–4035` on two lines and `RIGA, LV–1050` on one; `Postal` writes both on one line, which is what the sheet's rule says (the postcode right of the locality, separated by a comma). The trailing comma after a Latvian sub-locality (`Valdemāra street 42, Ainaži,`) is dropped.
- **The Latvian street line** is two lines in the sheet's diagram (`Kr. Barona street` / `7, dz. 1`); the extracted text of the sheet suggests one wrapped line, which is how the test reads it.
- **Foreign addresses on Posten Norge's page** (`DK-5000 Odense`, `IS-107 Reykjavik`, `SE-582 20 Linköping`) are Norwegian-post renderings of other countries' addresses; they are read (country, postcode, locality) and not compared as Postal examples, because each country's own sheet decides its Postal form.
- **Not read by the rules, so refused:** the Latvia Post contact block (`Ziemeļu iela 10. Lidosta “Rīga”`: a place name after the number) and Latvian post-office suffixes such as `Rīga–50`; the model decides those. The Iceland sheet's contact line is transcribed as the PDF text gives it (`Stórhöfð1 29`, which looks like an extraction artifact of `Stórhöfði 29`).
- **Posti** (Finland), **Omniva** (Estonia), **Latvijas Pasts** and **Íslandspóstur** publish no free addressing page we could find with examples of their own; their UPU sheets stand in. Finland's language-board guidance (Kielitoimiston ohjepankki) says the same about the postcode and locality but is not a post's guide and is not used for examples.
- The Danish registry's `Postal` conventions (`th.`, `tv.` with a period; floors with a period) are written as the writer wrote them; nothing is added or rewritten.
- Norwegian Nynorsk has no libpostal dictionary of its own; the Bokmål one (`nb`) covers both.

## Expansions

`ExpansionLanguages.Danish`, `Swedish`, `Finnish`, `Norwegian`, `Icelandic`, `Estonian`, `Latvian` and `Lithuanian` use the libpostal dictionaries `da`, `sv`, `fi`, `nb`, `is`, `et`, `lv`, `lt` (street types, directionals, unit and level words, post office words, ambiguous expansions) at the pinned tag, listed in `upstream/libpostal-dictionaries/PIN`. Each language keeps its own ambiguous-expansion list. The German and Dutch ending expansion (`Hauptstr` → `Hauptstrasse`) is not applied to the northern languages. Keys of the United States, Canada and the first two groups do not change.

## Labels and extra forms (recovery round)

These forms are read by rules:

- **Country code before the postcode** (`DK-1352`, `NO-6419`, `SE-839 89`, `IS-102`, `FI-02970`, `EE-`): the code is the `country` field and the postcode is the digits after the dash. Latvian and Lithuanian postcodes keep `LV-` and `LT-` as part of the postcode.
- **Unit labels.** Floor and door as the writer set them (`1. th`, `st. a19`, `13`, a Norwegian dwelling number `H0219`) are one unit number; a designator word (`lgh`, `dz.`, `korp.`, Estonian `korter`) is the designator and what follows it the number. In Estonia, Latvia and Lithuania the apartment after the house number's dash (`6–1`, `2 - 22`, and in Estonia also a slash, `46/49`) is the unit number; a Latvian building (`5 k-8`) stays part of the house number. The dash keeps its spacing in Postal.
- **Box words**: `Pb.` (DK, NO), `Postbox` (SE), `Postilokero` (FI), `Pósth.` (IS), `Postkast`, `PK` (EE), `Pasta kastīte`, `PK`, `p/k` (LV).
- **A company name that runs into the street on one line** is split after its legal form (`ApS`, `A/S`, `AB`, `Oy`, `AS`, `ehf`, …) when the rest is a street line. A person's name that runs into the street is not split: where the name ends and the street begins is not certain, so such a line is read as a street name or refused, and the model decides.
- **Latvia.** `Valmiera, Valmieras nov., LV-4201`: the municipality (`nov.`) is the admin area and the town the locality; a parish (`pag.`) line or segment is the dependent locality, and with a parish and no street the line above it is the house name (the Latvian rules: the house name stands where the street would in the countryside). A parish with the house name in front of it on one line (`Saulītes Sēļu pag. Sēļi`) is split into house name, parish and locality.
- **Estonia.** `48103 Põltsamaa Jõgeva maakond`: the county is the admin area; a `vald`, `linn` or `linnaosa` line is the dependent locality; with such a line (or a village locality and a line besides the addressee) and no street, the line above is the farm or house name.

What the rules leave to the model on purpose: a lone name line above a postcode in Iceland, Norway, Finland and Estonia (recipient or farm name: the sheets do not say), a Latvian `Name 8` without `iela` (a street or a farm, the Latvian rules allow both), a person's name glued to a street, a street and a locality on one line (`Marijas iela 22 Rūjiena`), and a postcode written without its `LV-`.

### Second recovery round: Lithuania

- **Village and municipality.** The UPU Lithuania sheet prints the rural address with the municipality (`RASEINIU R. SAV.`) and the locality apart. A line (or comma segment, or the words after the house number on one line) ending in `k.` (kaimas) is the dependent locality, including `Dabužių I k.`; one ending in `sav.` (`r. sav.`, `m. sav.`) is the admin area. `Pienionių k., Anykščių r. sav.` in one line of a label is two lines. The municipality stays where the writer put it in the printed layout; only its field changes.
- **Box.** `PD 2876` and `Pašto dėžutė 12` as well as the sheet's `P. d.`.
- **Apartment.** `butas 1` and `b. 3` after the house number are the designator and the unit number. A slash between house and apartment (`66/72`) is read like the sheet's dash, printed as written.
- **Company name.** `MB` after a comma (`Sakalauskas ir Ko, MB, Aušros g. 23`) belongs to the name before it; as `UAB` and `AB` it also ends a company name that runs into the street on one line.

## Country inference (no hint)

Without a country hint the reader first tries the plain Tier 2 read. When that fails as `country-undetermined` or `no-last-line`, and the text has a digit, is at most 600 characters and shows some evidence of a language, it is read once as each Tier 2 country (a trial read with the candidate as the hint). The country is chosen only if:

1. exactly one country among those that share the postcode shape has evidence in the text, and
2. that country's trial read succeeds.

The result then carries confidence 0.97 and the `CountryInferred` flag. Otherwise the text is refused as before; nothing is guessed.

**Evidence** (data in `Data/T2/t2-inference.tsv`): letters at or above U+0100 and `ß` (Latin-1 letters never count, since names carry them); the country's street and place words and suffixes; an abbreviation directly before a house number of up to four digits (`g.`, `pr.`, `al.`, `tn`, `mnt`, `pst`, `Top`); an ordinal before a floor word (`3. sal`); Liechtenstein's municipality names; and, for the Czech Republic and Slovakia, the postcode's first digit (1-7 Czech, 0 and 8-9 Slovak; a leading 0 counts only when written `NNN NN`).

**Vetoes** stop an inference even when the evidence is unique: the text ends in another country's name, a zone of another country, or an Italian province code left in the locality; a Dutch-looking `dddd LL`; an unspaced five-digit postcode in Swedish form (it may be Finnish with Swedish words); letters outside the winner's alphabet.

**Refused by design.** German-language text in Austria, Switzerland, Belgium and Luxembourg without a country name or distinctive word; Czech and Slovak ASCII text whose postcode falls in no zone; Danish and Norwegian text built from shared words (`Postboks ... Oslo`); lines without a postcode (`Verslo g. 5, Vilnius`), which count as evidence only. No gazetteer of places ships, so none is used.

## A person written in front of the street

Denmark, Norway, Sweden and Finland write a street name as one word or end it in a type word (`Karl Johans gate`, `Ole Bulls plass`; Posten Norge, *Adressering*; PostNord; Danmarks Adresser, *Gengivelse af adresse*). Exactly three words before the house number that start with a given name and do not end in a type word (`Nils Larsen Nestangen 58`) are a person written in front of a one-word street with no comma or line break; longer names are read as before, since a four-word street name is as likely as a person with a middle name. Where the person ends cannot be told from the words, so the rules refuse the line and the model reads it; they used to read the person as part of the street name. The given names are our own list (`docs/names.md`).

## The Estonian county beside the town

Omniva (*Kuidas kirja adresseerida?*) writes the postcode, the town and then the county (`maakond`). Web pages also put the county on a line of its own after the town (`Pärnu mnt 12, 10148 Tallinn, Harju maakond`, `Pärnu mnt 12, Tallinn 10148, Harju maakond`), or between the town and the postcode (`Ülikooli 18, Tartu, Tartu maakond, 51003`, `Tallinn, Harju maakond 10148`). A county line after the postcode line is joined to it, and a county that stands between a plain town word and the postcode makes that word the locality and the county the admin area. A village, rural municipality or town-status word (`küla`, `vald`, `linn`, `alevik`) before the county keeps the reading above. The postcode may carry the country letters (`EE-80010`; UPU Estonia sheet).

