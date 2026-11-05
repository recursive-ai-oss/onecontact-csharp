# Phones.Geo

Normative notes for `RecursiveAI.OneContact.Phones.Geo`: the geocoder, the carrier mapper and the time-zone mapper for phone numbers. The package references the core library only and carries its own compiled tables; the core library stays free of this data.

**Carrier caveat.** The carrier mapper names the carrier a number range was *first assigned to*. Where number portability exists a number can have moved to another carrier, and no offline table can know that. Use `GetSafeDisplayName` when a wrong carrier would mislead: it answers only for regions where portability does not apply. Never treat the carrier as the current one, and never as proof of who operates a line.

## Public API (`RecursiveAI.OneContact.Phones.Geo`)

| Type | Members |
|---|---|
| `PhoneGeocoder` | `GetDescription(number, languageTag)`, `GetDescription(number, languageTag, userRegion)` (checks validity), `GetDescriptionForValidNumber(...)` (two overloads, no validity check) |
| `PhoneCarrierMapper` | `GetName(number, languageTag)` (mobile, fixed-line-or-mobile and pager numbers only), `GetNameForValidNumber(number, languageTag)`, `GetSafeDisplayName(number, languageTag)` |
| `PhoneTimeZoneMapper` | `GetTimeZones(number)`, `GetTimeZonesForGeographicalNumber(number)`, `UnknownTimeZone` (`Etc/Unknown`) |

Each type has a `Default` instance on the core library's default metadata and a constructor taking a `PhoneNumberParser`. Language tags are BCP 47. Results never depend on the current culture. Nothing is fetched: every table is embedded and read on first use.

## Data and ingest

`tools/Ingest.LibPhoneNumberGeo` converts four pinned data sets into compiled table files (`OneContact.Data.<source>.ocd`), one converter per pin source. `tools/fetch/*.sh` fetch the files by exact path and check each SHA-256 against its `PIN`; the build fails on a missing or altered file, and the same input gives byte-identical output.

| Source (`upstream/…`) | Pin | Licence | Compiled size |
|---|---|---|---|
| `libphonenumber-geocoding` (`resources/geocoding/**`) | tag v9.0.43, commit 86ea33ce | Apache-2.0 | 2.6 MB |
| `libphonenumber-carrier` (`resources/carrier/**`) | same | Apache-2.0 | 162 KB |
| `libphonenumber-timezones` (`resources/timezones/map_data.txt`) | same | Apache-2.0 | 21 KB |
| `cldr-territories` (CLDR JSON 43.0.0 territory names) | c3349f36 | Unicode License v3 | 0.9 MB |

Notices are packed under `notices/` (Apache-2.0 licence and the compiled provenance; the Unicode licence for CLDR).

Prefix maps (geocoding, carrier) are tables named `g/<language>/<calling code>` and `c/<language>/<calling code>`. A table holds the sorted keys (digit prefixes, at most 32 digits) in blocks of 64 entries, each key delta-coded against its predecessor, with an index of block offsets and a pooled description list. A lookup binary-searches the block heads and scans one block, so only the tables a lookup touches are opened. The reader checks every offset and count and throws `InvalidDataException` for a truncated or inconsistent table; `Validate()` walks a whole map and also checks key order.

Country names for the "user in another country" geocoder variant come from CLDR 43 territory names for every locale CLDR has, delta-pruned against the parent locale. The reference runs on a JDK whose locale data is CLDR 43 (CLDR 42 differed, for example in the name of Türkiye), hence that pin.

## Behaviour

Settled by black-box experiments against the released jars (below), and held on generated cases.

- **Table language.** The requested language's own table answers by longest key that starts the national number. When it has no such key, or the entry is empty, the English table answers the same way. Chinese, Japanese and Korean never fall back to English. Chinese with region TW, HK or MO uses the traditional-script table when the calling code has one, otherwise the simplified one; other script subtags are ignored. Tags are BCP 47 only (`zh_TW` is not a tag, so it answers in English). The legacy codes `iw`, `in`, `ji` mean `he`, `id`, `yi`. A language with no tables (for example `mo`, `tl` for areas; any unknown tag) answers in English.
- **Empty entries.** In the compiled non-English tables an entry that equals the English one is empty with its key kept, so a longest key that lands on it falls back to English instead of to a shorter key of the same language. Ingest reproduces this.
- **Argentine mobile token.** Numbers of calling code 54 whose national number starts with 9 are looked up without that 9 in the geocoder and the carrier mapper. The time-zone map is unaffected.
- **NANP.** Calling code 1 is looked up only when the national number has exactly ten digits.
- **Geocoder.** A number of unknown type describes as empty. A place-bound number gets the prefix-lookup description, or, when nothing is found, the country name when the calling code or validity identifies exactly one region, else empty. A number that is not place-bound gets the country name by that rule. With a user region: the number's own region equal to the user's gives the full description; any other region gives that region's name in the requested language; an unknown region gives empty.
- **Carrier.** `GetName` answers only for mobile, fixed-line-or-mobile and pager numbers. `GetSafeDisplayName` answers empty where `PhoneNumberParser.IsMobileNumberPortableRegion` is true.
- **Time zones.** An unknown number type gives `Etc/Unknown`. A place-bound number takes the zones of the longest key that starts calling code plus national number; any other number takes the exact calling-code key; a missing key gives `Etc/Unknown`.
- **Country names.** The root names are the English names of the 249 ISO 3166-1 codes; `en` additionally has the extra codes (`AC`, `XK`, …). Locales the reference runtime lacks (35 of them, for example `tn`, `ss`) answer in English. One right-to-left override character in the Adlam name of Botswana is removed.

## Experiments

Each file under `tools/Oracles/PhonesGeo/probes/` is a crafted input list run through the jar driver (`GeoDriver.java`) and compared with our output; the conclusions are the behaviours above.

| File | Question | Conclusion |
|---|---|---|
| `e1-basics` | descriptions across languages and user regions | prefix lookup per language; foreign user gets the country name |
| `e2-fallback` | what a language without an entry returns | English table, except zh/ja/ko |
| `e3-zh` | Chinese variants for CN, TW, HK | region picks the traditional table |
| `e4-types` | description by number type | place-bound versus country-name rule |
| `e5-token`, `e6-tokens` | mobile tokens (AR, MX) | only AR 9 is skipped |
| `e7-locales` | which locales the runtime knows | CLDR 43 country names; 35 missing locales answer in English |
| `e8-valid` | calling codes shared by several regions | unique-valid-region rule |
| `e9-lengths` | NANP number lengths | ten digits only |
| `e10-ko-keys` | empty entries in non-English tables | key kept, text empty, falls back to English |

## Parity run

`tools/Oracles/PhonesGeo/run.sh <work dir> [seed] [prefixes per calling code]` generates cases, runs the jars (libphonenumber 9.0.43, geocoder 3.43, carrier 2.43, prefixmapper 3.43, Temurin 21.0.13) and our mappers, and compares nine fields per case: description with and without a user region, for-valid-number forms with and without a user region, carrier name, for-valid-number carrier name, safe carrier name, time zones, and geographical time zones. The cases are example numbers of every region in every ISO language and some odd tags, plus numbers from the data's own prefixes in every calling code, plus mobile and toll-free numbers per region in all 612 CLDR locales.
