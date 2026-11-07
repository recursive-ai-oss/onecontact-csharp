# Last-line orders on web pages

The authorities' sheets print the postcode and the locality in one order per country (`docs/address-rules-tier2-*.md`). Web pages write other orders. The rule reader
accepts them when the country is known (a hint or a country name in the text), rearranges the lines in memory so the last one reads *postcode, locality* as for every other
country, and leaves every span pointing at the words in the input. The rendered `Postal` and `Display` forms are the authority's, as before; nothing is added to the
address, and nothing in the text is rewritten.

| Form | Example | Countries | Source of the order |
|---|---|---|---|
| Postcode and locality open the address, the street follows | `00-590 Warszawa, ul. Marszałkowska 10` | every Tier 2 country except Hungary, Romania and Australia, which have their own readers | Poczta Polska, *Zasady poprawnego adresowania* (postcode before locality); the same order is the usual one in the other languages of the postcode-first countries |
| Locality first, street, postcode last | `Warszawa, ul. Złota 59, 00-120` | one-line text only | usage; written in lines, a name line above the street is a recipient, so it is not read this way |
| Street, locality, postcode apart | `Fő utca 12., Debrecen, 4024` | all | usage |
| Locality and postcode at the end of a line, a street elsewhere | `ul. Kościuszki 12, Gdynia 81-001`; `Kraków 31-147, ul. Długa 5` | all | usage |
| Country name between the post town and the postcode | `Belfast, UK BT1 1GH`; `Guildford, Surrey, England GU1 3AB` | GB, IE | UPU United Kingdom and Ireland sheets print the country on its own line; written inline it is still the country |
| Australian state in full, with the country after | `Melbourne, Victoria 3000, Australia` | AU | Australia Post, *Address Presentation Standards* (the state is an abbreviation or the full name) |
| Spanish street known by its name alone | `Gran Vía 25, 28013 Madrid` | ES | the name is the registered street name; there is no type word to read |

Limits that keep these readings safe:

- The country must be known. In country inference (no hint) a trial read takes these orders only for a postcode form that one country alone writes (`00-590`), because four
  or five digits at the start of a line are as often a house number.
- The city-states (Vatican, San Marino, Monaco) print their own name where the locality would be, so their postcode alone is never preceded by a locality.
- The locality is one to four words without digits that do not open a place inside a building and are not a postal-code word (`C.P.`, `PLZ`, `CAP`, ...).
- A building, estate or park name above the post town (`Westside Business Park`) is a line of the address, never the post town, so `Galway H91 X2Y3` keeps Galway.
- The Scottish council areas `Aberdeen City`, `Dundee City` and `Glasgow City` are counties of the line above the postcode.

Not read, left to the model: a voivodeship tail (`31-147 Kraków, woj. małopolskie`), a bilingual locality (`Borriana / Burriana`), dependent-locality lines of the Czech
Republic, Croatia and Romania (the real-page gold leaves them unlabelled, so reading them as fields would only add predictions).

## Labels, brackets and island names

- **Address labels.** A page may write `Cím: 1053 Budapest, ...`, `Adres: ul. ...` or `Sitz: ...`. The label says what the line is and is no part of the address: it is left out of every field and of the span of the address. A listed label (`Data/T2/t2-address-labels.tsv`: the ordinary words for "address", "registered office", "premises" and "postal address" in Hungarian, Polish, Czech, Slovak, English, German, French, Spanish, Portuguese and Italian, written by us) is dropped at the start of any line when a colon follows it; at the start of the text any single word ending in a colon is dropped. A colon elsewhere stays.
- **Brackets.** Round or square brackets around the whole address are dropped (offsets stay those of the original text).
- **Island names.** For a hinted Jersey, Guernsey or Isle of Man address: the island's name written right after the town without a comma (`Douglas Isle of Man IM1 1AA`) is the country and the words before it the locality; a town line followed by the island's name and the postcode with no street line (`Ballasalla, Isle of Man, IM9 2AA`) has the town as locality; a trailing `United Kingdom` or `UK` after the island's postcode is kept as the written country text (never rewritten), the island stays the country, and the two names are reported as a `country-conflict` warning. Without the hint the island's postcode still cannot be called Britain's and the text is refused.

## Scoring conventions

The scorer's `house_number` for a building and flat number written `12/3` is the whole `12/3` (the gold sets and the reference systems write it so). The library keeps a house number (`12`) and a unit number (`3`); `tools/Eval/Rules` joins the two for scoring when only a slash separates them (set `ONECONTACT_EVAL_NO_SLASH_CONVENTION` to score without the join). On the Polish set the join moves hinted field F1 from 89.96 to 96.39; it is a label convention, not an improvement of the reading.
