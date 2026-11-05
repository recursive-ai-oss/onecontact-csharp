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
