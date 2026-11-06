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
