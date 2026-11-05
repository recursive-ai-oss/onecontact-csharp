# Address rules: France and its overseas departments, Monaco, Belgium, the Netherlands, Luxembourg, Germany, Austria, Switzerland, Liechtenstein

How the rule parser (`Addresses/Tier2`) reads and standardizes these addresses. The rules are data (`Addresses/Data/T2/*.tsv`) plus one reader; adding a country of this kind means adding rows. The sources are the postal authorities' free public guides, listed at the end. Paid standards (NF Z 10-011 itself, DIN 5008) were not bought or read; where La Poste's guides cite a standard, only what the guide says is used.

## What the rules decide

1. **Lines.** Several lines, or one line with commas (or a lone `/`, `|`, `-` between words) in place of line breaks. A trailing country name is a field of its own.
2. **Last line.** The last postcode that has a locality after it ends the address: five digits (FR, MC, DE), four (BE, AT, CH, LI, LU), four digits and two capital letters (NL). A country letter before the postcode (`L-1234`, `CH-3000`, `FL-9490`, `D-26133`, `F-75008`, `B-1000`, `A-1010`, `MC-98000`) names the country. Words before the postcode on the same line are a line of their own. Switzerland and Liechtenstein: a canton abbreviation after the locality is the admin area (UPU Switzerland sheet).
3. **Country.** From the country name, the letter prefix, a hint, a postcode that fits one country (Dutch form, `97xxx` France, `980xx` Monaco), or language cues for the shared forms (CEDEX, BP, French street types for France; Postfach, Straße, Weg for Germany; a canton abbreviation for Switzerland). Otherwise `address.country-undetermined`; a hint the postcode cannot belong to is `address.country-conflict`.
4. **Delivery lines.** From the first line that is a box line or a street line to the line above the last. Lines above it are kept as written (recipient, organization, building, floor). A line that starts with a place-in-building word (`Appartement`, `Bâtiment`, `Gebouw`, `Verdieping`, `Chez`, …) is never the street line. Lines after the street line are further delivery lines (a box, a village, a lieu-dit). A bulk receiver has no delivery line: name lines without digits and the postcode with its locality are the whole address (UPU Luxembourg and Germany sheets); this carries a warning `address.no-delivery-line`.
5. **Everything else** is refused with `address.model-required`. Nothing is invented: no locality from a postcode, no street from a name.

## Postal and Display

`Postal` is the authority's style, `Display` the same tokens in natural title case. Both are built from the same decisions.

| Country | Postal | Source |
|---|---|---|
| FR, MC, GP, MQ, GF, RE, YT | Every line in capitals without accents or punctuation (hyphen and apostrophe become a space); six lines, 38 characters at most; a street type is written in full and abbreviated (`AV`, `BD`, `RTE`, `ZI`, …) only when the line is longer than 38; SAINT and SAINTE of the locality line and of the line above it become ST and STE; CEDEX and its digits stay; BP, CS, TSA lines kept. | La Poste, Guide pratique courrier 2026 pp. 18-21; SP 8855 volume 2 §3.1, §3.3; UPU France sheet |
| BE | Lines as written; locality in capitals; one space between postcode and locality; a house number directly after the street name, `21C`, `21/3`, `4-8`; box number after the literal `bus`, `bte` or `box` (`b`, `Bt`, `#`, `-`, `/` are rewritten, the language of the street name decides: `bte` for French types, `bus` for Dutch endings); `n°`, `nr` dropped; Postbus numbers without spaces; type abbreviated only when the line is longer than 50 (`Av.`, `Bd`, `str.`, `ln`, `stwg`, …). | bpost Belgian template §3-4; bpost MassPost guide 2025 §1 pp. 11-13; UPU Belgium sheet |
| NL | Lines as written; postcode `1234 AB`, two spaces, locality in capitals. | UPU Netherlands sheet; PostNL's page on addressing foreign mail says the same (read as search-result text; the PDF could not be downloaded) |
| LU | Lines as written; postcode printed `L-1234`; locality in capitals. | UPU Luxembourg sheet |
| DE | Lines as written; five-digit postcode without country letter; locality in capitals; `Postfach` numbers in the groups the sender wrote; `Postlagernd`, `Packstation 101` lines kept; apartment after `//`. | UPU Germany sheet |
| AT, CH, LI | Lines as written; locality in capitals; box lines `Postfach`, `Case postale`, `Casella postale`. | UPU Austria and Switzerland sheets (Liechtenstein: Swiss Post serves it, the Switzerland sheet applies) |

Display writes an all-capital word in title case (particles `de`, `du`, `des`, `van`, `von`, `am`, `im` in lower case, `L'` and `D'` elided), keeps a word that has any lower-case letter as the writer wrote it, keeps CEDEX, BP, CS, TSA in capitals, and never restores accents the input lacks. The country line (English, in capitals for Postal) is printed only when asked.

## Where the guides disagree or stop short

- La Poste asks for capitals without accents "dès la ligne 1" as a recommendation; its own examples (UPU France sheet) show lines 1-3 in mixed case. `Postal` follows the recommendation for all lines; the tests compare line 1-3 of the examples in that form.
- The UPU Belgium sheet and the bpost template print the municipality in mixed case (`1000 Bruxelles`); the bpost MassPost guide 2025 (p. 12) asks for municipalities in capitals. `Postal` follows the MassPost guide; `Display` keeps the mixed case.
- The MassPost guide abbreviates `Air Marshall Conninghamlaan 432` to `Conninghamln` as an example while stating that abbreviations are for lines over 50 characters; the example line is 31 characters. The code follows the stated rule.
- The UPU France sheet abbreviates IMPASSE as `MP` in its type table; La Poste's SP 8855 gives `IMP`. The code uses `IMP`.
- The Swiss sheet prints `Solothurnstrasse` in the diagram and `Solothurnerstrasse` in the example; the example is used.
- The UPU Germany sheet says to write P.O. box numbers in two-digit groups from the last number (`Postfach 3 41 41`); the code keeps the groups the sender wrote.
- The returned-address form of La Poste's guide (p. 22, lines 4 and 6 on one line separated by three spaces) is not read.

## Not covered by rules (these go to the model)

Addresses without a postcode; a name and a street on one line; a number glued to the street word (`5Route`); `Haus G`-style additions after the number; `R` for `rue`; Austrian and Swiss addresses with several local-part conventions beyond the sheets.

## Structural checks

`PostalAddress.Validate` uses the structural layer's postcode patterns for each country. The reader adds the postcode ↔ region rules the sheets state: a postcode starting `97` is French (overseas departments are part of La Poste's network), `980` is Monaco. It never looks anything up.

## Keys and expansion

Keys use the same segments as the United States and Canada (country, one segment per delivery line, locality, admin area, postcode) with words folded and the country's languages' dictionaries applied: `Hauptstr` is `hauptstrasse`, `av` is `avenue`, and the box literals `bus`, `bte`, `box` are one word. Existing key outputs are unchanged. `AddressExpander` takes `German`, `Dutch`, `Italian` and `Luxembourgish` besides `English` and `French`; German and Dutch names also expand an abbreviated ending (`Hauptstr` to `Hauptstrasse`) from libpostal's concatenated-suffix lists, keeping only abbreviations of three letters or more.

## Sources

La Poste: Guide pratique courrier, January 2026 (laposte.fr); Documentation technique système client presse, Adressage SP 8855, volume 2, version 1.9. bpost: Documentation_Belgian_Template.pdf; MassPost guide January 2025, section 1. UPU country sheets (addressing unit, upu.int): France 09/2011, Belgium 09/2012, Netherlands 05/2016, Luxembourg 06/2013, Germany 01/2015, Austria 04/2014, Switzerland 06/2024, Liechtenstein 07/2002, Monaco 05/2004. Deutsche Post: Leitfaden Automationsfähige Briefsendungen 2024, section 2.2-2.4 (pp. 17-19). PostNL: the web page Adressering van buitenlandse zendingen (snippet only). Facts and short examples only, cited by page; the example addresses used as tests are in `testdata/addresses/authority-examples-tier2a.tsv`.
