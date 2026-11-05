# Person names

Rules only. `PersonName`, `NameParsing.Classify/Split/SplitMany` and `NameHints` read a string, give every part with offsets into the
input (UTF-16 chars), and say in `ParseDetail` which issues were met. Nothing is inferred: no gender, origin or salutation, and no part
is added that the text did not contain. `CultureInfo` never affects a result; comparison is ordinal and case folding is our own invariant folding.

## Hints

`NameHints(Region? country, string? language)` is optional. A hint never overrules the text; it chooses between readings the text leaves open:

| Behaviour | Without a hint | With a hint |
|---|---|---|
| Family name first ("Kovács János") | read given-first | read family-first for `hu` |
| Two family names ("Ana García López") | one family name, the rest given/middle | two family names for es, ca, eu, gl, pt |
| Lower-case particles after casing restoration | "Van Der Berg" | "van der Berg" for a non-English language (nl, de, fr, es, pt, it, ...) |
| Title words that are also names (`don`, `g`, `p`, `hr`, `me`) | not titles | titles in the languages that use them |
| Legal-form candidates (companies) | all jurisdictions | the hinted country's first |

## Classes

`NameClass` is `Person`, `Company`, `Multiple` or `Unknown`.

- **Company**: a legal form of the ELF list (docs/company-names.md), a company word (Ltd-like words, "Holdings", "Associates", ...), a
  trading-as or care-of marker, or a comma part that is itself a company ("Kvarner Promet d.d., Jansen"). A company word after a given name
  that is also a surname ("Jane Baker") does not make a company.
- **Multiple**: connectors between name parts (`and`, `&`, `et`, `und`, `en`, `e`, `og`, `och`, `ja`, `i`, ...), semicolons and line breaks, and comma lists
  ending in a connector. "Mr & Mrs John Smith": a title-only part takes the whole name that follows. "John and Jane Smith": a lone word before a
  full name is a given name and shares the family name. "Maria Garcia y Lopez": `y`/`e`/`i` between two family names is one person (the right
  side is one word, with particles). Two single words joined by a connector are `Multiple` only when the first is a known given name
  ("Johnson & Johnson" is `Unknown`). Every part must also read as a person when parsed again alone, otherwise the whole text is `Unknown`.
- **Person**: everything else with 2 to 8 words. When nothing proves a person (no title, suffix, nickname, particle, comma order, initial or
  known given name) the result carries the info issue `person.no-evidence`.
- **Unknown**: one bare word, titles and suffixes alone (`person.no-name`), digits or `@`, more than 8 words, more than 8 people, or text that reads as a company once put in reading order
  (`person.not-a-person`). `PersonName.Parse` still accepts one bare word (flag `SingleWord`).

## Parts

Prefix (titles), Given, Middle, Nickname, Particle, Family, SecondFamily, Suffix (generational), ProfessionalSuffix, Order.

- Titles are read from the start of each comma segment: academic, military, religious, honorifics, up to three words, dotted runs such as
  `Prof.Dr.`. A stray dash or ampersand before the titles is skipped. Titles are scoped by language so that words that are names elsewhere are not stripped.
- Suffixes are peeled from the end: generational (`Jr`, `Sr`, `II`, `III`), professional (`PhD`, `MD`, `Esq`, ...; a bare one needs a dot or its own comma segment).
- Nicknames: quoted (`"Bob"`, curly and guillemet quotes) or parenthesized. A quote mark opens one only at the start of a word and closes it only at the end of one
  (`O'Brien`, `l'Heureux` are not nicknames); a quoted run that holds a comma is a stray mark and is ignored.
- Order: "Family, Given Middle" (trailing particles after the given names move to the family name: "Berg, Jan van den"), given-first, family-first for `hu`.
- Particles (`van`, `van der`, `de`, `de la`, `von`, `da`, `dos`, `di`, `le`, ...) stay in `Particle`, apart from `Family`.
- Spanish-style second family names with a hint; a three-word name whose middle word is the second half of a compound given name ("Ana Maria Garcia") keeps one family name.

## Canonical form

Always given-first reading order: "Smith, John" gives `John Smith`, and Hungarian "Kovács János" gives `János Kovács`; `Order` remembers the input order. This keeps the JSON
round trip stable. Nicknames are written `"Nick"`, professional suffixes after a comma (`, PhD`), generational suffixes inline. Mixed-case input is kept as written.
A reparse of a family-first canonical form with the `hu` hint is not idempotent, by design.

## Casing restoration

Applied only when the whole input is one case (at least two letters, all upper or all lower). Each restored part says so with the info issue `person.casing-restored`, and keeps the offsets of the original.

- Capital first letter of each word; hyphen and apostrophe parts too (`O'BRIEN` gives `O'Brien`, `ANNE-MARIE` gives `Anne-Marie`).
- `Mc` followed by a capital (`MCDONALD` gives `McDonald`). `Mac` is not restored: `MACDONALD` gives `Macdonald`, because `Mac` prefixes are too often not a prefix (`MACHADO`).
- Particles are lower case only with a non-English hint (not Irish): `VAN DER BERG` gives `Van Der Berg` without a hint and `van der Berg` with `nl`.

## Person key V1

See docs/name-keys.md.

## Black-box comparison

`tools/Oracles/Names` runs, from released packages and never reading them: python-nameparser 2.8.2 (used through `HumanName`, as its documentation describes), probablepeople 0.5.6,
cleanco 2.3 (Python, in a `uv` venv) and NameParserSharp 1.5.0 (NuGet, in the oracle project only), over `testdata/names/gold.tsv`: 522 synthetic person and company strings with gold parts, 28 languages, made by `make-gold.py` with seed 20261105.
`score.py` scores token accuracy (each gold part's words get the reference's label), class accuracy and legal-form detection. The gold set is made from the same kind of templates our
rules were written against, so our scores are near 100% by construction. The comparison is information about the references' coverage of these languages, not a quality claim for real-world text.
