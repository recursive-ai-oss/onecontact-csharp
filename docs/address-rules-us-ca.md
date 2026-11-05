# Address rules for the United States and Canada

How a free-text United States or Canadian address is read, standardized and rendered. The code is `RuleAddressParser` (reading), `StreetLineParser` (one delivery line), `AddressRenderer` (the postal and display styles) and `AddressExpander` / `AddressKey` (expansion and key). The rules are those of USPS Publication 28 (October 2024) and the Canada Post Addressing Guidelines (updated 2021-06-14, English and French editions); the rule tables are plain tab-separated files under `src/OneContact/Addresses/Data/US` and `.../CA`, so a new country is new data, not new code.

## Reading

Input is one line (parts separated by commas, or by nothing) or several lines. The last line is found from the right:

1. A postal code: a ZIP or ZIP+4 (`22042`, `22042-1441`, nine digits get their hyphen), or a Canadian code (`A1A 1A1`, the letters D F I O Q U are never used and W and Z never start one).
2. A state, territory or province: a two-letter code or a name (English and French), up to four words. A two-letter code with no postal code beside it counts only when written in capitals.
3. The city: what is left of the line. When nothing is left the line above, if it is not a delivery line, is the city.

Without commas the delivery line and the city run together (`123 MAIN ST APT 4 SPRINGFIELD`). The rules end the delivery line after the first street suffix (the last of consecutive suffix words, Publication 28 section 234.2), a one- or two-letter directional, and any unit pairs. Where no suffix is found, or the street type comes before the name (French), the rules cannot tell and the parse returns `address.model-required`: a trained model would be needed. With commas or line breaks no model is needed.

**Country** comes from the content: a ZIP, a postal code, a state or province, an explicit country name (`USA`, `United States`, `Canada`). The optional hint is used only when the content is silent; a hint that disagrees with the content is reported as warning `address.country-conflict` and the content wins. A hint other than the United States or Canada is `address.unsupported-country`. With no content and no hint: `address.country-undetermined`. Confidence: 0.99 when postal code and state or province agree, 0.93 for a postal code alone, 0.85 for a state alone, 1 for a written country name, 0.9 for a hint.

**Lines above the delivery line** (recipient, organization, attention, `MSC` lines) are kept as written (`LeadingLines`), with a postal-style form that is upper case with periods and commas removed (Publication 28 section 222). They are never parsed as names.

**Nothing is invented.** No city from a postal code, no province from a forward sortation area, no `CALLE` or `APT` added, no unit designator guessed for a bare `#`.

## Delivery lines

| Kind | Rule |
|---|---|
| Street | house number, optional fraction, pre-directional, name, suffix, post-directional, units. Parsed from the right (Pub 28 sections 231 to 237): a trailing directional (when a name remains), the suffix (when a name remains before it), a directional between name and suffix is part of the name and spelled out (233.3), two directionals in a row make one (`N E` is `NE`) except north-south and east-west pairs, a suffix word immediately before the suffix is spelled out (234.2), hyphens in street names become spaces (232), hyphens in house numbers stay (appendix D). `COUNTY ROAD`, `STATE HIGHWAY` and similar are names (237). |
| Units | `APT`, `STE`, `UNIT`, `BLDG`, `FL`, `RM`, ... from Pub 28 appendix C2 (`#` followed by a space and the number, 213.2); Canada: `APT`, `SUITE`, `UNIT` (English), `APP`, `BUREAU`, `UNITÉ` (French); a Canadian unit before the civic number (`10-123`) is kept before it. A unit on its own line above the street line stays there (213.3); a unit on a line between the street line and the city joins the street line. |
| Box | `PO BOX n`; `CALLER`, `FIRM CALLER`, `BIN`, `LOCKBOX`, `DRAWER`, `APARTADO` become `PO BOX` (283, 293); a leading hyphen of the ZIP+4 file becomes a zero (282); `PMB n` stays (285). Canada: `PO BOX` or `CP` with station information (`STN`, `SUCC`, `RPO`, `COP`). |
| Rural route, highway contract | `RR n BOX m`, `HC n BOX m`; `RFD`, `RD`, `RURAL ROUTE` become `RR`, `STAR ROUTE` and `HIGHWAY CONTRACT ROUTE` become `HC`; leading zeros drop; `BOX` stands for `BZN` and `#`; extra words move to a line above (241 to 255). Canada: `RR n` with station information. |
| General delivery | `GENERAL DELIVERY` spelled out (261); Canada `GD` (French `PR`) with station information. |
| Military | `CPR`, `OPC`, `PSC`, `UPR` or `UNIT` with its number, `BOX` and a box number (238.1); last line `APO`, `FPO` or `DPO` with `AE`, `AP` or `AA`. |
| Canada, additional address | `SITE n COMP m` above the rural route identifier (4.2). |
| Puerto Rico | `URBANIZACION` and its spellings become `URB`, `CONDOMINIO` `COND`, `RESIDENCIAL` `RES`, `EDIFICIO` `EDIF` (292, 293); the number may come last or as bloque and number and is printed first (295, appendix I5); `NO` and `SO` after the street are directionals kept as written (294). |

Without a house number only the line just above the city is read as a street line, and only if it holds a street suffix: the guides print number-less examples (`BAY DR W`), but a name line higher up (`CASEY BROOKS`) is not an address.

Every part carries the rule that decided it (`pub28-233.22`, `canada-post-4.4-unit-number`, ...), its text as written, its standardized text and its offsets.

## Renderings

`Postal`: upper case, the standard abbreviations, one space between elements, no periods; last line `CITY ST  ZIP` with two spaces before the ZIP or postal code on a label (Pub 28 section 224 prefers two, Canada Post section 2 requires two) and one on a single line. Domestic addresses carry no country line (`includeCountry` adds `USA` or `CANADA`).

`Display`: the same tokens in natural title case: `123 N Main St Apt 4` and `Springfield, VA 22162`. Directionals, state and province codes and box and route words (`PO`, `RR`, `HC`, `GD`, `STN`) stay in capitals; ordinals take a lower-case ending (`5th`); a number followed by a letter (`123A`) keeps its capital. A leading line the caller wrote in mixed case is left as written. `McDonald`-style capitals are not reconstructed.

`PostalAddress.Parse` and `PostalAddress.TryParse(text, country, ...)` use these rules and fill the field model (country, state or province code, locality, postal code, delivery lines, recipient for lines above them); `StreetDetail` and `RuleDetail` give the street parts and offsets, `ToString(AddressStyle, AddressLayout)` the authority renderings, and `Validate()` the structural checks of the field model (ZIP and state, forward sortation area and province, required fields), which are not repeated here. An address made with `Create` is read again from its label lines for those members. The JSON form of an address with fields is an object holding the canonical form, the fields and the flags, and reads back equal.

The canonical form of a `PostalAddress` parsed from text is the `Postal` rendering on one line with `, ` between lines; reading it back gives the same canonical form and key.

## Field names and what the rules read with certainty

Field names: `street.house-number`, `street.fraction`, `street.pre-directional`, `street.name`, `street.type`, `street.post-directional`, `street.unit-designator`, `street.unit-number` (the `4` of `4-123 Main St` is a unit number with the hyphen outside it), `street.box-designator` and `street.box-number` (also after a military unit or a rural route: `PSC 3022 BOX 684`, `RR 4 BOX 932`), `street.route-*`, `street.station-*`, `street.military-*`, `street.general-delivery`, `locality`, `admin-area`, `postal-code`, `country`, and `leading-line` for lines kept as written. The words of one street name are one `street.name` field, and the final period of an abbreviation (`R.R.`, `U.S.A.`) is inside its field.

Forms read only when nothing else is possible:

- the street words that run on into the city with no comma (`72 Dewey St Richford, Vermont`): words left after the street's last word, when no other city is given;
- a unit designator with its number (or a single letter) at the start of the city words belongs to the delivery line above (`..., Unit H Altura, MN`);
- a French civic number followed by a comma and a street type (`4291, AV Christophe-Colomb`);
- words in front of a house number on the line above the city (two or more, or one when another line stands above): kept as a leading line when they hold no digit, street word or directional;
- the United States spelling of a street type in a Canadian address (`LN` is read as Lane and written as Canada Post writes it).

Forms refused with `address.model-required`: a suffix word right after the street's suffix that also starts city names (`1300 Riverside Dr Mount Vernon WA`, `500 Main St Fort Worth TX`), because rules cannot tell the street's end from the city's start.

## Reason codes (kind address, numbers 110 to 116; 100 and 101 belong to the structural layer)

`model-required`, `country-undetermined`, `unsupported-country`, `country-conflict` (warning), `no-delivery-line`, `no-last-line`, `unplaced-text` (warning).

## Where the two guides disagree

Listed so the choice is visible; the tests follow the rule stated first.

- Pub 28 section 213.2 asks for a space after `#` (`# 72`); the section 285 figures print `#234` and `#4545`. The code writes `# 234`.
- Pub 28 section 233.3 says a directional between name and suffix is spelled out (`BAY W DRIVE` is `BAY WEST DR`) but the figure of alphabet indicators prints `BAY E DR`, because the local unit knows `E` is a letter. The code spells out every directional (`BAY EAST DR`); `BAY A DR`, `B` and `C` are unaffected.
- Pub 28 section 295: the figure that moves the number first drops the word `CALLE` (`150 JULIO ENRIQUE VIZCARRONDO`), while the other figures of the same section keep it (`A17 CALLE 1`). The code keeps `CALLE`.
- Pub 28 section 292 prints `AVE WILSON CHURCHILL 123` as a sample address and section 295 calls a street name followed by a number incorrect. The code follows 295.
- Pub 28 section 295 prints `RR3 BOX 3415` as correct, against section 241 (`RR ## BOX ##`) and the figure above it (`RR03 BOX 9800` incorrect). The code follows 241.
- Pub 28 appendix A32 spells `1010 CLEAR STREET` out for a Canadian address; Canada Post section 4.4 abbreviates the street type. The code abbreviates.
- Canada Post lists Place as `PL` in the English edition and `PLACE` in the French edition; both are accepted, the English form is printed.
- The Canada Post English edition omits Northwest Territories from its province table; `NT` is added.
- Pub 28 figures show one space before the ZIP although section 224 prefers two; the tests ignore the width of a run of spaces.

## Examples that cannot be standardized by rules

- Pub 28 section 223: `W STOCKBRIDGE` and `NEWBERRY SPGS` are 13-character city abbreviations from the City State file.
- Pub 28 section 236: a corner address (`5TH AND HIGH`) is replaced by a physical address from reference data.
- Pub 28 section 284: `# 4567` to `UNIT 4567` needs to know the address is a post office street address.
- Pub 28 section 245 and 254 figures with a station line moved above a box line.
