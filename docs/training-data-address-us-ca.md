# Address training data, United States and Canada

How the examples for the address-field head are made, what a line looks like, and what the labels mean. The tool is `tools/data/AddressUsCa`; where the data comes from and its licences are in `tools/data/provenance/address-us-ca.md`.

## Running it

```
tools/data/fetch-nad.sh        # US DOT National Address Database, text file (8.5 GB), checked against pins/nad.pin
tools/data/fetch-oda.sh        # Statistics Canada Open Database of Addresses, ten files, checked against pins/oda.pin
tools/data/fetch-tiger.sh      # Census TIGER/Line 2025 address ranges for the regions NAD lacks, checked against pins/tiger.pin
AddressUsCa count  --nad <NAD_TXT.zip> --oda <dir of ODA zips> --out <dir>      # usable records per region, sampling rates
AddressUsCa render --nad <NAD_TXT.zip> --oda <dir of ODA zips> --out <dir> --seed 20261105
AddressUsCa check  --out <dir> --sample 5000 [--offset N --misses <file>]
AddressUsCa count  --tiger <dir> --out <dir> --caps PR=80000,VI=24500,*=27000 --per-range '*=3,VI=16,AS=16,MP=16'
AddressUsCa render --tiger <dir> --out <dir> --countries us --dataset address-us-tiger --seed 20261105
AddressUsCa check  --out <dir> --sample 3000 --per-region 600     # rule-parser recovery per region
```

`check` with `--offset N` takes line N of every sampling step instead of the first (so a sample used for tuning and a fresh one for measuring share no example), skips the span checks, and with `--misses` writes every example the rules did not fully recover, with the missed and the extra fields.

The registers go to `$ONECONTACT_DATA/train/<register>` (default `~/onecontact/data/train`), never into the repository. `count` reads both registers once and writes `selection.tsv`: for each country and region the number of usable records and a sampling rate, chosen so that every region gets the same number of records (small regions are taken whole) and the total is near the target (1.3 M US, 380 k CA). `render` keeps a record when `hash(record id, selection salt)` is below its region's rate, drops a record whose address repeats one already kept, and writes the examples and `manifest.json`.

## Files

`us.train.jsonl`, `us.dev.jsonl`, `ca.train.jsonl`, `ca.dev.jsonl`: JSON Lines in the pipeline's training format (one object per line; unknown properties are ignored by the reader).

| Property | Meaning |
|---|---|
| `text` | The address as free text, one line or several (`\n`). |
| `tokens` | `[start, end]` UTF-16 offsets. Letters, digits and combining marks form a token; every other non-space character is a token on its own, so a hyphen or apostrophe never joins words; tokens are also cut at every span edge. A model that reads these files must use the given tokens (or this rule): the pipeline's default tokenizer keeps `4-123` and `O'Brien` as one token. |
| `entities` | `[start, end, "label"]`, flat, in order, not overlapping, each edge a token edge. Everything not labelled is outside (separators, punctuation). |
| `spans.fields` | `[start, end, "street-address"]`: the delivery line, from its first labelled part to its last. |
| `id`, `country`, `lang`, `layout`, `region` | Source record id (`nad:` or `oda:` plus the register's own id), `US` or `CA`, `en` or `fr`, the layout used, state or province. |

Every letter and digit of `text` lies inside an `entities` span; the `check` command verifies this and the other span rules on every line.

## Labels

The names are the ones the rule parser emits, so rule output and training labels compare directly.

| Label | Covers |
|---|---|
| `recipient`, `organization` | A whole line of invented text (a title, "Attn:" or "c/o" included). |
| `street.house-number` | `123`, `12B`, `W123N456`. |
| `street.fraction` | `1/2` after a house number. |
| `street.pre-directional`, `street.post-directional` | `N`, `Northwest`, `O` (Ouest), spelled out or abbreviated; the period of an abbreviation is outside the span. |
| `street.name` | The street name, including words the register keeps in the name (`County Road 12`, `du Parc`). |
| `street.type` | `St`, `Avenue`, `Boul`; before the name in French. |
| `street.unit-designator`, `street.unit-number` | `Apt`, `#`, `Suite`, `APP`, `BUREAU` and the number or letters; the `4` of `4-123 Main St` is a unit number with the hyphen outside. |
| `street.box-designator`, `street.box-number` | `PO Box 12`, `CP 12`, and `BOX 5` after a route or military unit. |
| `street.route-designator`, `street.route-number` | `RR 2`, `HC 64`. |
| `street.station-designator`, `street.station-name` | Canada Post `STN A`, `SUCC B`, `RPO`. |
| `street.military-designator`, `street.military-number` | `PSC 1234`, `UNIT 8672`, `CMR`. |
| `street.general-delivery` | `General Delivery`, `GD`, `Poste restante`. |
| `urbanization` | Puerto Rico: the urbanization line (`URB LAS GLADIOLAS`, `Urbanización X`, or the bare name), one span. |
| `locality` | The city, or `APO`, `FPO`, `DPO`. |
| `admin-area` | State, province or territory, as a code or a name (`QC`, `Québec`, `Quebec`). |
| `postal-code` | ZIP, ZIP+4, or a Canadian postal code with or without its space. |
| `country` | `USA`, `United States`, `Canada`, `CAN`. |

## Layouts and variants

`tools/data/templates/layouts.tsv` holds the layouts (USPS Publication 28 labels, Canada Post labels in English and French, and single lines with commas, without, with semicolons, with the province in parentheses); `names.tsv` the invented name parts; `oda-excluded-providers.tsv` the providers left out. A template names slots (`{city}{, |state}{ |zip}`); the punctuation before or after a slot is written only when the slot has content, so a missing ZIP removes its comma. The variants drawn per example are casing, abbreviated or spelled-out street types and directionals (from the Publication 28 and Canada Post rule tables), unit placement and designator, optional recipient, organisation and country, PO box, rural route, highway contract route, military and general-delivery lines, missing fields, ZIP+4, French civic forms (type before the name, a comma after the number, `St-`/`Ste-` for saint), and typing or OCR edits. The rates are in the manifest.

## Regions the National Address Database lacks

Puerto Rico, the territories and five states have no (or few) rows in NAD. `--tiger` reads Census TIGER/Line county address-range tables (the `.dbf` inside each `*_addrfeat.zip`; `*_edges.zip` for American Samoa and the Northern Mariana Islands, which have no ranges) and the ZCTA to place relationship file, and makes one record per house number drawn inside an address range. Street names come from the Census with their abbreviations, split with the Publication 28 tables; the house number and the city are synthetic (see `tools/data/provenance/address-us-ca.md`). Puerto Rico uses `templates/layouts-pr.tsv`: Spanish street prefixes stay in the street name (Publication 28 appendix I section 51.1), and the urbanization line comes before the delivery line. Option `--caps` replaces the shared per-region cap of `count` with fixed caps, `--per-range` sets how many numbers are drawn from each range, `--countries` and `--dataset` name the output files and the manifest.
