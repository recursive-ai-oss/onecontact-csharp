# Address key, version 1

`PostalAddress.Key`, `GetKey(KeyVersion.V1)` and `AddressKey.Compute` give the key. A published version's output never changes: a rule change ships as a new `KeyVersion` with its own golden fixtures (`testdata/addresses/key-v1-golden.tsv` holds version 1's).

The key is built from the parsed parts, not from the text. Segments are joined with `|`:

1. the country, lower case (`us`, `ca`);
2. one segment per delivery line, in order (below);
3. the locality;
4. the state, territory or province code, lower case;
5. the postal code: a ZIP+4 keeps its five digits; a Canadian code loses its space, lower case.

Recipient, organization and attention lines never enter the key.

**Words** (street names and the locality) are folded: lower case with the invariant culture, diacritics removed from Latin letters by a table of our own (the platform's normalization support is not relied on; `ß` is `ss`, `æ` is `ae`, `œ` is `oe`), punctuation other than a hyphen between digits becomes a space, an ordinal loses its ending (`5th`, `1re`, `2e`, `2ème` become `5`, `1`, `2`), and a word the libpostal street-name and synonym dictionaries know in abbreviated form is replaced by its full word (`st` is `saint`, `mt` is `mount`, `ft` is `fort`).

**Delivery lines**: the house number (lower case, hyphens kept) and fraction; a directional as its full word (`n` is `north`, `o` is `ouest`); the street type as the dictionaries' full word (`st` is `street`, `boul` is `boulevard`); units: every numbered unit designator (`apt`, `ste`, `suite`, `unit`, `rm`, `dept`, `#`, `app`, `bureau`, `unité`) reads as `unit`, floors, buildings and the rest keep their dictionary word; a Canadian unit written before the civic number is written after the street as a unit, so `10-123 Main St` and `123 Main Street Apt 10` have one key; a post office box is `pobox n` (Canadian `cp n`) with the station words; routes and military lines keep their designator and numbers (`rr 4 box 87a`, `psc 3022 box 684`).

Examples:

| Address | Key |
|---|---|
| 123 North Main Street Apartment 4, Springfield, Virginia 22162-1234 | `us|123 north main street unit 4|springfield|va|22162` |
| 10-123 Main St, Montreal QC H3Z 2Y7 | `ca|123 main street unit 10|montreal|qc|h3z2y7` |
| 12 Mt Vernon Rd, St Louis MO 63101 | `us|12 mount vernon road|saint louis|mo|63101` |
| PSC 3022 Box 684 / FPO AE 09397-0007 | `us|psc 3022 box 684|fpo|ae|09397` |

Two addresses that differ only in the unit designator word (`Apt 4` and `Ste 4`) share a key on purpose: the key is for finding duplicates. Different house numbers, different postal codes or different units never share one.

## Addresses without a country

An address read with the country left unknown (`AddressFacts.CountryUndetermined`, see `address-country-undetermined.md`) has the key `zz|<street words>|<locality words>|<admin area>|<postal code>`:

- `zz` stands where the country code stands;
- the street words are the text of the address's `street.*` fields in input order, folded and with the dictionaries' abbreviations replaced as for any other address (the language of the dictionaries is not known, so the default English-and-French set is used);
- the locality is folded the same way; the admin area is folded; the postal code keeps only its letters and digits, folded.

No country-specific rule applies (no ZIP+4 truncation, no Canadian unit reordering, no particle rules). The same address read with a hint or with a country in the text has a different key by design: `zz|...` and `be|...` are never equal, so an address without a country never matches one with a country by accident.

## Countries described only by their fields

An address in a country the rules do not read (the structurally described countries, `Coverage` is `Structural`) has the key `<country>|<street words>|<dependent locality>|<locality>|<admin area>|<postal code>`: the country code in lower case, the street lines folded and joined with spaces, then the other fields in this fixed order whatever the country's own label layout is, each folded as above, the postal code keeping only letters and digits. Before this rule the key had no country segment and followed the layout, so equal fields in two countries shared a key; that was a defect, not a version, because no key version had been published.
