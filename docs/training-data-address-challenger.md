# Address training data for the challenger model (OpenStreetMap, ODbL)

How the examples for a challenger address-field model are made from the OpenStreetMap address file of the published libpostal parser training set. The file format, tokens and span rules are those of the other address tools (`docs/training-data-address-us-ca.md`); this note covers what differs. The tool is `tools/data/AddressChallenger`; sources, licences and counts are in `tools/data/provenance/address-challenger-osm.md`.

**This data is a separate lineage.** It is ODbL data. It never feeds the default models: it lives under its own folder (`<data>/challenger/train/osm-lp-v1/`), every line carries `"set": "challenger-osm"`, and the manifest says `challenger`.

## Running it

```
tools/data/fetch-challenger.sh                                    # the file, checked against tools/data/pins/challenger-osm.pin
tools/data/run-challenger.sh <AddressChallenger dll> count render check verify
```

`count` makes one pass over the file and writes `selection.tsv`: for each country the lines in the file, the lines that convert, and a sampling rate (the target divided by the usable lines, at most 1; the default target is 250,000). `render` makes a second pass and keeps a line when `hash(line id, selection salt)` is below its country's rate, drops a line whose tagged text repeats one already kept in that country, converts it, and writes the train and dev files and `manifest.json`. A line's id is `osm:<country>:<line number in the file>`. Dev is `hash(id, split salt) mod 1000 < 20`, fixed from the id alone. The same input, seed (default 20261106) and selection give the same bytes; `verify` renders again and compares every file.

## The source file

`formatted_addresses_tagged.random.tsv.gz`: one address per line, `language TAB country TAB tokens`. A token is `word/label`; `|/FSEP` is a field separator and `,/SEP` a comma. The labels are libpostal's component labels (documented in libpostal's README). The file is in random order. It holds every OpenStreetMap address rendered with address-formatting templates, with abbreviations, dropped components and typing damage applied by its authors; some lines are only a city or a street. The language column is `unk` when no language was assigned.

## Lines left out

* A country outside the list: the United States and its territories (US PR GU VI AS MP), Canada, Mexico, Australia, the United Kingdom, Jersey, Guernsey and the Isle of Man, the Latin-script Tier 2 countries of the EU, Switzerland, Norway, Iceland, Liechtenstein, Andorra, Monaco, San Marino and the Vatican, and the French overseas departments (GP MQ GF RE YT). Greece, Bulgaria and Cyprus are outside the Latin-script tiers.
* A line with a `category`, `near`, `world_region`, `country_region` or `island` token: these are search-query words and informal regions with no counterpart among the address fields.
* A line with a word outside the Latin script (ASCII, Latin-1 to Latin Extended-B, Latin Extended Additional, combining marks, general punctuation and the numero sign), a word longer than 64 characters, more than 40 tokens, a text longer than 300 characters, or no field other than the country.
* Nothing is repaired: label noise in the source (a street tagged as a venue, a postcode glued to a city) stays.

## Writing the text

The source has tokens, not text, so the text is rebuilt:

* A field separator is written, per example (drawn from the id), as a line break (40 %, layout `osm.lines`), a comma and a space (45 %, `osm.comma`) or a space (15 %, `osm.space`). A `,/SEP` is a comma in the first two layouts and a space in the third. The source does not record which was used.
* Words are joined by one space, except: `.` `,` `;` `:` `)` `]` `!` `?` attach to the word before; `(` `[` and `#` attach to the word after; `/` joins letters or digits on both sides (`140/8`); `-` joins digits (`608-610`); `'` joins letters (`l'Église`); a quotation mark attaches inwards.
* The tokens of a set are our own (`tools/data/AddressUsCa/Tokenizer.cs`): letters, digits and combining marks form a token, every other character is a token, and tokens are cut at span edges.
* A span is a run of consecutive words with one source label and no separator between. Punctuation-only words at the edges of a run are left outside it, as is a final period or comma of the last word. Brackets and quotation marks stay in the span they belong to.

## Label mapping

| Source label | Our label | Note |
|---|---|---|
| `house` | `recipient` | A venue or building name. |
| `house_number` | `street.house-number` | |
| `road` | `street.road` | The whole street, type and name together. The source does not separate them; this set does not either. |
| `unit`, `level`, `staircase`, `entrance` | `street.unit-designator` + `street.unit-number` | Leading words without a digit are the designator (`Apt`, `#`, `Flat`) and the rest the number. A run that starts with a digit (`3rd Floor`, `6OG`), or has no digit at all (`Ground Floor`, `Rez-de-chaussée`), is all number. |
| `po_box` | `street.box-designator` + `street.box-number` | The same split; a run with no digit (`Postfach`) is all designator. |
| `postcode` | `postal-code` | |
| `suburb` | `dependent-locality` | An unofficial neighbourhood. |
| `city_district` | `dependent-locality.district` | A borough or district with an official role. Kept apart from `suburb` as the source keeps them apart. |
| `city` | `locality` | |
| `state` | `admin-area` | |
| `state_district` | `admin-area.district` | A county or second-level division (and the province in Italy and Spain). |
| `country` | `country` | |
| `category`, `near`, `world_region`, `country_region`, `island` | none | The line is left out. |

`street.road`, `dependent-locality.district` and `admin-area.district` are labels this set adds; a consumer that wants the register-based label set merges them (`street.road` to a street name and type, the two `.district` labels to their parents). `spans.fields` holds the `street-address` span from the first to the last `street.*` span when there is one.

## Known limits

* The file is from 2017-03-04 and reflects OpenStreetMap of that date and the conventions of its authors; later mapping is absent.
* Labels are the source authors', assigned when the file was built; they are noisy (see above). Evaluation on the real sets will show how much.
* A drawn layout is not a record of the original punctuation; connectors are joined by a fixed rule.
* Addresses in OpenStreetMap include venue names; some are names of businesses or people. They are public map data, kept only for training and not reproduced in any report.
