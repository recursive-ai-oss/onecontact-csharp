# The Tier 1 + Tier 2 model (`RecursiveAI.OneContact.Models`)

The same design as the Tier 1 package (see `models.md`): one fast-tier encoder shared by an address-field head, a name-part head, a name-class head and a contact-span head, trained with OneNLP's `Train` on OneModel. It covers the United States, Canada and every Tier 2 country. Rules run first; the model reads only what the rules leave open, and never replaces a rule hit.

- **Size and cost.** 22.8 MB of weights (Tier 1: 10.2 MB), peak working set of a process that has loaded it and parsed 5,000 addresses and 5,000 names: 367 MB (Tier 1: 277 MB; both include the comparison libraries the measuring tool loads). Load and first call 180 ms (Tier 1: 138 ms). The numbers are indicative (a shared machine, one run).
- **Resolution.** When both model packages are referenced the superset wins. The Tier 1 package never reads Tier 2 text; the superset reads Tier 1 text as Tier 1 does. Without a model package a Tier 2 address the rules refuse stays refused; the refusal message names both packages.
- **Single encode.** A name is encoded once for its class and part heads (Tier 1 as well). The outputs are byte for byte those of two encodes (checked on 5,000 development addresses and 5,000 names), and name analysis allocates 19% less (35.5 to 28.7 KB per call, indicative). A first version took the class vote after the parts head had overwritten the word confidences; the byte comparison caught it and the vote is now taken between the two heads.
- **Country.** The model labels fields; it does not tell countries apart. A Tier 2 read takes the country from the hint, or from the country the rules had decided before they refused the address, and is otherwise refused as country-undetermined. A model read of a text with no hint never displaces a Tier 2 rule reading, and is not made a United States or Canada address on a postal code alone (a German, Spanish or Czech postcode looks like a ZIP): it needs a state or province. This costs recall on no-hint text (below) and is the right trade: the first version labelled such addresses as United States addresses, with the fields right.
- **Dependent locality** (the settlement or sector between street and postcode) is a label of the address head and its own field and line in a model read. On the Czech, Croatian and Romanian development splits the rules read these lines as street text and succeed, so the model is not asked and finds 32 of 304, 48 of 442 and 18 of 317 of them; where the rules refuse (Estonia, Latvia, Lithuania, Mexico) it finds 96 to 99% (Mexico 87%).

## Development splits, rules alone and with the model

Register-rendered development examples (held out before rendering, used to pick the best epoch), 1,500 per country, every labelled field must be recovered with the same label and span ("every field"); field F1 is the micro F1 of the fields. Hint = the country given. Rules are the current rules, so the left value is what the rules do alone.

| Country | hint: every field, rules -> + model | hint: field F1, rules -> + model | no hint: every field | no hint: field F1 | refused with hint |
|---|---|---|---|---|---|
| AD | 91.3 -> 98.1 | 96.2 -> 99.3 | 91.3 -> 94.6 | 96.2 -> 97.8 | 25 -> 0 |
| AT | 82.2 -> 88.1 | 92.7 -> 95.6 | 21.0 -> 22.7 | 43.7 -> 45.4 | 92 -> 0 |
| AU | 91.7 -> 99.3 | 96.1 -> 99.7 | 86.5 -> 99.2 | 93.2 -> 99.7 | 123 -> 6 |
| BE | 41.7 -> 50.5 | 78.7 -> 84.6 | 32.1 -> 34.1 | 59.0 -> 61.2 | 143 -> 0 |
| CA | 77.7 -> 89.1 | 91.0 -> 97.1 | 77.6 -> 85.1 | 91.0 -> 95.3 | 193 -> 0 |
| CH | 60.5 -> 64.7 | 87.1 -> 89.1 | 18.7 -> 19.5 | 49.5 -> 50.3 | 67 -> 1 |
| CZ | 73.1 -> 78.7 | 91.6 -> 94.4 | 31.8 -> 33.5 | 60.7 -> 62.4 | 91 -> 0 |
| DE | 78.9 -> 84.9 | 92.0 -> 94.9 | 57.5 -> 59.0 | 77.4 -> 78.6 | 92 -> 0 |
| DK | 93.9 -> 98.7 | 97.7 -> 99.6 | 61.3 -> 62.6 | 77.6 -> 78.4 | 73 -> 0 |
| EE | 72.1 -> 84.1 | 90.6 -> 96.3 | 71.2 -> 73.6 | 89.9 -> 91.7 | 191 -> 1 |
| ES | 88.1 -> 97.8 | 94.4 -> 99.3 | 23.7 -> 27.2 | 41.9 -> 46.2 | 156 -> 0 |
| FI | 92.3 -> 98.9 | 96.8 -> 99.7 | 80.7 -> 83.5 | 90.4 -> 92.0 | 102 -> 0 |
| FR | 70.6 -> 77.7 | 89.4 -> 93.5 | 54.6 -> 57.7 | 77.5 -> 80.2 | 130 -> 0 |
| GB | 83.5 -> 88.9 | 93.3 -> 96.2 | 83.5 -> 86.8 | 93.3 -> 95.3 | 102 -> 1 |
| GG | 83.1 -> 92.3 | 93.6 -> 98.1 | 83.1 -> 87.4 | 93.6 -> 95.9 | 19 -> 0 |
| HR | 62.8 -> 68.7 | 89.1 -> 92.2 | 26.9 -> 27.8 | 53.3 -> 54.3 | 99 -> 0 |
| HU | 92.7 -> 98.5 | 96.8 -> 99.4 | 74.7 -> 77.3 | 86.5 -> 88.2 | 92 -> 0 |
| IE | 96.3 -> 96.9 | 98.6 -> 98.9 | 93.5 -> 93.8 | 97.4 -> 97.6 | 15 -> 4 |
| IM | 90.1 -> 94.8 | 96.3 -> 98.5 | 90.1 -> 92.2 | 96.3 -> 97.2 | 9 -> 0 |
| IS | 84.1 -> 88.7 | 95.4 -> 97.3 | 13.5 -> 94.1 | 30.0 -> 97.9 | 71 -> 0 |
| IT | 84.5 -> 88.7 | 95.1 -> 97.5 | 58.9 -> 62.4 | 78.6 -> 81.3 | 83 -> 0 |
| JE | 87.7 -> 92.7 | 95.9 -> 98.3 | 87.7 -> 90.9 | 95.9 -> 97.6 | 11 -> 0 |
| LI | 86.0 -> 93.8 | 95.3 -> 98.5 | 82.9 -> 86.0 | 93.5 -> 94.8 | 21 -> 1 |
| LT | 93.9 -> 98.3 | 97.7 -> 99.5 | 93.1 -> 93.9 | 97.3 -> 98.0 | 68 -> 1 |
| LU | 80.6 -> 86.4 | 93.1 -> 95.9 | 54.7 -> 56.1 | 77.1 -> 78.2 | 100 -> 0 |
| LV | 70.3 -> 88.6 | 86.3 -> 96.6 | 70.3 -> 84.5 | 86.3 -> 96.0 | 290 -> 4 |
| MC | 79.6 -> 90.3 | 92.5 -> 97.8 | 79.6 -> 82.3 | 92.5 -> 94.1 | 12 -> 0 |
| MT | 85.8 -> 97.1 | 93.1 -> 98.7 | 85.8 -> 92.5 | 93.1 -> 96.7 | 112 -> 14 |
| MX | 72.6 -> 79.7 | 90.3 -> 94.1 | 67.7 -> 69.7 | 86.7 -> 90.0 | 122 -> 1 |
| NL | 91.1 -> 96.1 | 96.4 -> 98.5 | 87.5 -> 88.6 | 94.4 -> 95.0 | 78 -> 0 |
| NO | 93.5 -> 98.9 | 97.5 -> 99.6 | 66.1 -> 67.4 | 81.1 -> 81.9 | 81 -> 0 |
| PL | 92.0 -> 97.6 | 97.0 -> 99.2 | 89.1 -> 90.7 | 95.7 -> 96.3 | 86 -> 0 |
| PT | 92.1 -> 98.7 | 96.3 -> 99.4 | 92.1 -> 94.9 | 96.3 -> 97.9 | 100 -> 0 |
| RO | 51.0 -> 69.9 | 79.3 -> 88.3 | 51.0 -> 67.5 | 79.3 -> 87.3 | 290 -> 0 |
| SE | 93.9 -> 99.1 | 97.4 -> 99.7 | 63.1 -> 64.9 | 78.8 -> 80.1 | 80 -> 1 |
| SI | 89.3 -> 97.1 | 95.8 -> 99.2 | 64.9 -> 67.8 | 81.5 -> 83.4 | 120 -> 0 |
| SK | 91.5 -> 96.7 | 96.8 -> 99.0 | 50.9 -> 53.0 | 69.9 -> 71.4 | 80 -> 0 |
| SM | 92.0 -> 99.4 | 96.3 -> 99.7 | 92.0 -> 96.3 | 96.3 -> 98.5 | 12 -> 0 |
| US | 90.7 -> 92.7 | 97.3 -> 98.3 | 90.7 -> 92.7 | 97.3 -> 98.3 | 34 -> 0 |
| VA | 96.7 -> 100.0 | 98.4 -> 100.0 | 96.7 -> 100.0 | 98.4 -> 100.0 | 2 -> 0 |

The model lowers no row on the development splits; on no-hint text it raises the rows where the rules decide the country themselves (AU, GB, IE, JE, IM, MT, PT, SM, NL, LV, ...) and the rows that stay low (AT, BE, CH, CZ, DE, ES, FR, HR, IT, SK) are texts whose country the rules cannot tell: they are refused as country-undetermined rather than read as United States addresses (see Country above). A rule-level country inference for those postcode forms would let the model read them.

## Real pages: addresses

Common Crawl evaluation sets (private, aggregates only). Field F1 on coarse labels; `+ model` is the rules and the model of this package; the interval is a paired bootstrap, 1000 resamples, seed 20261105, against libpostal 1.1.4 (Senzing's model alongside). "ok" = not behind libpostal beyond the interval (or within 0.5 points); "behind" = the interval is entirely below zero. The reference was run without a country, so the hinted columns compare us with a hint against it without one.

| Country | n | rules, hint | + model, hint | + model vs libpostal, hint [95% CI] | | libpostal / Senzing | rules, no hint | + model, no hint | + model vs libpostal, no hint [95% CI] | |
|---|---|---|---|---|---|---|---|---|---|---|
| PL | 128 | 89.2 | 90.0 | -5.44 [-9.32, -1.84] | behind | 95.4 / 95.5 | 89.2 | 89.9 | -5.48 [-9.37, -1.87] | behind |
| CZ | 92 | 92.6 | 96.6 | +2.90 [-0.26, +6.52] | ok | 93.7 / 87.9 | 52.6 | 60.8 | -32.95 [-43.51, -23.71] | behind |
| SK | 121 | 92.8 | 94.8 | +1.63 [-1.18, +4.71] | ok | 93.2 / 91.0 | 57.9 | 60.2 | -33.00 [-41.69, -24.46] | behind |
| SI | 114 | 93.8 | 96.4 | +2.10 [-0.11, +4.37] | ok | 94.3 / 94.8 | 86.0 | 89.1 | -5.21 [-9.69, -1.54] | behind |
| HR | 116 | 93.5 | 94.1 | -2.22 [-5.65, +0.86] | ok | 96.3 / 96.0 | 61.0 | 62.9 | -33.42 [-42.26, -25.35] | behind |
| HU | 103 | 84.9 | 84.9 | -6.46 [-11.45, -1.35] | behind | 91.4 / 91.4 | 83.9 | 85.0 | -6.39 [-11.13, -1.79] | behind |
| RO | 28 | 48.4 | 69.0 | +7.51 [-2.51, +16.83] | ok | 61.5 / 63.0 | 48.4 | 69.0 | +7.51 [-2.51, +16.83] | ok |
| MX | 27 | 54.0 | 72.4 | +14.51 [+3.47, +25.79] | ok | 57.9 / 63.9 | 45.2 | 60.8 | +2.94 [-11.62, +18.47] | ok |
| AU | 123 | 89.2 | 94.1 | -0.81 [-3.97, +1.89] | ok | 94.9 / 94.2 | 89.2 | 94.8 | -0.06 [-2.71, +2.49] | ok |
| FR | 101 | 82.7 | 85.3 | +5.82 [+0.91, +10.82] | ok | 79.5 / 80.9 | 78.4 | 81.4 | +1.92 [-4.10, +7.49] | ok |
| DE | 117 | 89.1 | 90.2 | -1.24 [-4.41, +1.98] | ok | 91.5 / 90.6 | 74.9 | 76.5 | -15.01 [-21.64, -9.23] | behind |
| NL | 117 | 95.4 | 97.0 | +40.97 [+36.42, +45.52] | ok | 56.1 / 45.0 | 95.4 | 97.0 | +40.97 [+36.42, +45.52] | ok |
| BE | 95 | 87.2 | 89.5 | +0.71 [-2.52, +3.85] | ok | 88.8 / 88.6 | 68.3 | 69.1 | -19.70 [-26.86, -12.93] | behind |
| AT | 90 | 89.7 | 90.7 | +0.70 [-2.79, +4.43] | ok | 90.0 / 90.9 | 48.5 | 50.8 | -39.13 [-50.86, -29.07] | behind |
| CH | 99 | 91.0 | 91.8 | +3.04 [-0.72, +6.91] | ok | 88.7 / 87.2 | 52.5 | 54.8 | -33.95 [-44.69, -24.84] | behind |
| LU | 92 | 90.4 | 90.5 | +5.29 [+0.63, +9.79] | ok | 85.2 / 84.6 | 88.2 | 88.3 | +3.15 [-1.62, +7.49] | ok |
| LI | 117 | 91.8 | 93.8 | +1.37 [-1.49, +4.37] | ok | 92.4 / 92.3 | 90.9 | 92.7 | +0.23 [-2.80, +3.49] | ok |
| MC | 38 | 84.5 | 87.3 | +26.93 [+20.30, +33.05] | ok | 60.3 / 78.1 | 84.5 | 87.3 | +26.93 [+20.30, +33.05] | ok |
| DK | 81 | 92.3 | 90.4 | +9.08 [+4.18, +15.09] | ok | 81.4 / 81.4 | 78.2 | 77.8 | -3.51 [-10.71, +3.76] | ok |
| SE | 49 | 97.3 | 97.4 | +1.71 [-1.56, +5.92] | ok | 95.7 / 90.2 | 87.8 | 87.8 | -7.89 [-16.25, -0.88] | behind |
| FI | 103 | 94.8 | 95.3 | +8.35 [+4.07, +12.94] | ok | 87.0 / 87.0 | 86.3 | 86.6 | -0.35 [-5.70, +4.79] | ok |
| NO | 85 | 88.8 | 91.7 | -1.75 [-5.58, +1.84] | ok | 93.5 / 93.1 | 74.1 | 78.1 | -15.37 [-22.94, -8.79] | behind |
| IS | 40 | 89.4 | 90.4 | -1.69 [-7.30, +4.78] | ok | 92.1 / 86.7 | 30.6 | 88.9 | -3.24 [-8.66, +2.79] | ok |
| EE | 91 | 86.1 | 87.3 | -2.03 [-5.77, +1.79] | ok | 89.3 / 90.7 | 55.8 | 67.9 | -21.43 [-28.19, -15.43] | behind |
| LV | 66 | 81.8 | 92.0 | +3.87 [-0.36, +8.27] | ok | 88.2 / 89.7 | 81.8 | 92.0 | +3.87 [-0.36, +8.27] | ok |
| LT | 19 | 71.9 | 90.9 | +4.32 [-2.58, +12.36] | ok | 86.6 / 88.6 | 69.5 | 83.0 | -3.57 [-15.27, +8.41] | ok |
| GB | 142 | 74.4 | 78.9 | -4.27 [-8.14, -0.92] | behind | 83.1 / 83.2 | 74.4 | 78.9 | -4.27 [-8.14, -0.92] | behind |
| JE | 51 | 77.2 | 78.4 | -4.51 [-11.90, +2.88] | ok | 82.9 / 73.8 | 77.2 | 78.4 | -4.51 [-11.90, +2.88] | ok |
| GG | 24 | 65.6 | 65.8 | +17.65 [+6.57, +28.76] | ok | 48.1 / 79.0 | 65.6 | 65.8 | +17.65 [+6.57, +28.76] | ok |
| IM | 59 | 78.4 | 77.6 | -8.96 [-16.53, -1.31] | behind | 86.6 / 89.0 | 78.4 | 77.6 | -8.96 [-16.53, -1.31] | behind |
| IE | 92 | 60.0 | 66.8 | -1.44 [-7.14, +4.64] | ok | 68.2 / 58.2 | 49.1 | 59.8 | -8.35 [-15.24, -0.96] | behind |
| ES | 69 | 67.3 | 83.3 | +6.94 [+1.80, +11.86] | ok | 76.3 / 76.8 | 35.9 | 45.6 | -30.66 [-43.30, -19.28] | behind |
| PT | 55 | 74.8 | 75.9 | +4.36 [-2.70, +10.86] | ok | 71.5 / 73.8 | 74.8 | 75.9 | +4.36 [-2.70, +10.86] | ok |
| IT | 60 | 94.1 | 95.8 | +6.50 [+3.12, +9.81] | ok | 89.3 / 88.4 | 76.3 | 82.0 | -7.34 [-16.63, +0.60] | ok |
| MT | 49 | 62.5 | 70.4 | +30.68 [+20.68, +41.39] | ok | 39.7 / 41.0 | 62.5 | 70.4 | +30.68 [+20.68, +41.39] | ok |
| SM | 61 | 75.7 | 83.2 | +14.91 [+7.57, +22.31] | ok | 68.3 / 75.6 | 75.7 | 83.2 | +14.91 [+7.57, +22.31] | ok |
| VA | 6 | 83.7 | 80.8 | +22.03 [-12.25, +47.65] | ok | 58.8 / 53.9 | 83.7 | 80.8 | +22.03 [-12.25, +47.65] | ok |
| AD | 46 | 74.4 | 78.3 | -1.70 [-7.81, +4.46] | ok | 80.0 / 82.3 | 74.4 | 78.3 | -1.70 [-7.81, +4.46] | ok |
| US | 229 | 95.8 | 97.2 | +0.00 [-1.08, +1.10] | ok | 97.2 / 97.1 | 95.8 | 97.2 | +0.00 [-1.08, +1.10] | ok |
| CA | 231 | 91.7 | 92.5 | -0.82 [-2.81, +0.99] | ok | 93.3 / 92.5 | 91.7 | 92.5 | -0.82 [-2.81, +0.99] | ok |

## Real pages: names

Accuracy of person-name tokens (person and multiple items), of the person/company/unknown class and of company-name tokens, rules alone and with the model, next to the best of probablepeople 0.5.6 and python-nameparser 2.8.2 (the class and company-token rows only have probablepeople; nameparser gives person tokens).

| Country | person tokens: rules | + model | best of probablepeople / nameparser | class: rules | + model | best reference | company tokens: rules | + model | best reference |
|---|---|---|---|---|---|---|---|---|---|
| PL | 100.0 | 100.0 | 89.4 | 71.0 | 79.0 | 74.2 | 59.8 | 73.8 | 42.6 |
| CZ | 100.0 | 100.0 | 97.0 | 97.7 | 98.8 | 55.2 | 90.7 | 94.4 | 47.2 |
| SK | 94.5 | 94.5 | 98.2 | 100.0 | 100.0 | 61.3 | 98.6 | 98.6 | 46.5 |
| SI | 93.2 | 93.2 | 97.7 | 91.7 | 95.8 | 47.9 | 85.7 | 92.9 | 33.3 |
| HR | 97.7 | 97.7 | 97.7 | 87.5 | 93.8 | 52.1 | 80.0 | 91.2 | 35.0 |
| HU | 96.1 | 96.1 | 17.6 | 100.0 | 100.0 | 81.8 | 94.8 | 94.8 | 70.1 |
| RO | 80.4 | 76.1 | 80.4 | 97.9 | 95.7 | 76.6 | 87.5 | 87.5 | 55.2 |
| MX | 93.5 | 93.5 | 88.7 | 86.3 | 90.2 | 78.4 | 91.3 | 95.2 | 60.2 |
| AU | 100.0 | 100.0 | 100.0 | 74.5 | 84.3 | 86.3 | 68.6 | 81.4 | 73.5 |
| FR | 95.6 | 95.6 | 97.1 | 67.2 | 81.0 | 65.5 | 37.3 | 66.1 | 49.1 |
| DE | 95.2 | 90.5 | 95.2 | 78.8 | 80.3 | 80.3 | 61.4 | 70.0 | 64.3 |
| NL | 97.9 | 93.6 | 97.9 | 64.6 | 67.1 | 67.1 | 39.8 | 50.0 | 45.5 |
| BE | 89.0 | 89.0 | 89.0 | 73.4 | 78.1 | 75.0 | 51.3 | 60.5 | 61.8 |
| AT | 95.1 | 95.1 | 95.1 | 79.4 | 88.2 | 69.1 | 54.9 | 80.3 | 63.4 |
| CH | 95.6 | 95.6 | 100.0 | 80.0 | 88.6 | 72.9 | 68.2 | 84.1 | 71.6 |
| LU | 97.1 | 88.4 | 91.3 | 65.7 | 74.6 | 73.1 | 54.6 | 77.3 | 59.8 |
| LI | 92.7 | 90.2 | 93.9 | 72.9 | 79.7 | 72.9 | 62.0 | 77.5 | 71.8 |
| MC | 100.0 | 100.0 | 100.0 | 30.6 | 41.7 | 36.1 | 28.8 | 42.4 | 27.1 |
| DK | 93.6 | 91.0 | 93.6 | 87.3 | 92.7 | 81.8 | 66.7 | 86.4 | 62.1 |
| SE | 100.0 | 100.0 | 100.0 | 100.0 | 100.0 | 83.9 | 87.2 | 87.2 | 72.3 |
| FI | 89.6 | 80.5 | 89.6 | 94.4 | 88.9 | 66.7 | 77.8 | 87.0 | 40.7 |
| NO | 95.0 | 93.8 | 95.0 | 94.1 | 96.1 | 68.6 | 82.7 | 94.2 | 57.7 |
| IS | 100.0 | 90.5 | 100.0 | 100.0 | 95.0 | 70.0 | 96.2 | 96.2 | 50.0 |
| EE | 100.0 | 84.9 | 100.0 | 100.0 | 91.3 | 84.8 | 98.2 | 98.2 | 55.6 |
| LV | 100.0 | 96.2 | 100.0 | 100.0 | 97.2 | 69.4 | 100.0 | 100.0 | 26.9 |
| LT | 91.3 | 82.6 | 95.7 | 97.1 | 94.3 | 80.0 | 90.9 | 90.9 | 42.4 |
| GB | 91.6 | 91.6 | 96.8 | 97.8 | 98.9 | 93.4 | 95.2 | 96.8 | 81.4 |
| JE | 100.0 | 98.2 | 100.0 | 100.0 | 98.7 | 94.7 | 99.0 | 99.0 | 83.3 |
| GG | 100.0 | 100.0 | 100.0 | 100.0 | 100.0 | 92.5 | 100.0 | 100.0 | 91.1 |
| IM | 100.0 | 94.2 | 98.8 | 100.0 | 97.3 | 91.9 | 100.0 | 100.0 | 92.6 |
| IE | 93.8 | 91.7 | 97.9 | 100.0 | 98.8 | 91.2 | 100.0 | 100.0 | 90.1 |
| ES | 100.0 | 98.5 | 89.2 | 100.0 | 98.8 | 82.1 | 99.4 | 99.4 | 72.9 |
| PT | 98.0 | 98.0 | 94.1 | 100.0 | 100.0 | 81.5 | 100.0 | 100.0 | 75.9 |
| IT | 97.2 | 96.1 | 98.3 | 100.0 | 99.0 | 65.7 | 99.3 | 99.3 | 57.4 |
| MT | 95.7 | 95.7 | 97.4 | 100.0 | 100.0 | 89.3 | 100.0 | 100.0 | 89.7 |
| SM | 100.0 | 100.0 | 96.5 | 100.0 | 100.0 | 89.2 | 99.1 | 99.1 | 60.0 |
| VA | 90.0 | 90.0 | 90.0 | 100.0 | 100.0 | 81.0 | 100.0 | 100.0 | 64.9 |
| AD | 100.0 | 88.2 | 100.0 | 100.0 | 94.4 | 88.9 | 97.1 | 97.1 | 62.9 |
| US | 98.0 | 98.0 | 100.0 | 79.3 | 88.7 | 90.2 | 68.1 | 84.6 | 76.8 |
| CA | 98.6 | 97.9 | 99.6 | 80.8 | 88.5 | 85.6 | 64.6 | 82.9 | 78.3 |

**Reading the name rows.** The Tier 2 person names the model learned come from small lists of our own (about 20 to 40 names per language), so on real pages the model reads some unfamiliar person names as companies or unknown, and person-token or class accuracy falls below the rules alone in DE, NL, LU, LI, DK, FI, IS, EE, LV, LT, JE, IM, IE, ES, IT, AD and RO. It raises the class and company rows in most other countries (for example FR, AT, CH, PL, SI, HR, US and CA). A larger open name source, licence-checked per country, is the fix; until then these rows are the package's weakest.

## Extraction (United States and Canada pages, no regression)

Value F1 of the shipped extractor on the private web (119 pages) and text (115 items) sets, with the contact-span model at cut 0.4: rules and markup alone, with the Tier 1 model, and with this model.

| Set | Field | Rules | Tier 1 | This model |
|---|---|---|---|---|
| web, visible text | all fields | 73.4 | 73.5 | 73.6 |
| web, visible text | person | 17.9 | 19.4 | 21.4 |
| web, visible text | org | 36.7 | 35.9 | 34.9 |
| web, visible text | record F1 | 24.6 | 25.4 | 25.4 |
| text | all fields | 73.5 | 74.0 | 74.9 |
| text | address | 46.2 | 71.7 | 72.7 |
| text | person | 27.6 | 27.8 | 38.9 |
| text | org | 60.3 | 55.6 | 54.1 |
| text | record F1 | 29.8 | 31.1 | 30.6 |

Phone, email and website rows are identical in the three columns. Differences between the Tier 1 and this model's columns are inside the bootstrap intervals.

## Reproducing

`recipes/models` (train and span stages), `tools/Eval/Tier2Recovery` (development table), `tools/Eval/run-superset.sh` (real-page rows), `tools/Eval/extraction/run-model-eval.sh` (extraction rows).
