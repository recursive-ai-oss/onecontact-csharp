# Addresses whose country cannot be told

Many postcodes are written alike in several countries: four digits in Belgium, Austria, Switzerland, Liechtenstein, Luxembourg, Denmark, Norway, Slovenia and Hungary; five in France, Germany, Spain, Italy, Croatia, Mexico, Finland, Estonia, Lithuania and Sweden. When a text has such a postcode, no country hint, and nothing in it that belongs to one country only (country inference, `address-rules-tier2-c-north.md`), the country cannot be told. Before this rule the text was refused with `address.country-undetermined`. A caller reading web pages usually needs the fields all the same: the street, the number, the locality and the postcode are the same whichever country it is.

## Rule

When the United States and Canada rules could not read the text and the country-deciding path refused it, the text is read once as each Tier 2 country that can read it (the same trial reads as country inference). Candidates are dropped for the reasons that already veto an inference (a letter outside the language, a Swedish-form postcode written without its space, a province code left in the locality). If at least one candidate is left and **every remaining candidate finds the same fields**, those fields are returned and the country stays unknown. If two candidates differ, the text is refused as before. Evidence for exactly one country that the inference turned down (a conflict, not an absence) also keeps the refusal.

"The same fields" means the same set of (field, offsets) after joining the parts of a road: the house number; the unit parts; the road (street name, type and directionals as one stretch); the locality, admin area, postal code, country name and the lines kept as written. Two countries that split a road into name and type differently still agree on the road.

The country-undetermined read happens last: the United States and Canada rules, the Tier 2 rules with a hint or with evidence, and the model read all come first, and a hint always decides.

## What the caller gets

| Member | Value |
|---|---|
| `ParsedAddress.Country` / `PostalAddress.Country` | `Region.Unknown` |
| `ParsedAddress.CountryConfidence` | `0` |
| `AddressFacts.CountryUndetermined` (new, value 128) | set, in `PostalAddress.Flags` and `ParsedAddress.Facts` |
| `ParseDetail.Issues` | a warning with the existing reason `address.country-undetermined` (no new reason code) |
| `PostalAddress.Validate()` | `Country` missing (the structural validator already reports a missing country and stops there) |
| `PostalAddress.Coverage` | `Structural` (the default for no country) |
| `ToString(style, layout, includeCountry)`, `Canonical` | the text as written: the lines of a multi-line input, or the comma-, semicolon- and bar-separated parts of a one-line input, trimmed, joined with a line feed or `, `. Postal and Display are the same; no country line is added. No authority's style applies, so nothing is rewritten |
| `Key` | `zz|...` (`address-keys.md`) |
| Fields | the first candidate's field names and offsets; each field's standardized text is the text as written. The street parts (`StreetDetail`) are the first candidate's reading of the delivery lines |
| JSON | as for any parsed address (the text is stored and read again); the flag survives |

With a model package referenced, the model is not asked for these texts: its reading would have to name a country, and it never decides one.

## Measurement

The 2,000-line register dev renders per country (`tools/Eval/Tier2Recovery`, without a hint), every-field share and refusals before and after: Austria 20.7 to 80.8 percent (1,487 to 165 refusals), Switzerland 21.1 to 61.0, Denmark 60.9 to 89.9, Norway 65.0 to 89.2, Slovenia 64.6 to 85.5, Luxembourg 54.8 to 79.4, Croatia 26.8 to 50.5, Slovakia 50.0 to 71.7, France 52.3 to 68.3, Germany 59.9 to 72.3, Hungary 79.1 to 89.7, Italy 59.1 to 67.4. No country with a hint changes and the wrong-country counts do not rise.
