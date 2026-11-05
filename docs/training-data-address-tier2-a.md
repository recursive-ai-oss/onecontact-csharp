# Address training data, Tier 2 group A (France, Belgium, the Netherlands, Luxembourg, Germany, Austria, Switzerland, Liechtenstein, Monaco)

How the examples for the address-field head are made for these countries, and what is different from the United States and Canada (`docs/training-data-address-us-ca.md`, whose file format and label names this follows). The tool is `tools/data/AddressTier2A`; registers, licences and counts are in `tools/data/provenance/address-tier2-a.md`.

## Running it

```
tools/data/fetch-tier2a.sh                       # the registers, each file checked against tools/data/pins/*.pin
AddressTier2A count  --data <train dir> --out <dir> [--countries fr,be,...] [--targets FR=310000,...]
AddressTier2A render --data <train dir> --out <dir> [--seed 20261105] [--dev-per-mille 20]
AddressTier2A check  --out <dir>                 # span rules on every line
AddressTier2A sample --out <dir> --per-country 300   # what the library's rule parser does with a dev sample
```

`count` reads each register once and writes `selection.tsv`: usable records per region and a sampling rate that gives every region the same number of records (small regions whole), with the total near the country's target. `render` keeps a record when `hash(record id, selection salt)` is below its region's rate, drops an address that repeats one already kept (not for Monaco, which is drawn with replacement), assigns dev by `hash(record id, split salt) mod 1000 < 20` before rendering, and writes `<cc>.train.jsonl`, `<cc>.dev.jsonl` and `manifest.json` (seed, renderer version, pinned register files, per-file SHA-256 and counts, rates, variants drawn). The same inputs and seed give the same bytes.

## What is read from each register

| Country | Fields used | Region (sampling stratum) | Language of the example |
|---|---|---|---|
| FR | house number, repetition index, street, commune, the postal (upper-case) locality, lieu-dit, postcode | département (971–976 for the overseas departments; the collectivities 975, 977, 978 and 98x are left out) | fr |
| BE | house number, box, street and place in fr/nl/de, postcode | region (BRU, VLG, WAL) | nl in Flanders; fr or nl in Brussels; de where the register gives German names, else fr |
| NL | house number, letter, addition, street, place, postcode | the first two postcode digits | nl |
| LU | street, number, locality, postcode | commune | lb (street names in Luxembourgish, French or German; French-typed names are written number-first) |
| DE | street, number, addition, municipality (North Rhine-Westphalia only) | Regierungsbezirk | de |
| AT | street, number, postcode, Zustellort | federal state | de |
| CH | street, number, ZIP label, canton | canton | de, fr or it from the street's type word, else the canton's main language |
| LI | as CH | LI | de |
| MC | none: synthesized | MC | fr |

## Labels

The US/CA label set, with two additions for these countries: `sorting-code` (France's `CEDEX 08`) and `dependent-locality` (a French lieu-dit, a Monaco quarter). `street.type` is split from the name only where the type is a separate word in front of the name (French and Italian: `Rue`, `Avenue`, `Via`); Dutch and German types are part of the name (`Kerkstraat`, `Hauptstraße`) and stay inside `street.name`. A country code in front of the postcode (`F-75008`, `CH-8001`, `FL-9490`) is labelled `country` with the hyphen outside; Luxembourg's `L-` is labelled the same way (the structural layer's postcode pattern is the four digits alone). `street.unit-*` carries Belgian `bus`/`boîte` with the box, and the French, German and Austrian secondary-address words (`Bâtiment`, `Appartement`, `Wohnung`, `Top`); `street.box-*` carries `BP`, `CS`, `TSA`, `Postfach`, `Postbus`, `Case postale`, `B.P.`.

## Layouts and variants

Number before the street in France, Monaco and Luxembourg's French-typed streets (`12 rue de la Paix`, `12, rue de la Paix`); after it elsewhere (`Hauptstraße 12`). The postcode line puts the postcode first (`75008 Paris`, `1234 AB Amsterdam`, `L-1234 Luxembourg`). Each example is a multi-line label or a single line with commas, spaces, dashes, semicolons or slashes. Drawn per example, with the rates in the manifest: casing (as in the register, upper, lower, and the postal service's own form: France's upper-case without diacritics, PostNL's upper-case locality), the second language of Brussels and German-speaking Wallonia (and a bilingual `Rue … / …straat` street in 3 % of Brussels examples), abbreviated French types (`av.`, `bd`, `ch.`) and German `…str.`, `ß`→`ss` (always in Switzerland and Liechtenstein), Dutch and Belgian number additions (`12A`, `12-2`, `12/3`, `bus 3`), French `bis`/`ter`, a recipient or organisation line from our own name parts (`tools/data/templates/tier2a-names.tsv`), a country line, a post-box line, `CEDEX`, a synthetic secondary-address line, a missing postcode or locality, typing slips, glued and doubled spaces.

## Two gaps, stated plainly

* **Germany is one state.** No national open address register exists; the only permissive state file fetched is North Rhine-Westphalia's building references (`dl-de/zero-2-0`). Its streets and municipalities are real; it has **no postcodes**, so each address gets a synthetic one inside NRW's real two-digit ranges (a fixed hash of municipality and street; stated in the manifest). Labelling is unaffected; postcode-to-place agreement is not real. Other states (Hamburg, Berlin, Brandenburg, Sachsen and others publish under `dl-de/by-2-0` in GML or per-county files) are a follow-up.
* **Monaco is synthetic**: about 40 well-known public streets, numbers drawn per street, the single postcode 98000.
