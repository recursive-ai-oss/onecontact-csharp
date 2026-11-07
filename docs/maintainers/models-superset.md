# The Tier 1 + Tier 2 model (`RecursiveAI.OneContact.Models`)

The same design as the Tier 1 package (see `models.md`): one fast-tier encoder shared by an address-field head, a name-part head, a name-class head and a contact-span head, trained with OneNLP's `Train` on OneModel. It covers the United States, Canada and every Tier 2 country. Rules run first; the model reads only what the rules leave open, and never replaces a rule hit.

- **Size and cost.** 22.8 MB of weights (Tier 1: 10.2 MB), peak working set of a process that has loaded it and parsed 5,000 addresses and 5,000 names: 367 MB (Tier 1: 277 MB; both include the comparison libraries the measuring tool loads). Load and first call 180 ms (Tier 1: 138 ms). The numbers are indicative (a shared machine, one run).
- **Resolution.** When both model packages are referenced the superset wins. The Tier 1 package never reads Tier 2 text; the superset reads Tier 1 text as Tier 1 does. Without a model package a Tier 2 address the rules refuse stays refused; the refusal message names both packages.
- **Single encode.** A name is encoded once for its class and part heads (Tier 1 as well). The outputs are byte for byte those of two encodes (checked on 5,000 development addresses and 5,000 names), and name analysis allocates 19% less (35.5 to 28.7 KB per call, indicative). A first version took the class vote after the parts head had overwritten the word confidences; the byte comparison caught it and the vote is now taken between the two heads.
- **Country.** The model labels fields; it does not tell countries apart. A Tier 2 read takes the country from the hint, or from the country the rules had decided before they refused the address, and is otherwise refused as country-undetermined. A model read of a text with no hint never displaces a Tier 2 rule reading, and is not made a United States or Canada address on a postal code alone (a German, Spanish or Czech postcode looks like a ZIP): it needs a state or province. This costs recall on no-hint text (below). A wrong country is invented content, so the refusal is the decision, not a gap to close in the model: recall comes back through rules-level country inference, after which the model reads the text with the country known. The first version labelled such addresses as United States addresses, with the fields right.
- **Dependent locality** (the settlement or sector between street and postcode) is a label of the address head and its own field and line in a model read. On the Czech, Croatian and Romanian development splits the rules read these lines as street text and succeed, so the model is not asked and finds 32 of 304, 48 of 442 and 18 of 317 of them; where the rules refuse (Estonia, Latvia, Lithuania, Mexico) it finds 96 to 99% (Mexico 87%).

**Current weights (`c5cedca0…`, 22,785,395 bytes).** The encoder and address head are trained on the register sets mixed with 1,200,000 OpenStreetMap addresses (ODbL; © OpenStreetMap contributors, ODbL 1.0), then the name heads (national name lists, frozen encoder) and the contact-span head. The weights ship `MIT OR Apache-2.0` with every source credited. Recipe, data, seeds and hashes: `src/OneContact.Models/PROVENANCE.md` and `models-challenger.md` (Full candidate). Compared with the previous permissive-only weights (`0e31f3fd…`) on the same rules, on the frozen real-page sets, with paired 95% intervals: address field F1 hinted gains in GB +3.3, IE +7.2, IM +7.6, MT +4.7, HU +5.7, DE +1.5, LI +1.3; no hint adds SM, EE, HR; full parse gains in IE, MT, LV (EE no hint). **One row loses: ES hinted address field F1, -1.13 [-2.46, -0.02] (n=69).** Tier 2 name rows: no interval wholly below zero (EE, FR and LI have upper ends of exactly 0). Extraction: text address +11.9 [+3.1, +22.5], text record F1 +6.5 [+1.0, +13.1], no row below its interval. All rows: `recipes/models/full-candidate/`. The tables below were measured with the previous weights; they are kept as the permissive-only reference. The previous weights are rebuilt with `run.sh`, `names.sh` and `spans.sh` without any OpenStreetMap folder. The Tier 2 person override list (AU, EE, FR, GB, IT, LI, MT) is unchanged; the development rule that chose it no longer selects any country with the current rules.

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

Common Crawl evaluation sets (private, aggregates only). Field F1 on coarse labels; `+ model` is the rules and the model of this package; the interval is a paired bootstrap, 1000 resamples, seed 20261105, against libpostal 1.1.4 (Senzing's model alongside). "ok" = not behind libpostal beyond the interval (or within 0.5 points); "behind" = the interval is entirely below zero. **The reference was run without a country**, so the hinted columns compare us with a hint against libpostal and Senzing without one; they are to be re-run like for like with the item country given to the reference.

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

Accuracy of person-name tokens (person and multiple items), of company-name tokens and of the person/company/unknown class on the private sets. Columns: rules alone; the weights shipped before (`adde4a66…`, name heads trained on our own Tier 2 lists, Tier 2 override off); the weights of this version (`0e31f3fd…`, name heads retrained on national name lists); the best reference of the row (probablepeople 0.5.6 or python-nameparser 2.8.2, the higher score); this version minus that reference and minus the rules, with paired bootstrap 95% intervals (1000 resamples, seed 20261105). Where the interval of "vs best" lies entirely below zero the row is behind; no row is, though person tokens against python-nameparser in CA, IE and RO have an upper end of exactly 0.

**Policy for Tier 2 names.** The Tier 2 person names the earlier weights learned came from small lists of our own, and a first version of the override (the model replacing the rules' default reading of a string as a person, when it said company or unknown with confidence 0.6) turned unfamiliar real person names into companies in many countries. The name heads are now retrained (frozen encoder; the address and contact-span heads are byte-identical in their predictions) on person names drawn from open national name statistics (`../tools/data/provenance/names-tier2.md`), and the override is allowed per country, chosen on the development names: where it gained at least 0.3 points of class accuracy at confidence 0.8 on a development country with at least 150 items. Those are AU, EE, FR, GB, IE, IT, JE, LI, MT and RO. On the real-page sets IE, JE and RO each lost one item against the rules alone. The same item is lost with the override off, so it comes from the new heads reading a rule refusal, not from the override; the override was still left off in those three, which showed no real-page gain from it. The enabled list is AU, EE, FR, GB, IT, LI and MT. Every other Tier 2 country reads only what the rules leave open, as before. The list is `OverrideCountries` in `ModelNameReader`; United States and Canada keep confidence 0.6, and the Tier 1 package is unchanged. A first-version table showed company and class gains in AT, CH, NL, DE, NO and others that the development names do not predict; they are not enabled.

**Training.** `recipes/models names` retrains the two name heads from the shipped weights with the encoder frozen: 250,000 US/CA name examples, 100,000 Tier 2 person examples (drawn by a hash of the text from the 1.56 million rendered), all 123,948 Tier 2 company examples; 4 epochs of 2,114 steps, seed 1, learning rate 0.001, best dev epoch kept; 38 seconds on 8 CPU threads. Development score (mean of name-part F1 and class accuracy on 14,882 development names): 0.9656 before, 0.9820 after. More Tier 2 persons in the mix lowered the United States and Canada company and class rows (450,000 persons: class -2.5 on the US set), because the head's prior for "person" rises with their share; 100,000 keeps them within noise of the shipped weights (US class -1.0 [-2.5, 0.0], CA -1.0 [-2.4, 0.0]). Variants with 50,000 and 100,000 (seed 2), 300,000 and 450,000 persons, and one without the English-language units were also scored on the real sets of the United States and Canada before this one was chosen, so those two rows are not an untouched test.

### Person tokens

| Country | n | rules | shipped before | this model | best reference | this model vs best [95% CI] | vs rules [95% CI] |
|---|---|---|---|---|---|---|---|
| AD | 8 | 100.0 | 100.0 | 100.0 | probab 100.0 | +0.0 [+0.0, +0.0] | +0.0 [+0.0, +0.0] |
| AT | 37 | 95.1 | 95.1 | 95.1 | namepa 95.1 | +0.0 [+0.0, +0.0] | +0.0 [+0.0, +0.0] |
| AU | 19 | 100.0 | 100.0 | 100.0 | namepa 100.0 | +0.0 [+0.0, +0.0] | +0.0 [+0.0, +0.0] |
| BE | 35 | 89.0 | 89.0 | 89.0 | namepa 89.0 | +0.0 [+0.0, +0.0] | +0.0 [+0.0, +0.0] |
| CA | 126 | 98.6 | 97.9 | 97.9 | namepa 99.6 | -1.8 [-3.9, +0.0] | -0.7 [-2.2, +0.0] |
| CH | 35 | 95.6 | 95.6 | 95.6 | namepa 100.0 | -4.4 [-10.5, +0.0] | +0.0 [+0.0, +0.0] |
| CZ | 54 | 100.0 | 100.0 | 100.0 | namepa 97.0 | +3.0 [+0.0, +7.8] | +0.0 [+0.0, +0.0] |
| DE | 35 | 95.2 | 95.2 | 95.2 | namepa 95.2 | +0.0 [+0.0, +0.0] | +0.0 [+0.0, +0.0] |
| DK | 31 | 93.6 | 93.6 | 93.6 | namepa 93.6 | +0.0 [+0.0, +0.0] | +0.0 [+0.0, +0.0] |
| EE | 26 | 100.0 | 100.0 | 100.0 | namepa 100.0 | +0.0 [+0.0, +0.0] | +0.0 [+0.0, +0.0] |
| ES | 42 | 100.0 | 100.0 | 100.0 | namepa 89.2 | +10.8 [+6.5, +15.0] | +0.0 [+0.0, +0.0] |
| FI | 36 | 89.6 | 89.6 | 89.6 | namepa 89.6 | +0.0 [+0.0, +0.0] | +0.0 [+0.0, +0.0] |
| FR | 31 | 95.6 | 95.6 | 95.6 | namepa 97.1 | -1.5 [-4.6, +0.0] | +0.0 [+0.0, +0.0] |
| GB | 37 | 91.6 | 91.6 | 91.6 | namepa 96.8 | -5.3 [-13.3, +0.0] | +0.0 [+0.0, +0.0] |
| GG | 40 | 100.0 | 100.0 | 100.0 | namepa 100.0 | +0.0 [+0.0, +0.0] | +0.0 [+0.0, +0.0] |
| HR | 17 | 97.7 | 97.7 | 97.7 | namepa 97.7 | +0.0 [+0.0, +0.0] | +0.0 [+0.0, +0.0] |
| HU | 20 | 96.1 | 96.1 | 96.1 | probab 17.6 | +78.4 [+68.4, +89.1] | +0.0 [+0.0, +0.0] |
| IE | 37 | 93.8 | 93.8 | 91.7 | namepa 97.9 | -6.2 [-13.6, +0.0] | -2.1 [-6.6, +0.0] |
| IM | 39 | 100.0 | 100.0 | 100.0 | namepa 98.8 | +1.1 [+0.0, +3.5] | +0.0 [+0.0, +0.0] |
| IS | 9 | 100.0 | 100.0 | 100.0 | namepa 100.0 | +0.0 [+0.0, +0.0] | +0.0 [+0.0, +0.0] |
| IT | 65 | 97.2 | 97.2 | 97.2 | namepa 98.3 | -1.1 [-4.6, +2.3] | +0.0 [+0.0, +0.0] |
| JE | 44 | 100.0 | 100.0 | 98.2 | namepa 100.0 | -1.9 [-7.2, +0.0] | -1.9 [-7.2, +0.0] |
| LI | 29 | 92.7 | 92.7 | 92.7 | namepa 93.9 | -1.2 [-9.9, +8.4] | +0.0 [+0.0, +0.0] |
| LT | 22 | 91.3 | 91.3 | 91.3 | namepa 95.7 | -4.3 [-12.8, +0.0] | +0.0 [+0.0, +0.0] |
| LU | 29 | 97.1 | 97.1 | 97.1 | namepa 91.3 | +5.8 [+0.0, +13.5] | +0.0 [+0.0, +0.0] |
| LV | 26 | 100.0 | 100.0 | 100.0 | namepa 100.0 | +0.0 [+0.0, +0.0] | +0.0 [+0.0, +0.0] |
| MC | 4 | 100.0 | 100.0 | 100.0 | probab 100.0 | +0.0 [+0.0, +0.0] | +0.0 [+0.0, +0.0] |
| MT | 45 | 95.7 | 95.7 | 95.7 | namepa 97.4 | -1.7 [-5.3, +0.0] | +0.0 [+0.0, +0.0] |
| MX | 23 | 93.5 | 93.5 | 93.5 | namepa 88.7 | +4.8 [-3.3, +12.5] | +0.0 [+0.0, +0.0] |
| NL | 40 | 97.9 | 97.9 | 97.9 | namepa 97.9 | +0.0 [+0.0, +0.0] | +0.0 [+0.0, +0.0] |
| NO | 32 | 95.0 | 95.0 | 95.0 | namepa 95.0 | +0.0 [+0.0, +0.0] | +0.0 [+0.0, +0.0] |
| PL | 26 | 100.0 | 100.0 | 100.0 | namepa 89.4 | +10.6 [+2.8, +20.3] | +0.0 [+0.0, +0.0] |
| PT | 41 | 98.0 | 98.0 | 98.0 | namepa 94.1 | +3.9 [+0.0, +9.8] | +0.0 [+0.0, +0.0] |
| RO | 20 | 80.4 | 80.4 | 76.1 | namepa 80.4 | -4.3 [-13.9, +0.0] | -4.3 [-13.9, +0.0] |
| SE | 19 | 100.0 | 100.0 | 100.0 | namepa 100.0 | +0.0 [+0.0, +0.0] | +0.0 [+0.0, +0.0] |
| SI | 18 | 93.2 | 93.2 | 93.2 | namepa 97.7 | -4.5 [-13.3, +0.0] | +0.0 [+0.0, +0.0] |
| SK | 37 | 94.5 | 94.5 | 94.5 | namepa 98.2 | -3.7 [-9.1, +0.0] | +0.0 [+0.0, +0.0] |
| SM | 36 | 100.0 | 100.0 | 100.0 | namepa 96.5 | +3.5 [+0.0, +10.5] | +0.0 [+0.0, +0.0] |
| US | 96 | 98.0 | 98.0 | 98.0 | namepa 100.0 | -2.0 [-4.9, +0.0] | +0.0 [+0.0, +0.0] |
| VA | 13 | 90.0 | 90.0 | 90.0 | namepa 90.0 | +0.0 [+0.0, +0.0] | +0.0 [+0.0, +0.0] |

### Company tokens

| Country | n | rules | shipped before | this model | best reference | this model vs best [95% CI] | vs rules [95% CI] |
|---|---|---|---|---|---|---|---|
| AD | 10 | 97.1 | 97.1 | 97.1 | probab 62.9 | +34.3 [+19.4, +55.9] | +0.0 [+0.0, +0.0] |
| AT | 31 | 54.9 | 54.9 | 54.9 | probab 63.4 | -8.4 [-26.7, +10.3] | +0.0 [+0.0, +0.0] |
| AU | 32 | 68.6 | 68.6 | 78.4 | probab 73.5 | +4.9 [-15.2, +23.0] | +9.8 [+1.8, +20.6] |
| BE | 29 | 51.3 | 51.3 | 51.3 | probab 61.8 | -10.5 [-33.9, +11.4] | +0.0 [+0.0, +0.0] |
| CA | 82 | 64.6 | 82.9 | 81.0 | probab 78.3 | +2.7 [-6.1, +10.4] | +16.4 [+8.7, +25.7] |
| CH | 35 | 68.2 | 68.2 | 68.2 | probab 71.6 | -3.4 [-18.3, +10.0] | +0.0 [+0.0, +0.0] |
| CZ | 33 | 90.7 | 90.7 | 90.7 | probab 47.2 | +43.5 [+31.5, +56.6] | +0.0 [+0.0, +0.0] |
| DE | 31 | 61.4 | 61.4 | 61.4 | probab 64.3 | -2.9 [-21.2, +14.5] | +0.0 [+0.0, +0.0] |
| DK | 24 | 66.7 | 72.7 | 72.7 | probab 62.1 | +10.6 [-9.4, +31.8] | +6.1 [+0.0, +18.2] |
| EE | 20 | 98.2 | 98.2 | 98.2 | probab 55.6 | +42.6 [+36.2, +50.0] | +0.0 [+0.0, +0.0] |
| ES | 42 | 99.4 | 99.4 | 99.4 | probab 72.9 | +26.5 [+17.7, +36.3] | +0.0 [+0.0, +0.0] |
| FI | 18 | 77.8 | 87.0 | 87.0 | probab 40.7 | +46.3 [+31.2, +66.7] | +9.3 [+0.0, +27.3] |
| FR | 27 | 37.3 | 37.3 | 66.1 | probab 49.1 | +16.9 [-9.5, +39.6] | +28.8 [+12.1, +45.9] |
| GB | 54 | 95.2 | 95.2 | 96.8 | probab 81.4 | +15.4 [+8.4, +23.5] | +1.6 [+0.0, +5.0] |
| GG | 27 | 100.0 | 100.0 | 100.0 | probab 91.1 | +8.9 [+3.1, +16.7] | +0.0 [+0.0, +0.0] |
| HR | 31 | 80.0 | 80.0 | 80.0 | probab 35.0 | +45.0 [+18.1, +71.0] | +0.0 [+0.0, +0.0] |
| HU | 24 | 94.8 | 94.8 | 94.8 | probab 70.1 | +24.7 [+15.8, +34.2] | +0.0 [+0.0, +0.0] |
| IE | 43 | 100.0 | 100.0 | 100.0 | probab 90.1 | +9.9 [+5.5, +14.8] | +0.0 [+0.0, +0.0] |
| IM | 35 | 100.0 | 100.0 | 100.0 | probab 92.6 | +7.4 [+2.5, +13.4] | +0.0 [+0.0, +0.0] |
| IS | 11 | 96.2 | 96.2 | 96.2 | probab 50.0 | +46.1 [+32.1, +63.6] | +0.0 [+0.0, +0.0] |
| IT | 40 | 99.3 | 99.3 | 99.3 | probab 57.4 | +41.9 [+30.9, +53.0] | +0.0 [+0.0, +0.0] |
| JE | 31 | 99.0 | 99.0 | 99.0 | probab 83.3 | +15.6 [+8.6, +24.7] | +0.0 [+0.0, +0.0] |
| LI | 30 | 62.0 | 62.0 | 74.7 | probab 71.8 | +2.8 [-10.0, +14.1] | +12.7 [+2.6, +26.6] |
| LT | 13 | 90.9 | 90.9 | 90.9 | probab 42.4 | +48.5 [+30.6, +69.7] | +0.0 [+0.0, +0.0] |
| LU | 38 | 54.6 | 54.6 | 54.6 | probab 59.8 | -5.2 [-27.2, +14.8] | +0.0 [+0.0, +0.0] |
| LV | 10 | 100.0 | 100.0 | 100.0 | probab 26.9 | +73.1 [+57.1, +88.5] | +0.0 [+0.0, +0.0] |
| MC | 32 | 28.8 | 28.8 | 28.8 | probab 27.1 | +1.7 [-17.3, +20.6] | +0.0 [+0.0, +0.0] |
| MT | 30 | 100.0 | 100.0 | 100.0 | probab 89.7 | +10.3 [+5.6, +15.5] | +0.0 [+0.0, +0.0] |
| MX | 28 | 91.3 | 91.3 | 91.3 | probab 60.2 | +31.1 [+16.1, +45.3] | +0.0 [+0.0, +0.0] |
| NL | 42 | 39.8 | 39.8 | 39.8 | probab 45.5 | -5.7 [-25.3, +10.8] | +0.0 [+0.0, +0.0] |
| NO | 19 | 82.7 | 82.7 | 82.7 | probab 57.7 | +25.0 [-1.8, +49.0] | +0.0 [+0.0, +0.0] |
| PL | 36 | 59.8 | 59.8 | 59.8 | probab 42.6 | +17.2 [-3.6, +35.0] | +0.0 [+0.0, +0.0] |
| PT | 40 | 100.0 | 100.0 | 100.0 | probab 75.9 | +24.1 [+13.6, +37.0] | +0.0 [+0.0, +0.0] |
| RO | 27 | 87.5 | 87.5 | 87.5 | probab 55.2 | +32.3 [+21.8, +44.4] | +0.0 [+0.0, +0.0] |
| SE | 12 | 87.2 | 87.2 | 87.2 | probab 72.3 | +14.9 [+2.0, +25.0] | +0.0 [+0.0, +0.0] |
| SI | 30 | 85.7 | 85.7 | 85.7 | probab 33.3 | +52.4 [+30.0, +74.0] | +0.0 [+0.0, +0.0] |
| SK | 25 | 98.6 | 98.6 | 98.6 | probab 46.5 | +52.1 [+38.2, +67.2] | +0.0 [+0.0, +0.0] |
| SM | 38 | 99.1 | 99.1 | 99.1 | probab 60.0 | +39.1 [+29.8, +47.7] | +0.0 [+0.0, +0.0] |
| US | 107 | 68.1 | 84.6 | 83.1 | probab 76.8 | +6.3 [-1.8, +14.0] | +15.1 [+8.5, +22.9] |
| VA | 8 | 100.0 | 100.0 | 100.0 | probab 64.9 | +35.1 [+13.9, +59.0] | +0.0 [+0.0, +0.0] |

### Class

| Country | n | rules | shipped before | this model | best reference | this model vs best [95% CI] | vs rules [95% CI] |
|---|---|---|---|---|---|---|---|
| AD | 18 | 100.0 | 100.0 | 100.0 | probab 88.9 | +11.1 [+0.0, +27.8] | +0.0 [+0.0, +0.0] |
| AT | 68 | 79.4 | 79.4 | 79.4 | probab 69.1 | +10.3 [-1.5, +22.1] | +0.0 [+0.0, +0.0] |
| AU | 51 | 74.5 | 74.5 | 82.3 | probab 86.3 | -3.9 [-17.6, +9.8] | +7.8 [+2.0, +15.7] |
| BE | 64 | 73.4 | 73.4 | 73.4 | probab 75.0 | -1.6 [-15.6, +12.5] | +0.0 [+0.0, +0.0] |
| CA | 208 | 80.8 | 88.5 | 87.5 | probab 85.6 | +1.9 [-2.9, +6.7] | +6.7 [+3.4, +11.1] |
| CH | 70 | 80.0 | 80.0 | 80.0 | probab 72.9 | +7.1 [-4.3, +18.6] | +0.0 [+0.0, +0.0] |
| CZ | 87 | 97.7 | 97.7 | 97.7 | probab 55.2 | +42.5 [+32.2, +54.0] | +0.0 [+0.0, +0.0] |
| DE | 66 | 78.8 | 78.8 | 78.8 | probab 80.3 | -1.5 [-10.6, +7.6] | +0.0 [+0.0, +0.0] |
| DK | 55 | 87.3 | 89.1 | 89.1 | probab 81.8 | +7.3 [-5.5, +20.0] | +1.8 [+0.0, +5.5] |
| EE | 46 | 100.0 | 100.0 | 100.0 | probab 84.8 | +15.2 [+6.5, +28.3] | +0.0 [+0.0, +0.0] |
| ES | 84 | 100.0 | 100.0 | 100.0 | probab 82.1 | +17.9 [+10.7, +26.2] | +0.0 [+0.0, +0.0] |
| FI | 54 | 94.4 | 96.3 | 96.3 | probab 66.7 | +29.6 [+16.7, +40.7] | +1.9 [+0.0, +5.6] |
| FR | 58 | 67.2 | 67.2 | 81.0 | probab 65.5 | +15.5 [+3.5, +31.0] | +13.8 [+5.2, +22.4] |
| GB | 91 | 97.8 | 97.8 | 98.9 | probab 93.4 | +5.5 [+0.0, +11.0] | +1.1 [+0.0, +3.3] |
| GG | 67 | 100.0 | 100.0 | 100.0 | probab 92.5 | +7.5 [+1.5, +13.4] | +0.0 [+0.0, +0.0] |
| HR | 48 | 87.5 | 87.5 | 87.5 | probab 52.1 | +35.4 [+16.7, +54.2] | +0.0 [+0.0, +0.0] |
| HU | 44 | 100.0 | 100.0 | 100.0 | probab 81.8 | +18.2 [+6.8, +29.6] | +0.0 [+0.0, +0.0] |
| IE | 80 | 100.0 | 100.0 | 98.8 | probab 91.2 | +7.5 [+2.5, +13.8] | -1.2 [-3.8, +0.0] |
| IM | 74 | 100.0 | 100.0 | 100.0 | probab 91.9 | +8.1 [+2.7, +14.9] | +0.0 [+0.0, +0.0] |
| IS | 20 | 100.0 | 100.0 | 100.0 | probab 70.0 | +30.0 [+10.0, +50.0] | +0.0 [+0.0, +0.0] |
| IT | 105 | 100.0 | 100.0 | 100.0 | probab 65.7 | +34.3 [+25.7, +42.9] | +0.0 [+0.0, +0.0] |
| JE | 75 | 100.0 | 100.0 | 98.7 | probab 94.7 | +4.0 [+0.0, +8.0] | -1.3 [-4.0, +0.0] |
| LI | 59 | 72.9 | 72.9 | 79.7 | probab 72.9 | +6.8 [-5.1, +16.9] | +6.8 [+1.7, +13.6] |
| LT | 35 | 97.1 | 100.0 | 100.0 | probab 80.0 | +20.0 [+8.6, +34.3] | +2.9 [+0.0, +8.6] |
| LU | 67 | 65.7 | 65.7 | 65.7 | probab 73.1 | -7.5 [-19.4, +4.5] | +0.0 [+0.0, +0.0] |
| LV | 36 | 100.0 | 100.0 | 100.0 | probab 69.4 | +30.6 [+16.7, +47.2] | +0.0 [+0.0, +0.0] |
| MC | 36 | 30.6 | 30.6 | 30.6 | probab 36.1 | -5.6 [-19.4, +8.3] | +0.0 [+0.0, +0.0] |
| MT | 75 | 100.0 | 100.0 | 100.0 | probab 89.3 | +10.7 [+4.0, +18.7] | +0.0 [+0.0, +0.0] |
| MX | 51 | 86.3 | 86.3 | 86.3 | probab 78.4 | +7.8 [-3.9, +19.6] | +0.0 [+0.0, +0.0] |
| NL | 82 | 64.6 | 64.6 | 64.6 | probab 67.1 | -2.4 [-13.4, +7.3] | +0.0 [+0.0, +0.0] |
| NO | 51 | 94.1 | 94.1 | 94.1 | probab 68.6 | +25.5 [+11.8, +41.2] | +0.0 [+0.0, +0.0] |
| PL | 62 | 71.0 | 71.0 | 71.0 | probab 74.2 | -3.2 [-14.5, +8.1] | +0.0 [+0.0, +0.0] |
| PT | 81 | 100.0 | 100.0 | 100.0 | probab 81.5 | +18.5 [+9.9, +27.2] | +0.0 [+0.0, +0.0] |
| RO | 47 | 97.9 | 97.9 | 95.7 | probab 76.6 | +19.1 [+6.4, +31.9] | -2.1 [-6.4, +0.0] |
| SE | 31 | 100.0 | 100.0 | 100.0 | probab 83.9 | +16.1 [+3.2, +29.0] | +0.0 [+0.0, +0.0] |
| SI | 48 | 91.7 | 91.7 | 91.7 | probab 47.9 | +43.8 [+29.2, +58.3] | +0.0 [+0.0, +0.0] |
| SK | 62 | 100.0 | 100.0 | 100.0 | probab 61.3 | +38.7 [+27.4, +50.0] | +0.0 [+0.0, +0.0] |
| SM | 74 | 100.0 | 100.0 | 100.0 | probab 89.2 | +10.8 [+5.4, +18.9] | +0.0 [+0.0, +0.0] |
| US | 203 | 79.3 | 88.7 | 87.7 | probab 90.2 | -2.5 [-7.4, +2.5] | +8.4 [+4.9, +12.8] |
| VA | 21 | 100.0 | 100.0 | 100.0 | probab 81.0 | +19.1 [+4.8, +38.1] | +0.0 [+0.0, +0.0] |


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
