# Address training data, Tier 2 group B (United Kingdom, Spain, Italy, Portugal, Ireland, Malta, San Marino, the Vatican, Andorra, Jersey, Guernsey, the Isle of Man)

How the examples for the address-field head are made for these countries. The file format, label names, splits and determinism are those of the other address tools (`docs/training-data-address-us-ca.md`, `docs/training-data-address-tier2-a.md`); this note covers what differs. The tool is `tools/data/AddressTier2B`; sources, licences and counts are in `tools/data/provenance/address-tier2-b.md`.

## Running it

```
tools/data/fetch-tier2b.sh                       # the sources, each file checked against tools/data/pins/*.pin
AddressTier2B count  --data <train dir> --out <dir> [--countries gb,es,...] [--targets GB=300000,...]
AddressTier2B render --data <train dir> --out <dir> [--seed 20261105] [--dev-per-mille 20]
AddressTier2B check  --out <dir>                 # span rules on every line
AddressTier2B sample --out <dir> --per-country 300   # what the library's rule parser does with a dev sample
tools/data/verify-tier2b.sh <dll> <train dir> <out dir>   # render twice, compare every byte, check, sample
```

`count` and `render` work as in group A: a per-region sampling rate gives every region the same number of records (small regions whole) with the total near the target, dev is `hash(record id, split salt) mod 1000 < 20` taken before rendering (same salts as group A), and the same inputs and seed give the same bytes. The tool shares the tokenizer, random generator, example type, span rules, delimited-text reader and rule-parser sample with the other address tools by compiling their files; it adds its own readers, lexicon and renderer.

## What is real and what is drawn

| Country | Street, number | Locality, postcode | Drawn |
|---|---|---|---|
| GB | every named road of OS Open Names | the road's populated place; the postcode units nearest to the road in its 10 km tile and postcode district | house number (many low numbers, a tail to ~180), a letter after it (4 %), flats |
| ES | Catastro portals: street (with its type code), number, postcode, municipality | as the portal | floors and doors, post-box lines |
| IT | ANNCSU civic numbers (street, number, exponent, frazione, South Tyrol's German name) | comune name from the ISTAT list; the CAP from GeoNames by comune, picked by a fixed hash where a city has several | secondary-address lines |
| PT | our vocabulary (`tier2b-streets.tsv`) | GeoNames postcode (seven digits) and place; the concelho as the post-office name in 40 % | street, house number |
| IE | our vocabulary | GeoNames routing key and place | street, number, the last four characters of the Eircode |
| MT | our vocabulary (Maltese and English) | GeoNames locality code and name (Maltese name in 30 %) | street, number, the four digits of the postcode |
| SM, VA, AD | our vocabulary | GeoNames postcode and place | street, number |
| JE, GG, IM | our vocabulary (shared) | GeoNames postcode area and parish/place | street, number, the inward part of the postcode |

A drawn postcode part is valid in form and unreal in fact; the manifest counts `postcode:drawn`. In the United Kingdom the road/postcode pairing is a stand-in for an address register that does not exist openly: the pair is plausible, not a statement about which postcode the street's houses have. No Royal Mail PAF, OS AddressBase or Eircode data is used.

## Labels and layouts

The group A label set: `street.house-number`, `street.type`, `street.name`, `street.unit-designator`, `street.unit-number`, `street.box-designator`, `street.box-number`, `dependent-locality`, `locality`, `admin-area`, `postal-code`, `country`, `recipient`, `organization`.

* **Street type** is labelled where it is a separate word: after the name in English (`Church` `Road`), before it in Spanish, Catalan, Galician, Italian, Portuguese, Maltese and Andorran Catalan (`Calle` `Mayor`, `Via` `Roma`, `Rua` `Augusta`, `Triq` `ir-Repubblika`). Spanish `C/`, Italian `V.le` and similar are the type with the slash or period inside the word or after it.
* **Number position:** before the street in the English-speaking countries and in half of the Maltese ones; after it in Spain (`Calle Mayor, 12`), Portugal, Italy, San Marino, the Vatican and Andorra, with a comma in most Spanish, Portuguese and Andorran examples and in about half of the Italian ones. An Italian exponent is written `12/A`, `12A` or `12 A`.
* **Spanish floors and doors** (`3º B`, `Bajo`, `1.º Dcha.`) and Portuguese ones (`2.º Esq.`, `R/C Dto.`) are one `street.unit-number` span; Italian `Scala B`, `Int. 5` and English `Flat 3` have a designator span and a number span.
* **Last line:** UK `Post Town`, optional county line, postcode (own line, or after the town on the same line); Ireland locality then Eircode; Spain `28864 Ajalvir`, with the province after it in 30 % (`(Madrid)`, `, Madrid` or its own line); Italy `00145 Roma RM` with the province sigla (`admin-area`) in three quarters; Portugal `1100-053 Lisboa`; Malta `Valletta VLT 1117`; San Marino, the Vatican, Andorra postcode first (`AD500 Andorra la Vella`, `AD-500` in 10 %).
* **Languages:** Welsh and Gaelic second names of OS Open Names roads (`cy`, `gd`), South Tyrol's German names (`de`), the Catalan, Galician and Spanish forms of Spanish street types (`ca` in Catalonia, the Balearic Islands and the Valencian provinces at 35–60 %, `gl` in Galicia at 55 %), Maltese and English, Andorran Catalan with Spanish (6 %) and French (6 %) forms.
* **Variants drawn** (rates in the manifest): casing (as the source writes it, UPPER, lower, and the postal form: upper-case locality), type abbreviations, recipient and organisation lines from our own name parts (`tier2b-names.tsv`), a country line, a post-box line, a missing postcode or locality, typing slips, doubled spaces, a flat or unit line.

## Gaps stated plainly

* **Spain:** the Catastro does not serve the Basque Country or Navarre (their cadastres are separate), so those provinces are absent and Basque-language street names do not occur. About 12 % of the municipalities are fetched (917 of the feed's municipal files; chosen by a fixed hash of the municipality code); a street whose name carries a parenthesised article (`CL IGLESIA (DE LA)`), a number with letters, or an unlisted type code is left out.
* **Italy:** the open civic-number file has no postcode. A city with several CAPs gets one by a fixed hash, so the street-to-CAP pairing there is not real (the manifest counts `postcode:drawn`). Comuni the GeoNames list does not name are left out (4 % of rows).
* **Portugal, Ireland, Malta, San Marino, the Vatican, Andorra, Jersey, Guernsey, Isle of Man:** the street names are our own vocabulary, not a register. Ireland has no county (the postcode list has none) and an Irish-language form does not occur. The Portuguese national address base (Base Nacional de Moradas) is published on dados.gov.pt, which was not reachable from the build network, so it is not used.
* **Northern Ireland** is absent: OS Open Names covers Great Britain.
* **Structural check against the pinned layer** (information, in `rule-sample.md`): Great Britain, Jersey, Guernsey, the Isle of Man, Malta, Portugal, San Marino and the Vatican pass; Spain, Italy and Andorra examples without a province lack a required `AdminArea` (the data file requires it); Ireland's postcode is `UnexpectedField` because the pinned data file gives Ireland no postcode field.
