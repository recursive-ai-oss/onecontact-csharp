# Honest-refusal rows

The rules-only packages promise two things about free-text addresses: they never read a text in the wrong country, and they never print a value the text does not contain. Text they cannot read is refused with a reason. This note says how that is measured and what the measurement found. The evaluation sets are private; only the counts below are published.

## Method

`tools/Eval/Refusal` reads every item of every frozen address evaluation set with the rules alone (no model package is referenced) and puts the result in one outcome. Each item is read twice: with its country as the hint, and without a hint.

- **Refused**: no reading was returned. The reason id is counted.
- **Read, no country**: the fields were returned and the country left unknown, with the `CountryUndetermined` flag (the low-level form; `PostalAddress.Parse` refuses these).
- **Read, right country**: the country the reader settled on is the item's. France's overseas departments (GP, MQ, GF, RE, YT) count as France, because La Poste serves them as one territory.
- **Read, wrong country**: the reader settled on another country.
- **Invented**: a reading that prints something the input does not contain. Two checks:
  1. every field's text must equal the input at the field's offsets (the United States and Canada readers store the standardized box designator instead; its words go through the second check);
  2. every word of the postal and the display rendering must trace to the input. A word traces when the input has it (ignoring case and accents; letters and digits are separate words, and a dot between single letters joins them: `P.O.` is `PO`), or it abbreviates an input word (its letters are in that word in order, starting with the same letter: `BLVD`, `ST`), or an input word abbreviates it, or it abbreviates the next few words together (`CDMX`), or it is a number regrouped (`950 01` for `95001`), or it is the initials of one-letter input words. A country mark in front of a postcode (`HR-10000`, `L-8805`) is the authority's postcode form and is not counted.

The sets are the frozen real-page sets of the United States and Canada, of every Tier 2 country, and the fresh sets drawn for the 1.0 gate. A country can appear in two sets (an older one and a fresh one); the rows are kept apart. Sets are small (6 to 231 items), so a single item moves a row by 1 to 17 points.

Only counts leave the machine that holds the sets. For a wrong-country or invented reading the item's own text and fields were looked at to find the cause; nothing from them is quoted here.

Run it with `tools/Eval/Refusal/run.sh <output directory>` and `tools/Eval/Refusal/table.py`.

## Results

Rules only, `RecursiveAI.OneContact` as released in 1.0.

| Set / country | Items | Hinted: refused | read, right | read, wrong country | invented | No hint: refused | read, no country | read, right | read, wrong country | invented |
|---|---|---|---|---|---|---|---|---|---|---|
| tier1/CA | 231 | 6 | 225 | 0 | 0 | 6 | 0 | 225 | 0 | 0 |
| tier1/US | 229 | 8 | 221 | 0 | 0 | 8 | 0 | 221 | 0 | 0 |
| group-a/AT | 90 | 8 | 82 | 0 | 0 | 4 | 55 | 31 | 0 | 0 |
| group-a/BE | 95 | 9 | 86 | 0 | 0 | 6 | 34 | 55 | 0 | 0 |
| group-a/CH | 99 | 8 | 91 | 0 | 0 | 6 | 59 | 34 | 0 | 0 |
| group-a/DE | 117 | 12 | 105 | 0 | 0 | 12 | 28 | 77 | 0 | 0 |
| group-a/FR | 101 | 9 | 92 | 0 | 0 | 11 | 8 | 82 | 0 | 0 |
| group-a/LI | 117 | 11 | 106 | 0 | 0 | 11 | 2 | 104 | 0 | 0 |
| group-a/LU | 92 | 4 | 88 | 0 | 0 | 5 | 9 | 78 | 0 | 0 |
| group-a/MC | 38 | 3 | 35 | 0 | 0 | 3 | 0 | 35 | 0 | 0 |
| group-a/NL | 117 | 5 | 112 | 0 | 0 | 5 | 0 | 112 | 0 | 0 |
| group-b/AD | 46 | 8 | 38 | 0 | 0 | 8 | 0 | 38 | 0 | 0 |
| group-b/ES | 69 | 15 | 54 | 0 | 0 | 8 | 32 | 29 | 0 | 0 |
| group-b/GB | 142 | 33 | 109 | 0 | 0 | 33 | 0 | 109 | 0 | 0 |
| group-b/GG | 24 | 6 | 18 | 0 | 0 | 6 | 0 | 18 | 0 | 0 |
| group-b/IE | 92 | 42 | 50 | 0 | 0 | 44 | 0 | 48 | 0 | 0 |
| group-b/IM | 59 | 9 | 50 | 0 | 0 | 12 | 0 | 47 | 0 | 0 |
| group-b/IT | 60 | 4 | 56 | 0 | 0 | 6 | 19 | 35 | 0 | 0 |
| group-b/JE | 51 | 3 | 48 | 0 | 0 | 3 | 0 | 48 | 0 | 0 |
| group-b/MT | 49 | 16 | 33 | 0 | 0 | 14 | 2 | 33 | 0 | 0 |
| group-b/PT | 55 | 7 | 48 | 0 | 0 | 7 | 0 | 48 | 0 | 0 |
| group-b/SM | 61 | 13 | 48 | 0 | 0 | 13 | 0 | 48 | 0 | 0 |
| group-b/VA | 6 | 1 | 5 | 0 | 0 | 1 | 0 | 5 | 0 | 0 |
| c-north/DK | 81 | 10 | 71 | 0 | 0 | 8 | 24 | 49 | 0 | 0 |
| c-north/EE | 91 | 5 | 86 | 0 | 0 | 8 | 4 | 79 | 0 | 0 |
| c-north/FI | 103 | 3 | 100 | 0 | 0 | 3 | 21 | 79 | 0 | 0 |
| c-north/IS | 40 | 1 | 39 | 0 | 0 | 33 | 0 | 7 | 0 | 0 |
| c-north/LT | 19 | 1 | 18 | 0 | 0 | 4 | 1 | 14 | 0 | 0 |
| c-north/LV | 66 | 17 | 49 | 0 | 0 | 17 | 0 | 49 | 0 | 0 |
| c-north/NO | 85 | 8 | 77 | 0 | 0 | 8 | 28 | 49 | 0 | 0 |
| c-north/SE | 49 | 1 | 48 | 0 | 0 | 2 | 8 | 39 | 0 | 0 |
| c-south/AU | 123 | 13 | 110 | 0 | 0 | 11 | 2 | 110 | 0 | 0 |
| c-south/CZ | 92 | 7 | 85 | 0 | 0 | 7 | 50 | 34 | 1 | 0 |
| c-south/HR | 116 | 7 | 109 | 0 | 0 | 9 | 49 | 58 | 0 | 0 |
| c-south/HU | 103 | 15 | 88 | 0 | 0 | 16 | 1 | 86 | 0 | 0 |
| c-south/MX | 27 | 14 | 13 | 0 | 0 | 16 | 2 | 9 | 0 | 0 |
| c-south/PL | 128 | 7 | 121 | 0 | 0 | 7 | 0 | 121 | 0 | 0 |
| c-south/RO | 28 | 15 | 13 | 0 | 0 | 15 | 0 | 13 | 0 | 0 |
| c-south/SI | 114 | 6 | 108 | 0 | 0 | 7 | 18 | 89 | 0 | 0 |
| c-south/SK | 121 | 9 | 112 | 0 | 0 | 9 | 62 | 50 | 0 | 0 |
| fresh-addresses/BE | 112 | 15 | 97 | 0 | 0 | 8 | 48 | 56 | 0 | 0 |
| fresh-addresses/CH | 122 | 6 | 116 | 0 | 0 | 8 | 80 | 34 | 0 | 0 |
| fresh-addresses/CZ | 131 | 13 | 118 | 0 | 0 | 13 | 68 | 50 | 0 | 0 |
| fresh-addresses/DE | 123 | 12 | 111 | 0 | 0 | 10 | 23 | 90 | 0 | 0 |
| fresh-addresses/ES | 132 | 13 | 119 | 0 | 0 | 8 | 80 | 44 | 0 | 0 |
| fresh-addresses/HR | 133 | 13 | 120 | 0 | 0 | 15 | 35 | 83 | 0 | 0 |
| fresh-addresses/IM | 25 | 4 | 21 | 0 | 0 | 5 | 0 | 20 | 0 | 0 |
| fresh-addresses/SK | 134 | 9 | 125 | 0 | 0 | 9 | 64 | 61 | 0 | 0 |
| fresh-ro-mx/MX | 144 | 56 | 88 | 0 | 2 | 64 | 9 | 71 | 0 | 2 |
| fresh-ro-mx/RO | 141 | 45 | 96 | 0 | 0 | 48 | 0 | 93 | 0 | 0 |
| **All** | 4623 | 565 | 4058 | 0 | 2 | 600 | 925 | 3097 | 1 | 2 |

| Reason | Hinted | No hint |
|---|---|---|
| `address.no-last-line` | 254 | 461 |
| `address.model-required` | 279 | 71 |
| `address.country-undetermined` | 0 | 68 |
| `address.no-delivery-line` | 24 | 0 |
| `address.country-conflict` | 8 | 0 |

Reading the table: with a hint nothing was read in a wrong country. The 2 "invented" items are one text each, counted in the hinted and the no-hint column: the `D.F.` to `CDMX` standardization described below. Without a hint a text whose postcode form several countries share is not guessed: it is refused, or returned without a country, which is the honest answer for it.

## Findings

**Hinted reads: 0 wrong country.** A first measurement found 2 hinted items returned as French (a German address with the postcode 97478 and a Mexican one with 97306, both inside France's 97xxx overseas range); with a German, Spanish, Croatian or Mexican hint such a postcode is now read in the hinted country (`HintedPostcodeRangeTests`).

**Invented values: 2 items, one cause, kept.** Two Mexican items write the capital as `D.F.`. The standardized state is `CDMX`, the abbreviation the UPU Mexico sheet and SEPOMEX use for the state that `D.F.` named. It is a standardized abbreviation, not new content, and it is the only printed word of any reading, hinted or not, that does not trace to the input.

**No-hint reads in the wrong country: 29 items at the first measurement, 1 after the fixes.** What changed (all in the rules; `NoHintWrongCountryTests`):

| Cause | Items | Fix |
|---|---|---|
| A five-digit number without a United States state was taken for a ZIP, or a Mexican state abbreviation (`N.L.`, `B.C.`) with a five-digit postcode for a Canadian province | 24 (Mexico 21, Estonia 1, Lithuania 2) | Without a hint the United States and Canada rules read a text only when it shows the country: a state it has, a ZIP+4, a Canadian postal code, or the country written out. `N.L.` and `B.C.` with a five-digit number in the text are not Canadian. `Baja California` and `Nuevo México` do not end in an American state. |
| 97xxx taken as a French overseas postcode | 2 (a German and a Mexican address) | With a German legal form or street word, or a Mexican colonia word, `C.P.` or state, the country is not guessed (`address.country-undetermined`). Text without such a cue is still French. |
| A five-digit house number taken as a Lithuanian postcode while the text carries a Canadian postal code | 1 | A Canadian postal code in the text drops the Tier 2 reading. |
| `6980 LA ROCHE` (Belgian) read as a Dutch postcode with letters `LA` | 1 | Romance articles that are also valid Dutch postcode letters (LA, LE, EL, ET, AU, UN, DU, DI, DA, DO) are not read as Dutch when the text has French street words. |
| A Czech-labelled item whose town and postcode (`040 12`) are Slovak | 1 | Not a rule defect: the text reads as Slovak and the gold label is probably wrong. The one remaining wrong-country read. |

Side effect, by design: a no-hint text with a bare ZIP and no state (`1 Main Street, Springfield 22162`) is no longer read as American; it is refused (`address.no-last-line`) or returned without a country. With the United States given it is read as before. On the register dev sets this changes 21 of the 300 no-hint United States dev items from read to refused and 2 to no country.

**Refusal reasons.** `address.no-last-line` (the text has no postcode or locality line the rules can place) and `address.model-required` (the rules left a field open and only a model package, available from 1.1, can fill it; the reason names the package to add) account for nearly all refusals. `address.country-undetermined` is the no-hint refusal of a text whose postcode form several countries share.
