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
