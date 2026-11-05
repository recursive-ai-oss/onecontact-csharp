# Person key

`PersonKey.Compute(PersonName, KeyVersion)` and `PersonName.Key` give the aggressive, versioned comparison key. Canonical forms stay faithful; only the key
loses information. A published version's output never changes: a rule change ships as a new `KeyVersion` with its own golden fixtures
(`NameRuleTests`).

## V1

For a name, in this order, joined by single spaces:

1. **Given**: the given name folded (below), then replaced by its formal name when it is a listed nickname ("bob" gives "robert", "liz" gives "elizabeth").
2. **Middle**: the first letter of each folded middle word.
3. **Family**: particle, family name and second family name folded and concatenated without spaces ("van der Berg" gives `vanderberg`).

Dropped: titles, generational and professional suffixes, the nickname part, the family connector (`y`).

**Folding**: invariant lower case, then a fixed table maps Latin letters with marks to their base letter (`é` to `e`, `ø` to `o`, `ł` to `l`, `đ` to `d`, ...), `ß` to `ss`, `æ` to `ae`, `œ` to `oe`, `þ` to `th`, `ð` to `d`; combining marks (U+0300 to U+036F) vanish, so decomposed text folds like precomposed text; every other character that is not a letter or digit (apostrophes, hyphens, dots) is removed. No Unicode normalization tables are involved.

**Nicknames**: our own list (`NameLexicon`), written for this repository; no external list is used. Only unambiguous pairs are in it. Short forms that are also names or words of their own
(Al, Sam, Chris, Pat, ...) are left out, and so are equivalences across languages (José is not Joseph).

Never inferred: gender, origin, language. Golden examples (fixtures in `NameRuleTests`): `Bob Smith` and `Dr. Robert "Bobby" Smith Jr., PhD` both give `robert smith`; `Robert James Smith` gives `robert j smith`; `Liz O'Brien` gives `elizabeth obrien`; `Anna van der Berg` gives `anna vanderberg`;
`José García López` with the hint `ES` gives `jose garcialopez`.
