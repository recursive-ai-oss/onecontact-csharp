# Address training data, Tier 2 group C (Norway, the Czech Republic, Latvia, Estonia, Denmark, Sweden, Finland, Iceland so far)

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

## Not yet covered

Lithuania, Poland, Slovakia, Slovenia, Croatia, Hungary, Romania, Mexico and Australia have no loader yet; the provenance file records what was found out about each source.

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
