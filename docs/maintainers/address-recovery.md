# Address rules: recovery measurements

Maintainer notes. Rules-only recovery on register-rendered development examples, before and after each round of rule work. These are tuning history, not behaviour guarantees; the rules themselves are in the `docs/address-rules-*.md` notes.

## Group B: before and after the web-form work

## Before and after the web-form work (all 38 Tier 2 countries; register dev examples, rules alone, 2000 per country or all of them)

Every labelled field recovered (every-field) and share of labelled fields recovered (field), as percent, then refusals and wrong-country reads, before then after. With the country as hint (hint) and without (no hint). "Before" is the tree before this work.

| Country | hint every-field | hint field | hint refused | hint wrong | no hint every-field | no hint field | no hint refused | no hint wrong |
|---|---|---|---|---|---|---|---|---|
| AD | 79.8 → 91.3 | 81.7 → 93.4 | 68 → 25 | 0 → 0 | 79.8 → 91.3 | 81.7 → 93.4 | 68 → 25 | 0 → 0 |
| AT | 83.0 → 83.0 | 90.0 → 90.0 | 117 → 117 | 0 → 0 | 20.7 → 20.7 | 27.8 → 27.8 | 1487 → 1487 | 0 → 0 |
| AU | 90.6 → 90.6 | 91.5 → 91.5 | 188 → 188 | 0 → 0 | 85.9 → 85.9 | 86.7 → 86.7 | 280 → 280 | 12 → 12 |
| BE | 41.9 → 41.9 | 70.7 → 70.7 | 218 → 218 | 0 → 0 | 32.0 → 32.0 | 43.4 → 43.4 | 1022 → 1022 | 6 → 6 |
| CH | 62.4 → 62.4 | 84.2 → 84.2 | 115 → 115 | 0 → 0 | 21.1 → 21.1 | 37.5 → 37.5 | 1232 → 1232 | 3 → 3 |
| CZ | 71.9 → 71.9 | 88.9 → 88.9 | 121 → 121 | 0 → 0 | 33.8 → 33.8 | 47.8 → 47.8 | 1040 → 1040 | 1 → 1 |
| DE | 80.7 → 80.7 | 89.1 → 89.1 | 126 → 126 | 0 → 0 | 59.9 → 59.9 | 66.3 → 66.3 | 615 → 615 | 2 → 2 |
| DK | 93.7 → 93.7 | 95.4 → 95.4 | 105 → 105 | 0 → 0 | 60.9 → 60.9 | 63.1 → 63.1 | 766 → 766 | 1 → 1 |
| EE | 74.4 → 74.4 | 86.8 → 86.8 | 233 → 233 | 0 → 0 | 73.1 → 73.1 | 85.2 → 85.2 | 275 → 275 | 2 → 2 |
| ES | 88.7 → 88.7 | 90.8 → 90.8 | 184 → 184 | 0 → 0 | 25.0 → 25.0 | 27.9 → 27.9 | 1490 → 1490 | 0 → 0 |
| FI | 93.0 → 93.0 | 94.3 → 94.3 | 127 → 127 | 0 → 0 | 82.1 → 82.1 | 83.7 → 83.7 | 342 → 342 | 6 → 6 |
| FR | 68.9 → 68.9 | 83.4 → 83.4 | 175 → 175 | 0 → 0 | 52.3 → 52.3 | 63.4 → 63.4 | 649 → 649 | 11 → 11 |
| GB | 62.6 → 83.7 | 61.5 → 90.8 | 718 → 129 | 0 → 0 | 62.6 → 83.7 | 61.5 → 90.8 | 718 → 129 | 0 → 0 |
| GG | 73.4 → 83.1 | 76.6 → 89.3 | 46 → 19 | 0 → 0 | 73.4 → 83.1 | 76.6 → 89.3 | 46 → 19 | 0 → 0 |
| HR | 61.5 → 61.5 | 85.7 → 85.7 | 152 → 152 | 0 → 0 | 26.8 → 26.8 | 37.7 → 37.7 | 1220 → 1220 | 1 → 1 |
| HU | 92.6 → 92.6 | 94.0 → 94.0 | 128 → 128 | 0 → 0 | 75.6 → 75.6 | 77.4 → 77.4 | 471 → 471 | 0 → 0 |
| IE | 87.0 → 96.4 | 88.5 → 97.8 | 246 → 21 | 0 → 0 | 87.0 → 93.7 | 88.5 → 95.6 | 246 → 77 | 0 → 0 |
| IM | 75.5 → 90.1 | 77.8 → 93.8 | 40 → 9 | 0 → 0 | 75.5 → 90.1 | 77.8 → 93.8 | 40 → 9 | 0 → 0 |
| IS | 84.3 → 84.3 | 93.5 → 93.5 | 103 → 103 | 0 → 0 | 12.7 → 12.7 | 16.8 → 16.8 | 1716 → 1716 | 0 → 0 |
| IT | 83.0 → 84.3 | 91.3 → 92.5 | 144 → 116 | 1 → 1 | 57.8 → 59.1 | 65.3 → 66.5 | 699 → 671 | 31 → 31 |
| JE | 73.6 → 87.7 | 76.4 → 93.2 | 50 → 11 | 0 → 0 | 73.6 → 87.7 | 76.4 → 93.2 | 50 → 11 | 0 → 0 |
| LI | 86.0 → 86.0 | 92.1 → 92.1 | 21 → 21 | 0 → 0 | 82.9 → 82.9 | 88.7 → 88.7 | 31 → 31 | 0 → 0 |
| LT | 93.6 → 93.6 | 95.6 → 95.6 | 95 → 95 | 0 → 0 | 92.8 → 92.8 | 95.0 → 95.0 | 108 → 108 | 3 → 3 |
| LU | 81.1 → 81.1 | 89.7 → 89.7 | 135 → 135 | 0 → 0 | 54.8 → 54.8 | 63.6 → 63.6 | 753 → 753 | 0 → 0 |
| LV | 71.4 → 71.4 | 79.2 → 79.2 | 390 → 390 | 0 → 0 | 71.4 → 71.4 | 79.2 → 79.2 | 390 → 390 | 0 → 0 |
| MC | 79.6 → 79.6 | 87.7 → 87.7 | 12 → 12 | 0 → 0 | 79.6 → 79.6 | 87.7 → 87.7 | 12 → 12 | 0 → 0 |
| MT | 85.8 → 85.8 | 87.4 → 87.4 | 112 → 112 | 0 → 0 | 85.8 → 85.8 | 87.4 → 87.4 | 112 → 112 | 0 → 0 |
| MX | 73.3 → 73.3 | 87.1 → 87.1 | 174 → 174 | 17 → 17 | 67.8 → 67.8 | 80.8 → 80.8 | 312 → 312 | 53 → 53 |
| NL | 90.9 → 90.9 | 94.1 → 94.1 | 112 → 112 | 0 → 0 | 85.8 → 85.8 | 89.0 → 89.0 | 218 → 218 | 0 → 0 |
| NO | 93.4 → 93.4 | 95.2 → 95.2 | 108 → 108 | 0 → 0 | 65.0 → 65.0 | 67.0 → 67.0 | 681 → 681 | 4 → 4 |
| PL | 92.4 → 92.4 | 95.1 → 95.1 | 107 → 107 | 0 → 0 | 89.0 → 89.0 | 92.1 → 92.1 | 162 → 162 | 76 → 76 |
| PT | 89.1 → 91.7 | 90.7 → 93.1 | 195 → 143 | 0 → 0 | 89.1 → 91.7 | 90.7 → 93.1 | 195 → 143 | 0 → 0 |
| RO | 51.3 → 51.3 | 71.1 → 71.1 | 397 → 397 | 0 → 0 | 51.3 → 51.3 | 71.1 → 71.1 | 397 → 397 | 0 → 0 |
| SE | 93.3 → 93.3 | 94.7 → 94.7 | 117 → 117 | 0 → 0 | 62.6 → 62.6 | 64.7 → 64.7 | 734 → 734 | 1 → 1 |
| SI | 89.9 → 89.9 | 93.0 → 93.0 | 149 → 149 | 0 → 0 | 64.6 → 64.6 | 68.9 → 68.9 | 660 → 660 | 0 → 0 |
| SK | 91.3 → 91.3 | 94.4 → 94.4 | 108 → 108 | 0 → 0 | 50.0 → 50.0 | 53.6 → 53.6 | 957 → 957 | 9 → 9 |
| SM | 92.0 → 92.0 | 93.1 → 93.1 | 12 → 12 | 0 → 0 | 92.0 → 92.0 | 93.1 → 93.1 | 12 → 12 | 0 → 0 |
| VA | 96.7 → 96.7 | 96.9 → 96.9 | 2 → 2 | 0 → 0 | 96.7 → 96.7 | 96.9 → 96.9 | 2 → 2 | 0 → 0 |

No row drops by more than 0.5 and no wrong-country count rises. The web forms are not in the register renders, so rows for countries other than the British Isles, Portugal and Italy do not move; the forms are covered by the unit tests.

## Group B: recovery on the register dev examples

## Recovery on the register dev examples (rules alone, with the country as hint; every labelled field recovered; before then after)

| GB | JE | GG | IM | IE | ES | PT | IT | SM | VA | MT | AD |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 8.4 → 62.6 | 0.5 → 73.6 | 1.4 → 73.4 | 0 → 75.5 | 1.6 → 87.0 | 52.0 → 88.7 | 68.7 → 89.1 | 36.7 → 83.0 | 69.9 → 92.0 | 61.7 → 96.7 | 29.4 → 85.8 | 62.4 → 79.8 |

Samples of 2000 (all of them when fewer: JE 220, GG 207, IM 192, SM 163, VA 60, MT 855, AD 367). The GB figure was measured before the inline post town fix above and counts 443 refusals for the county ambiguity. Without a hint, Spanish and Italian text with a five-digit postcode and no province evidence is refused as `address.country-undetermined` unless country inference finds evidence. The remaining refusals are mostly single-line text without a comma between street and locality. Known gaps for the model: county-ambiguous GB lines, typos in road types, Welsh and Gaelic bilingual names, the Italian dependent-locality line.

## C-north: recovery with and without a hint

## Recovery with and without a hint (country inference round)

Rules only, 2000 register-rendered dev examples per country, before and after this round. With a hint nothing moved (the dev registers hold few web forms); the gains are without a hint. Every field is the share of examples where all gold fields are recovered; field is the share of gold fields; refused counts refusals; wrong counts examples read as another country (none rose).

Forms and evidence come from Omniva *Kuidas kirja adresseerida?* (Estonian postcode before the town or county), Posten Norge *Adressering*, Danmarks Adresser *Gengivelse af adresse*, PostNord *Adressering*, and the Wikipedia pages on Czech and Slovak postal codes (first-digit zones, facts only).

| Country | Hint | Every field % | Field % | Refused | Wrong country |
|---|---|---|---|---|---|
| AD | yes | 79.8 -> 79.8 | 81.7 -> 81.7 | 68 -> 68 | 0 -> 0 |
| AD | no | 79.8 -> 79.8 | 81.7 -> 81.7 | 68 -> 68 | 0 -> 0 |
| AT | yes | 83.0 -> 83.0 | 90.0 -> 90.0 | 117 -> 117 | 0 -> 0 |
| AT | no | 20.7 -> 20.7 | 26.1 -> 27.8 | 1530 -> 1487 | 0 -> 0 |
| AU | yes | 90.6 -> 90.6 | 91.5 -> 91.5 | 188 -> 188 | 0 -> 0 |
| AU | no | 85.9 -> 85.9 | 86.7 -> 86.7 | 280 -> 280 | 12 -> 12 |
| BE | yes | 41.9 -> 41.9 | 70.7 -> 70.7 | 218 -> 218 | 0 -> 0 |
| BE | no | 10.6 -> 32.0 | 22.4 -> 43.4 | 1516 -> 1022 | 6 -> 6 |
| CH | yes | 62.4 -> 62.4 | 84.2 -> 84.2 | 115 -> 115 | 0 -> 0 |
| CH | no | 21.1 -> 21.1 | 37.5 -> 37.5 | 1232 -> 1232 | 3 -> 3 |
| CZ | yes | 71.9 -> 71.9 | 88.9 -> 88.9 | 121 -> 121 | 0 -> 0 |
| CZ | no | 10.2 -> 33.8 | 15.9 -> 47.8 | 1718 -> 1040 | 1 -> 1 |
| DE | yes | 80.7 -> 80.7 | 89.1 -> 89.1 | 126 -> 126 | 0 -> 0 |
| DE | no | 58.6 -> 59.9 | 62.1 -> 66.3 | 722 -> 615 | 2 -> 2 |
| DK | yes | 93.7 -> 93.7 | 95.4 -> 95.4 | 105 -> 105 | 0 -> 0 |
| DK | no | 13.1 -> 60.9 | 16.0 -> 63.1 | 1730 -> 766 | 2 -> 1 |
| EE | yes | 74.4 -> 74.4 | 86.6 -> 86.8 | 242 -> 233 | 0 -> 0 |
| EE | no | 10.3 -> 73.1 | 14.1 -> 85.2 | 1755 -> 275 | 2 -> 2 |
| ES | yes | 88.7 -> 88.7 | 90.8 -> 90.8 | 184 -> 184 | 0 -> 0 |
| ES | no | 19.6 -> 25.0 | 22.3 -> 27.9 | 1603 -> 1490 | 0 -> 0 |
| FI | yes | 93.0 -> 93.0 | 94.3 -> 94.3 | 127 -> 127 | 0 -> 0 |
| FI | no | 12.7 -> 82.1 | 15.6 -> 83.7 | 1739 -> 342 | 6 -> 6 |
| FR | yes | 68.9 -> 68.9 | 83.4 -> 83.4 | 175 -> 175 | 0 -> 0 |
| FR | no | 52.3 -> 52.3 | 63.4 -> 63.4 | 649 -> 649 | 11 -> 11 |
| GB | yes | 62.6 -> 62.6 | 61.5 -> 61.5 | 718 -> 718 | 0 -> 0 |
| GB | no | 62.6 -> 62.6 | 61.5 -> 61.5 | 718 -> 718 | 0 -> 0 |
| GG | yes | 73.4 -> 73.4 | 76.6 -> 76.6 | 46 -> 46 | 0 -> 0 |
| GG | no | 73.4 -> 73.4 | 76.6 -> 76.6 | 46 -> 46 | 0 -> 0 |
| HR | yes | 61.5 -> 61.5 | 85.7 -> 85.7 | 152 -> 152 | 0 -> 0 |
| HR | no | 24.4 -> 26.8 | 34.1 -> 37.7 | 1297 -> 1220 | 1 -> 1 |
| HU | yes | 92.6 -> 92.6 | 94.0 -> 94.0 | 128 -> 128 | 0 -> 0 |
| HU | no | 72.1 -> 75.6 | 74.0 -> 77.4 | 541 -> 471 | 0 -> 0 |
| IE | yes | 87.0 -> 87.0 | 88.5 -> 88.5 | 246 -> 246 | 0 -> 0 |
| IE | no | 87.0 -> 87.0 | 88.5 -> 88.5 | 246 -> 246 | 0 -> 0 |
| IM | yes | 75.5 -> 75.5 | 77.8 -> 77.8 | 40 -> 40 | 0 -> 0 |
| IM | no | 75.5 -> 75.5 | 77.8 -> 77.8 | 40 -> 40 | 0 -> 0 |
| IS | yes | 84.3 -> 84.3 | 93.5 -> 93.5 | 103 -> 103 | 0 -> 0 |
| IS | no | 12.7 -> 12.7 | 16.8 -> 16.8 | 1716 -> 1716 | 0 -> 0 |
| IT | yes | 83.0 -> 83.0 | 91.3 -> 91.3 | 144 -> 144 | 1 -> 1 |
| IT | no | 53.9 -> 57.8 | 60.5 -> 65.3 | 811 -> 699 | 31 -> 31 |
| JE | yes | 73.6 -> 73.6 | 76.4 -> 76.4 | 50 -> 50 | 0 -> 0 |
| JE | no | 73.6 -> 73.6 | 76.4 -> 76.4 | 50 -> 50 | 0 -> 0 |
| LI | yes | 86.0 -> 86.0 | 92.1 -> 92.1 | 21 -> 21 | 0 -> 0 |
| LI | no | 42.6 -> 82.9 | 50.5 -> 88.7 | 142 -> 31 | 0 -> 0 |
| LT | yes | 93.6 -> 93.6 | 95.6 -> 95.6 | 95 -> 95 | 0 -> 0 |
| LT | no | 85.7 -> 92.8 | 87.7 -> 95.0 | 254 -> 108 | 3 -> 3 |
| LU | yes | 81.1 -> 81.1 | 89.7 -> 89.7 | 135 -> 135 | 0 -> 0 |
| LU | no | 52.9 -> 54.8 | 62.2 -> 63.6 | 793 -> 753 | 0 -> 0 |
| LV | yes | 71.4 -> 71.4 | 79.2 -> 79.2 | 390 -> 390 | 0 -> 0 |
| LV | no | 71.4 -> 71.4 | 79.2 -> 79.2 | 390 -> 390 | 0 -> 0 |
| MC | yes | 79.6 -> 79.6 | 87.7 -> 87.7 | 12 -> 12 | 0 -> 0 |
| MC | no | 79.6 -> 79.6 | 87.7 -> 87.7 | 12 -> 12 | 0 -> 0 |
| MT | yes | 85.8 -> 85.8 | 87.4 -> 87.4 | 112 -> 112 | 0 -> 0 |
| MT | no | 85.8 -> 85.8 | 87.4 -> 87.4 | 112 -> 112 | 0 -> 0 |
| MX | yes | 73.3 -> 73.3 | 87.1 -> 87.1 | 174 -> 174 | 17 -> 17 |
| MX | no | 67.8 -> 67.8 | 80.8 -> 80.8 | 312 -> 312 | 53 -> 53 |
| NL | yes | 90.9 -> 90.9 | 94.1 -> 94.1 | 112 -> 112 | 0 -> 0 |
| NL | no | 85.8 -> 85.8 | 89.0 -> 89.0 | 218 -> 218 | 0 -> 0 |
| NO | yes | 93.4 -> 93.4 | 95.2 -> 95.2 | 108 -> 108 | 0 -> 0 |
| NO | no | 13.3 -> 65.0 | 16.0 -> 67.0 | 1728 -> 681 | 4 -> 4 |
| PL | yes | 92.4 -> 92.4 | 95.1 -> 95.1 | 107 -> 107 | 0 -> 0 |
| PL | no | 87.5 -> 89.0 | 90.6 -> 92.1 | 192 -> 162 | 76 -> 76 |
| PT | yes | 89.1 -> 89.1 | 90.7 -> 90.7 | 195 -> 195 | 0 -> 0 |
| PT | no | 89.1 -> 89.1 | 90.7 -> 90.7 | 195 -> 195 | 0 -> 0 |
| RO | yes | 51.3 -> 51.3 | 71.1 -> 71.1 | 397 -> 397 | 0 -> 0 |
| RO | no | 51.3 -> 51.3 | 71.1 -> 71.1 | 397 -> 397 | 0 -> 0 |
| SE | yes | 93.3 -> 93.3 | 94.7 -> 94.7 | 117 -> 117 | 0 -> 0 |
| SE | no | 12.0 -> 62.6 | 14.6 -> 64.7 | 1757 -> 734 | 1 -> 1 |
| SI | yes | 89.9 -> 89.9 | 93.0 -> 93.0 | 149 -> 149 | 0 -> 0 |
| SI | no | 29.0 -> 64.6 | 32.8 -> 68.9 | 1394 -> 660 | 0 -> 0 |
| SK | yes | 91.3 -> 91.3 | 94.4 -> 94.4 | 108 -> 108 | 0 -> 0 |
| SK | no | 13.5 -> 50.0 | 16.9 -> 53.6 | 1716 -> 957 | 9 -> 9 |
| SM | yes | 92.0 -> 92.0 | 93.1 -> 93.1 | 12 -> 12 | 0 -> 0 |
| SM | no | 92.0 -> 92.0 | 93.1 -> 93.1 | 12 -> 12 | 0 -> 0 |
| VA | yes | 96.7 -> 96.7 | 96.9 -> 96.9 | 2 -> 2 | 0 -> 0 |
| VA | no | 96.7 -> 96.7 | 96.9 -> 96.9 | 2 -> 2 | 0 -> 0 |

## C-south: web forms of Hungary, Romania and Australia

Recovery on the register dev rows (2000 per country, rules alone; every labelled field recovered / labelled fields recovered; main before the change and after, same rows, same tool):

| Country | Hint | Every field before | after | Fields before | after | Refused before | after |
|---|---|---|---|---|---|---|---|
| HU | yes | 92.6 | 92.6 | 94.0 | 94.0 | no-last-line 128 | no-last-line 128 |
| HU | no | 45.2 | 72.1 | 47.8 | 74.0 | country-undetermined 955, no-last-line 131 | country-undetermined 410, no-last-line 131 |
| RO | yes | 50.2 | 51.3 | 68.8 | 71.1 | model-required 21, no-last-line 435 | model-required 19, no-last-line 378 |
| RO | no | 50.2 | 51.3 | 68.8 | 71.1 | no-last-line 456 | no-last-line 397 |
| AU | yes | 90.6 | 90.6 | 91.5 | 91.5 | country-conflict 4, model-required 24, no-last-line 160 | unchanged |
| AU | no | 85.9 | 85.9 | 86.7 | 86.7 | country-undetermined 4, no-last-line 276 | unchanged |

The register rows mostly lack what the web forms add (their refusals are texts with no postcode, a postcode with no locality, or a glued recipient), so the table moves little; the new forms are covered by the unit tests. No other Tier 2 country changed on its dev rows.

## Country inference rounds

## Measurement

The 2,000-line register dev renders per country (`tools/Eval/Tier2Recovery`, without a hint), every-field share and refusals before and after: Austria 20.7 to 80.8 percent (1,487 to 165 refusals), Switzerland 21.1 to 61.0, Denmark 60.9 to 89.9, Norway 65.0 to 89.2, Slovenia 64.6 to 85.5, Luxembourg 54.8 to 79.4, Croatia 26.8 to 50.5, Slovakia 50.0 to 71.7, France 52.3 to 68.3, Germany 59.9 to 72.3, Hungary 79.1 to 89.7, Italy 59.1 to 67.4. No country with a hint changes and the wrong-country counts do not rise.

Partial agreement, language candidates and model reads, on the same dev renders (no hint; every-field share, refusals): Spain 25.0 to 69.3 percent (1,154 to 125 refusals), Czech Republic 52.0 to 70.4 (585 to 132), Slovakia 71.7 to 86.8 (509 to 129), Sweden 64.2 to 90.2 (699 to 118), Croatia 50.5 to 58.8 (608 to 152), Finland 84.9 to 88.6; no country's every-field share fell by more than 0.5 points, and wrong-country counts rose only for Finland (6 to 7) and Croatia (1 to 2).

**Happy path and engine.** The partial reading above is what the low-level engines return (`RuleAddressParser.TryParse` and the status-returning `PostalAddress.TryParse(span, out value, out detail)`). The happy-path `PostalAddress.Parse`/`TryParse` and `Contacts.Normalize` treat it as a failure: they return no value and report `address.country-undetermined`, because a value with no country cannot be rendered in an authority's style or checked. Give a country (`PostalAddress.Parse(text, region)`) or read through the engine.

## US and CA: where rules alone fall short on training examples

## Where rules alone fall short

The library's rule parser run over about 5,000 sampled training examples per country (information for the model recipe; the parser reads the example text with no hint, and a field counts as recovered when it returns the same label on the same span):

| | US | CA |
|---|---|---|
| Every labelled field recovered | 66.3 % | 55.2 % |
| Fields recovered | 90.9 % | 81.7 % |
| Parse refused (`address.model-required`) | 84 (1.7 %) | 135 (2.7 %) |
| Parse refused (`address.no-last-line`) | 59 | 464 |

Weakest labels (fields recovered / seen): US `street.name` 76.5 %, `street.military-*` 66.7 %, `street.route-designator` 67.9 %, `organization` 69.5 %; CA `street.unit-number` 48.6 %, `street.name` 62.3 %, `street.route-designator` 57.8 %, `organization` 68.4 %. Recipient and organisation lines are expected misses in single-line text: the rules cannot tell a name from the rest without line breaks. The numbers include the deliberate typing and OCR noise (6 % of examples), spacing damage, and layouts the rules decline to read, so they are a floor for what the model must add, not a score of the parser.

## C-north: Lithuania round

Measured on the 2000 register dev examples (rules alone). The recovery tool keeps `LT-` inside the postal-code field for Lithuania, as the register does (the other countries' registers label the letters apart); both columns use it.

| Hint | Every field before → after | Fields before → after | Top refusals after |
|---|---|---|---|
| yes | 58.5 → 93.6 | 82.3 → 95.6 | no-last-line 91 (no postcode), model-required 4 |
| no | 53.7 → 85.7 | 75.6 → 87.7 | country-undetermined 159, no-last-line 94 |

## C-south: recovery rounds

Measured on the register dev examples (2000 per country, rules alone, with the country as hint; every labelled field recovered):


| Country | Rules alone, with hint: before | after | Refused to the model |
|---|---|---|---|
| CZ | 71.2 | 71.2 | no-last-line 104 (no postcode or no locality), model-required 17 |
| HR | 61.5 | 61.5 | no-last-line 102, model-required 50 |
| RO | 16.0 | 50.2 | no-last-line 435, model-required 21 |
| HU | 92.6 | 92.6 | no-last-line 128 |
| AU | 56.8 | 90.6 | no-last-line 160, model-required 24, country-conflict 4 |

Without a hint the Czech, Croatian and Hungarian forms are shared with other countries, so text is refused as `address.country-undetermined` unless country inference finds evidence (see the north rules, "Country inference"; the figures here were measured before it); Romania and Australia read without a hint as well as with it.

### Second recovery round: Mexico, Poland, Slovenia, Slovakia

Same measurement (2000 register dev examples per country, rules alone). Both columns come from the same recovery tool with the same gold conventions: the marker word of a number (`Núm.`) counts with the house number as `No.` does. Before is the code without this round; "every" is every labelled field recovered, "fields" the share of labelled fields recovered.

| Country | Hint | Every field before → after | Fields before → after | Top refusals after |
|---|---|---|---|---|
| MX | yes | 4.7 → 73.3 | 42.5 → 87.1 | no-last-line 140 (no postcode), model-required 29, country-conflict 5 |
| MX | no | 4.0 → 67.8 | 39.5 → 80.8 | no-last-line 134, country-undetermined 131, model-required 42 |
| PL | yes | 72.3 → 92.4 | 81.0 → 95.1 | no-last-line 107 |
| PL | no | 75.1 → 87.5 | 84.3 → 90.6 | no-last-line 125, country-undetermined 65 |
| SI | yes | 78.5 → 89.9 | 90.2 → 93.0 | no-last-line 143, model-required 6 |
| SI | no | 26.2 → 29.0 | 32.1 → 32.8 | country-undetermined 1249, no-last-line 145 |
| SK | yes | 89.3 → 91.3 | 93.7 → 94.4 | no-last-line 108, model-required 4 |
| SK | no | 13.2 → 13.5 | 16.8 → 16.9 | country-undetermined 1592, no-last-line 123 |
