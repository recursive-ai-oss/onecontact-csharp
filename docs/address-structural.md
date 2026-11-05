# The structural address layer

What the library knows about the shape of an address in every country the address data describes: which fields exist, in what order they are written, which are required, what a postal code looks like, and which subdivisions a country lists. It is data plus a small engine. It never looks anything up: a well-formed address that does not exist passes.

## Source

The per-region address data of Google's libaddressinput, `testdata/countryinfo.txt`, at a pinned commit (see `upstream/libaddressinput/PIN`: repository, commit, SHA-256 of the file and of the licence). The file holds one `key={json}` line per node of a tree:

```
data                       the list of countries (property "countries")
data/US                    a country
data/US/CA                 a subdivision (administrative area)
data/BR/AC/Acrelândia      a locality; one level deeper is a dependent locality
data/CA--fr                the French variant of a country; subdivisions carry the same suffix (data/CA/AB--fr)
data/ZZ                    the defaults: what a country that gives no value of its own gets
examples/...               sample addresses for a few countries (not compiled)
```

Licence: Apache-2.0 (the repository's LICENSE). The repository README also says its data is available under CC BY 4.0; both are credited in the packed notice. Fetch with `tools/fetch/libaddressinput.sh`.

### Properties

All values are strings. Lists are separated by `~`, and the lists of one node are parallel (`sub_keys`, `sub_names`, `sub_lnames`, `sub_zips`, `sub_zipexs`, `sub_mores`, `sub_isoids`, `sub_lfnames`).

| Property | Meaning |
|---|---|
| `key`, `name`, `lname`, `lfname` | The node's key, its name in the local script, its Latin name, its Latin full name. The country `name` is English in capitals. |
| `lang`, `languages` | The main language, and the official languages (`~` separated). |
| `fmt`, `lfmt` | The address format in the local script, and in Latin script. Codes: `%N` recipient, `%O` organization, `%A` street lines, `%D` dependent locality, `%C` locality, `%S` administrative area, `%Z` postal code, `%X` sorting code, `%n` line break. Anything else is literal text. |
| `require` | Letters of the required fields (`ACSZ`). The country is always required. |
| `upper` | Letters of the fields the postal service writes in capitals. |
| `zip` | At country level a pattern every postal code matches. At subdivision level a pattern the start of the codes of that subdivision matches. |
| `xzip` | At subdivision level a pattern the whole code must match, replacing the country's pattern. |
| `zipex`, `sub_zipexs` | Example postal codes (`,` separates codes, `:` separates ranges). |
| `sub_keys` | The keys of the subdivisions below the node. |
| `state_name_type`, `zip_name_type`, `locality_name_type`, `sublocality_name_type` | What the country calls the administrative area (`state`, `province`, `prefecture`...), the postal code (`zip`, `postal`, `pin`), the locality and the dependent locality. Defaults: `province`, `postal`, `city`, `suburb`. |
| `posturl`, `postprefix`, `isoid`, `sub_xzips`, `sub_xrequires`, `xrequire` | Carried through; not used by the engine. |

## Compiled tables

`tools/Ingest.LibAddressInput` converts the pinned file into three tables of the compiled data file (container: `docs/architecture.md`). The same file always gives the same bytes. Version 1:

- `props`: the property names that occur, in ordinal order, joined by a line feed. A property is referred to by its position. A reader ignores names it does not know.
- `strings`: `u32 count`, `u32 offsets[count + 1]` (byte offsets into the data that follows, starting at 0), then the UTF-8 bytes. Every distinct value and node key appears once, in ordinal UTF-8 order.
- `nodes`: `u32 count`, `u32 offsets[count]` (from the end of the offset array), then one record per node in ordinal order of its key: `varint key-string, varint property-count, (varint property, varint value-string)*`. Varints are 7 bits per byte, low first, at most 5 bytes.

The reader checks every count and offset, that all strings are UTF-8, that every record parses and refers only to existing strings and properties, and that the keys are in order. Anything else raises `InvalidDataException`.

## Coverage

`Coverage` is `Full` for the supported tiers and `Structural` for every other country in the data. Free text is parsed only for `Full` countries; for the others `PostalAddress.TryParse(text, country, ...)` refuses with the reason `address.free-text-refused`. The tiers: US with PR, VI, GU, AS, MP and UM; CA; MX, AU; GB with JE, GG and IM; AT, BE, HR, CZ, DK, EE, FI, FR, DE, HU, IE, IT, LV, LT, LU, MT, NL, PL, PT, RO, SK, SI, ES, SE; CH, NO, IS, LI; AD, MC, SM, VA; and the French overseas departments GF, GP, MQ, RE and YT. Greece, Bulgaria and Cyprus are never in a tier.

## Validation

`PostalAddress.Validate()` returns at most one issue per field, in field order. The rules were settled from the data's documentation and by experiments with the reference implementation (below).

1. **White space.** A value is empty when it is made only of characters up to and including U+0020. Other white space (the no-break space, the ideographic space) is content. Values are trimmed of the same characters before they are compared.
2. **The format and the required fields** of a country are its own `fmt` and `require`, or the defaults' (`data/ZZ`) when it gives none. A country the data does not describe uses the defaults.
3. **The chain.** The country, the administrative area, the locality and the dependent locality are checked in that order, and checking stops at the first one that has a problem, because the levels below cannot be judged against a wrong level above.
   - Country: no country is `MissingRequiredField`; a country the data does not list (its `countries` list; `ZZ` is not a country) is `UnknownValue`. Either stops the chain.
   - A level with no value is `MissingRequiredField` when the country requires it, which stops the chain; otherwise the levels below have nothing to be looked up in.
   - A level with a value the country's format does not place is `UnexpectedField`, which stops the chain.
   - Otherwise, when the level above leads to a node that lists subdivisions, the value must be *known*: equal, ignoring case, to a key, a name or a Latin name in that node's lists; if not it is `UnknownValue` and the chain stops. When there is no list to check against any value is accepted.
   - A known value leads on to that subdivision's own node only when it equals a key exactly (case-sensitively), or equals a Latin name ignoring case. A value known by its local name only (`California` for `CA`, `강원` for `강원도`) is accepted but leads nowhere, so its own subdivisions and postal code prefix are not consulted.
4. **Sorting code, organization, recipient** are `UnexpectedField` when given and not placed by the format, and `MissingRequiredField` when empty and required. They do not depend on the chain. Street lines are `MissingRequiredField` when all are empty and `A` is required.
5. **Postal code**: not placed by the format and given: `UnexpectedField`; empty and required: `MissingRequiredField`; for a described country, not matching the whole-code pattern: `InvalidFormat`, where the whole-code pattern is the `xzip` of the most specific subdivision reached that has one, else the country's `zip`; and not matching the start-of-code pattern (`zip`) of the most specific subdivision reached that has one: `MismatchingValue`. A country the data does not describe has no pattern to check.

## Layout

`GetLabelLines` and `ToSingleLine` (lines joined with `, `) lay an address out in the country's `fmt`, or its `lfmt` for the Latin script (the language tag names Latin, or the caller asks). A country with no `lfmt` uses its `fmt`. The rules:

- Values are trimmed as above; a value empty after trimming is absent.
- A field with no value leaves out the literal text written just before it in the format.
- That literal text is also left out when nothing else has been written on the line yet, except for the first field of the line.
- A line made of literal text only is kept; a line whose fields all have no value is dropped.
- The street lines replace `%A` one per line (blank ones dropped). With more than one street line, whatever follows `%A` in the format starts a new line.

Upper-casing (`upper`) is information only (`AddressRegionInfo.UpperCaseFields`); the structural layer never changes the case of a value.

## Postal code patterns

The patterns are Java-style regular expressions in a small dialect: alternation `|`, groups `( )` and `(?: )`, classes `[...]` with ranges and `^`, `.`, the escapes `\d \D \w \W \s \S` and escaped punctuation, and the quantifiers `? * + {n} {n,} {n,m}` (greedy). Anchors, look-around, back-references, lazy and possessive quantifiers, named groups, `\p{..}` and nested classes are refused when a pattern is compiled. Matching is ignoring case, over a compiled program with a step budget and a recursion limit, so a hostile pattern cannot run long or overflow the stack. No `Regex` object is built from text at run time.

## Language variants

A language tag given with the address selects the data variant `data/CC--tag` where the data has one (`CA--fr`, `CH--fr`, `CH--it`, `HK--en`); the tag is kept in lower case with `-` separators by the public API. The engine matches the tag exactly as it is given.

## Experiments with the reference

The reference is the `common` module of libaddressinput built from the pinned commit (`tools/Oracles/Addresses/build-oracle.sh`) and driven through its public classes. Findings, each from crafted inputs (the reference was also given crafted variants of the data file):

1. The reference takes each country's `fmt`, `lfmt` and `require` from a table compiled into its jar (an older snapshot of the data), ignoring those entries in the data it is handed; it takes everything else (`zip`, `xzip`, `sub_keys`, `sub_names`, `sub_lnames`, the subdivision nodes) from the data. Crafting `fmt`/`require` of US, DE, FR and GB changed nothing; crafting `zip` and `sub_keys` of US and DE changed the verdicts.
2. Checking stops at the first problem among country, administrative area, locality and dependent locality (e.g. for a format without `%S`, a state and a dependent locality give only the state as unexpected).
3. A value is known by key, name or Latin name ignoring case; it leads on to a node by exact key or by Latin name ignoring case, never by local name (grid of 316 probes over the first four subdivisions of every country with subdivisions).
4. The postal code of a subdivision with `xzip` is checked against that pattern in place of the country's; the prefix `zip` used is the most specific one along the chain.
5. White space is the characters up to U+0020; a variant tag is looked up as written (`EN` finds no variant).
6. Layout: values trimmed; literal text dropped as above (11,152 field-subset probes over every format of its table matched the rules exactly); several street lines end the line.

## Where the library differs from the reference

These are intended and listed for approval:

1. The shipped data is the pinned file, whose `fmt`, `lfmt` and `require` are newer than the reference's built-in table (the two differ in the format of 66 countries and the required fields of 19). The parity test gives the library the pinned data with those three entries replaced by the reference's.
2. The Latin layout of a country with no `lfmt`: the reference uses its default layout (street lines and locality only); the library uses the country's own `fmt`.
3. A language tag with a Latin script subtag (`zh-Hans-Latn`) selects the Latin layout; the reference recognises only two-part tags such as `ja-Latn`.
4. The public API lower-cases language tags and turns `_` into `-` before they select a data variant.

## Parity test

`tools/Oracles/Addresses/make-reference.sh` regenerates `testdata/addresses-structural/`: the cases (from the pinned data and the reference's format table by `tools/Oracles/Addresses/CaseGen`, deterministic), the reference's answer to each, and a record of what produced them. `AddressStructuralParityTests` compares problems per field and label lines for every case.
