# Address training data, Tier 2 group C (Norway, the Czech Republic, Latvia, Estonia, Denmark, Sweden, Finland, Iceland, Mexico, Poland, Slovenia, Slovakia, Lithuania, Croatia, Hungary, Romania, Australia)

How the examples for the address-field head are made for these countries. File format, label names, splits and determinism are those of `docs/training-data-address-tier2-a.md`; this page says what differs. The tool is `tools/data/AddressTier2C` (it shares the tokenizer, random generator, example type and span rules with the other address tools and the delimited-text reader with group A). Registers, licences and counts: `tools/data/provenance/address-tier2-c.md`.

## Running it

```
tools/data/fetch-tier2c.sh                        # the four registers, each file checked against tools/data/pins/*.pin
AddressTier2C count  --data <train dir> --out <dir> [--countries no,cz,lv,ee] [--targets NO=250000,...]
AddressTier2C render --data <train dir> --out <dir> [--seed 20261105] [--dev-per-mille 20]
AddressTier2C check  --out <dir>
```

`count` and `render` work as in group A: a per-region sampling rate gives every region the same number of records (small regions whole), dev is `hash(record id, split salt) mod 1000 < 20` fixed before rendering, repeated addresses are dropped, and the same inputs and seed give the same bytes.

## What is read from each register

| Country | Records used | Region (sampling stratum) | Notes |
|---|---|---|---|
| NO | road addresses (`vegadresse`) of Matrikkelen Adresse: street, number, letter, postcode, post office | the first two digits of the municipality number | Farm and cadastral addresses (`124/1`) are left out. The register writes post offices in capitals; the examples use title case, with the capitals as the "postal" casing variant. |
| CZ | RÚIAN address places with a house number (`č.p.`): street, house and orientation number, postcode, municipality, part of municipality, Prague district | first postcode digit | A village address has no street; the part of the municipality stands where the street would (`Libhošť 1`). Registration numbers (`č.ev.`) are left out. Prague is addressed by its district (`Praha 1`). |
| LV | existing buildings of VARIS, read from the register's own address text | municipality | `Brīvības iela 10`, building part `k-2`, farm names in quotes (`"Riņņi"`, written without them). Parish (`pag.`) is the dependent locality, municipality (`nov.`) the admin area. |
| EE | valid ADS address versions with a postcode: short address, municipality, settlement, city district, county | county | Flat numbers (`9-12`, `9a/4`) are labelled as unit numbers. A farm name with its number (`Kase-3`) stays one street name. |

## Layouts and variants

Street before number everywhere. Norway, the Czech Republic and Estonia put the postcode first on the last line (`0155 Oslo`, `110 00 Praha 1`, `48216 Jõgeva maakond`); Latvia puts it last (`Rīga, LV-1010`). The Czech postcode is written with the space in 90 % of examples, Latvia's `LV-` is part of the postcode span (and missing in 10 %, which the structural layer flags as an invalid format). Estonian rural addresses carry the municipality on its own line and may end with the county. Each example is a multi-line label or a one-line text with commas, spaces, dashes, semicolons or slashes. Drawn per example, with the rates in the manifest: casing, recipient and organisation lines from our own name parts (`tools/data/templates/tier2c-names.tsv`), a country line and a country code in front of the postcode, a post-box line, synthetic flat designators (`H0201` in Norway, `byt 5`, `dz. 5`), a missing postcode or locality, typing slips and doubled spaces.

## Mexico, Poland, Slovenia, Slovakia, Lithuania

These five use the same tool, splits, label set and determinism. Registers, licences and counts: `tools/data/provenance/address-tier2-c.md`.

```
tools/data/fetch-tier2c1.sh                       # MX, PL, SK and the LT geodatabase, each file checked against tools/data/pins/*.pin
python3 tools/data/fetch-si-rpe.py <dir>          # SI: pages through the public WFS once and writes the pinned files
python3 tools/data/convert-lt-gdb.py <gdb> <dir>  # LT: geodatabase to two plain tables (needs the pyogrio package as a file-format converter)
AddressTier2C convert --data <train dir> --countries pl,sk,si   # one-off plain tables for the large or awkward registers
```

The plain tables (`<register>/derived/<cc>-addresses.tsv.gz`) have one address per line: id, region, street, street kind, house number, suffix, flat, second number, locality, dependent locality, admin area, postcode. Their hashes are in the pins and the provenance file; the conversion is deterministic.

| Country | Records used | Region (sampling stratum) | Notes |
|---|---|---|---|
| MX | DENUE establishment addresses with a street name, an outside number (or `S/N`) and a postcode, one zip per state | state | Many establishments share an address; one of each is kept. The register writes capitals without accents in street names; the examples title-case them (`Av. Miguel Hidalgo y Costilla 577`). Kind of street (`AVENIDA`) and of neighbourhood (`COLONIA`) are written out or abbreviated at render time. The neighbourhood is the dependent locality (`Col. Roma Sur`), the municipality the locality, the state the admin area. Post codes that lost their leading zero are restored. |
| PL | PRG address points: street, house number, postcode, locality | voivodeship (first two digits of the municipality code) | `ul.` is added to a plain street name (78 %, "ulica" 8 %, none 14 %); `al.`, `pl.`, `os.` and the like stay as the register writes them. A village without street names is addressed by the village (`Gliny Małe 135`). The register gives the locality but not the post office town; the locality stands in for it. Flats are synthetic (`30/5`, `30 m. 5`, `30 lok. 5`). |
| SI | RPE house numbers with their street, settlement and post office | municipality | Last line is the post office name (`2314 Zgornja Polskava`). A village without street names is addressed by the settlement. The letter after a number is written with or without a space (`11 c`, `11c`). |
| SK | Register adries entrances in force: orientation number, street, building registration number, municipality, part, postcode | county | `Hlavná 12`, or with the registration number first (`Hlavná 15/12`) in 40 % of examples that have both. A building without a street link is addressed by the part of the municipality (or the municipality) and its registration number. The postcode is written `811 01` (85 %) or `81101`. |
| LT | address objects with their street or village, number, building block, flat (from the room table) and postcode; the locality is read from the register's own address text | municipality | `Gedimino pr. 10-5, LT-01103 Vilnius`; flats `-5`, `, butas 5`, ` b. 5`, `/5`; building block `15 k. 7` or `15, korpusas 7`. A village address carries its own line `Akmeniškių k., Vilniaus r. sav.`. `LT-` is part of the postcode span (and missing in 10 %). |

Recipient and organisation lines come from `tools/data/templates/tier2c-c1-names.tsv` (Polish, Slovak and Lithuanian family names are chosen so that a given name and a family name drawn apart never disagree in sex; invented, no register). Post box designators: `Apartado Postal`, `Skrytka pocztowa`, `Poštni predal`, `Poštový priečinok`, `Pašto dėžutė` and their short forms.

## Denmark, Sweden, Finland (with Åland) and Iceland

```
tools/data/fetch-nordic.sh                        # the Finnish and Icelandic registers and the GeoNames lists, each checked against tools/data/pins/{fi,is,nordic}.pin
AddressTier2C count  --data <train dir> --out <dir> --countries dk,se,fi,is
AddressTier2C render --data <train dir> --out <dir> --countries dk,se,fi,is
```

| Country | Records used | Region (sampling stratum) | Notes |
|---|---|---|---|
| FI | DVV building addresses (dBASE table) with one street name, a house number (`12`, `12 B`, `12-14`) and a postcode GeoNames knows; post office by postcode | the 19 regions of the register, Åland included | Finnish-only and Swedish-only addresses in their own language; bilingual addresses in Finnish. The staircase letter is written in capitals after a space in 40 % of examples (`Kauppakatu 5 B`) and a flat number may follow it. Åland addresses use `AX-` as the code prefix and `Åland` on the country line. |
| IS | Staðfangaskrá rows with a street or farm name and no plot, land or special name; letter kept as written | municipality number | A farm with no number is a street name alone (`Unusel, 561 Varmahlíð`), as for Latvian villages. |
| DK | GeoNames postcode areas with a drawn street name and number | region | Synthetic streets. Flat designations as floor and side (`2. th.`, `st. tv.`) in 3 % of examples. |
| SE | GeoNames postcode areas with a drawn street name and number | county | Synthetic streets. Postcode `123 45`, without the space in 10 %; apartment as `lgh 1101`. |

The last line is `postcode place` in all four (`2100 København Ø`, `123 45 Stockholm`, `00100 Helsinki`, `101 Reykjavík`), with the country code in front in 7 % (`DK-2100`, `SE-123 45`, `FI-00100`, `IS-101`). The postal casing variant writes the place in capitals for Finland and Sweden as for Norway. Post box lines are `Postboks`, `Box`, `PL` (Finnish) or `PB` (Swedish in Finland), and `Pósthólf`. Name parts for recipient and organisation lines are in `tools/data/templates/tier2c-names-nordic.tsv` (the renderer reads every `tier2c-names*.tsv`), street vocabulary in `tier2c-streets-nordic.tsv`.

Denmark, Sweden, Finland, Iceland, 

## Group C-3: Croatia, Hungary, Romania, Australia

```
tools/data/fetch-tier2c3.sh                        # the five source files, each checked against tools/data/pins/{hr,hu,ro,au,au-abs}.pin
AddressTier2C count  --data <train dir> --out <dir> --countries HR,HU,RO,AU
AddressTier2C render --data <train dir> --out <dir> --countries HR,HU,RO,AU
AddressTier2C check  --out <dir>
tools/data/try-tier2c3.sh                          # count, render, check and show examples for HU,AU,RO into scratch directories
```

The options, splits and determinism are those above; these countries are drawn by `RendererC3.cs` (layouts, rates and the labelled pieces are the same kind as the others) from `tools/data/templates/tier2c3-names.tsv` (recipient and organisation parts) and `tier2c3-streets.tsv`.

| Country | Records | Region (sampling stratum) | Notes |
|---|---|---|---|
| HR | every address of the INSPIRE Addresses file: street, number with letter, post office name and postcode, settlement | first two postcode digits | The street name includes its type (`Brežna ulica`, `Ulica Stjepana Radića`). A settlement that is not the post office goes on its own line (`dependent-locality`). Flats follow a slash (`12/3`, drawn). |
| HU | **synthesized**: each GeoNames postcode and place with 42 streets drawn from our list | county | Hungary has no open address register. Numbers are drawn (`12.`, `3/A.`, the full stop belongs to the number span). Half the label layouts follow the mail form (city, street, postcode on separate lines), the rest put the postcode first. Recipients are written family name first. |
| RO | street rows of the Posta Romana workbook for Bucharest (with sector) and for the localities over 50,000, and its village rows | Bucharest sector or county | Three numbers are drawn per row inside the register's range (steps of two), anywhere on a street whose number cell is empty. A village has no street: the village name (50 % with `Sat`) stands in the street place with a drawn number. `nr.` belongs to the house-number span; flats and blocks (`bl. 4, ap. 15`) are drawn. Types appear in full (`Strada`, `Bulevardul`) or short (`Str.`, `Bd.`); the register writes person-named streets surname first (`Mincu Ion`) and the examples keep that. 20 % of examples use the cedilla letters. |
| AU | **synthesized**: each ABS suburb or locality with its postal area (SAL to POA correspondence, ASGS Edition 3), plus the GeoNames postcode, place and state pairs ABS lacks, with 14 streets drawn from our list per place | state | Number before street; units as `Unit 3, 45 Smith Street` or `3/45 Smith Street`; the "postal" casing is Australia Post's capitals with short street types (`45 SMITH ST`, `SYDNEY NSW 2000`); state as the abbreviation or the full name. |

Croatia, Hungary and Romania also draw an ASCII variant (diacritics dropped, 7 %), as people type them. Counts, licences and hashes: `tools/data/provenance/address-tier2-c.md`.
