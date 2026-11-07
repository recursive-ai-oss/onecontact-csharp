# Extraction: measured rows and span-head history

Maintainer notes. The user documentation is `docs/extraction.md`.

## The web gate

On a web page the model can be let in through a gate (`TextFieldReader.ModelOnWeb`, internal, off). With the gate on, the model reads a page, but only its people and organizations count, only within three lines of an email, phone or address the rules found in the same container, heading and landmark, and only at probability 0.6 or more. On development blocks wrapped as pages (3,136 pages, value F1) the gate raises person from 92.5 to 96.5 and company from 75.6 to 77.4. On the real pages it does not help, so it is off. Both configurations were scored once each, next to the version before these rules (rules and markup on pages, the span model on text):

| Row (value F1, visible-text mentions unless noted) | Before | Rules, gate off (shipped) | Rules, gate on | Best reference |
|---|---|---|---|---|
| web person | 0 | 19.4 [4.6, 34.4] | 17.9 [4.4, 32.4] | spaCy 8.4 [3.7, 14.0] |
| web org | 0 | 35.9 [26.9, 44.9] | 35.7 [26.8, 44.6] | spaCy 11.0 [8.3, 14.5] |
| web address, phone, email, website | 84.0, 99.0, 98.9, 58.2 | same | same | none, 99.0, 92.2, 58.2 |
| web record F1 | 30.3 [20.8, 39.3] | 25.4 (−4.9 [−12.7, +2.4]) | 24.1 (−6.2 [−13.7, +1.0]) | none |
| web person, all mentions | 12.8 | 19.6 (+6.8 [−6.6, +21.1]) | 18.6 | spaCy 8.4 |
| web org, all mentions | 59.2 [49.6, 67.6] | 54.5 (−4.6 [−12.8, +6.0]) | 54.3 | spaCy 11.0 |
| web record F1, all mentions | 37.9 [28.7, 46.6] | 34.1 (−3.8 [−9.8, +2.0]) | 33.0 (−4.9 [−10.8, +0.6]) | none |
| text person | 18.8 [0, 38.1] | 27.8 [6.9, 48.3] (+9.0 [−2.0, +23.6]) | same | spaCy 61.3 [45.8, 75.8] |
| text org | 17.2 [4.4, 30.2] | 55.6 [40.0, 68.7] (+38.3 [+21.8, +55.4]) | same | spaCy 32.9 [21.3, 46.4] |
| text address, phone, email, website | 71.7, 100, 100, 34.5 | same | same | none, 100, 96.6, 34.5 |
| text record F1 | 25.3 [13.6, 37.0] | 31.1 [18.9, 43.0] (+5.8 [−3.3, +15.0]) | same | none |

The gate adds no people and organizations the rules did not find and costs record F1 (its extra names split records), so it stays off. Person and organization rows on web pages go from zero to above spaCy's transformer, and text organizations to above it; text people remain well below it (recall 25 against 95). No per-type row falls beyond its interval. The pooled value F1 over all six types of the web pages (visible-text mentions) falls 6.3 points [−9.5, −2.9] (162 more predicted values, mostly organizations at precision 29): that pooled number is not a gated row, and it comes from rows that were zero.

## The span model on real pages

Scored on the private extraction evaluation sets (`extraction-eval-sets.md`: 119 pages, 115 text items; visible-text mentions, value F1, 1000-resample bootstrap 95% interval, aggregates only) by `tools/Eval/extraction/run-model-eval.sh`, once per candidate. The first head (trained on blocks that were mostly contact data) read ordinary page text as names, companies and addresses: on web pages it predicted 1,136 persons for 15 gold and 1,503 organizations for 69, address F1 fell from 84.0 to 42.2 and record F1 from 30.3 to 7.5.

The retrained head learns from blocks inside pages: `tools/data/blocks` renders each contact block between unlabelled page chrome (navigation, headings, product and brand names, prose, testimonials, news, bylines, forms, cookie banners, legal lines), plus pages with no contact block and look-alike lines (roads and exits, service areas, `logo@2x.png`, social handles). All of that text is ours, written for the generator; the held-out fifth of every list is used by dev blocks only.

| Set, type | Rules only | Head v1 (cut 0.3) | v2 data (cut 0.4) | v3 data (cut 0.4), all fields | v3, text only (shipped) | Best reference |
|---|---|---|---|---|---|---|
| web person | 0 | 1.6 | 4.0 | 8.0 [2.3, 14.5] | 0 (not used) | spaCy 8.4 |
| web org | 0 | 4.7 | 20.1 | 16.0 [9.0, 23.0] | 0 (not used) | spaCy 11.0 |
| web address | 84.0 [76.6, 90.3] | 42.2 | 76.2 | 76.7 [67.1, 85.3] | 84.0 | none |
| web email | 98.9 | 98.3 | 97.8 | 98.3 | 98.9 | Recognizers 92.2 |
| web record F1 | 30.3 [20.8, 39.3] | 7.5 | 26.6 | 23.7 [16.0, 30.4] (−6.6 [−12.5, −1.2]) | 30.3 | none |
| text person | 0 | 9.2 | 11.4 | 18.8 [0, 38.1] | 18.8 | spaCy 61.3 |
| text org | 0 | 15.6 | 12.7 | 17.2 [4.4, 30.2] | 17.2 | spaCy 32.9 |
| text address | 46.2 [24.4, 64.0] | 68.9 | 74.1 | 71.7 [53.9, 88.5] | 71.7 | none |
| text phone, email, website | 100, 100, 34.5 | same | same | same | same | |
| text record F1 | 22.8 [11.5, 34.8] | 15.6 | 25.3 | 25.3 [13.6, 37.0] | 25.3 [−3.8, +9.8 vs rules] | none |

- **Text:** no row is below the rules, address recall goes from 31 to 66 at 79 precision, and the model finds people and organizations the rules cannot. Overall value F1 on text 66.2 → 67.9 (+1.7 [−2.4, +5.7]). Names and organizations stay far from spaCy's recall (person recall 15 against 95): the head is conservative, which is what keeps precision at 25.
- **Web:** even with realistic chrome the head's addresses (precision 68 against the rules' 95) and its names and organizations (precision 4 and 13) lower the record row beyond noise (−6.6 [−12.5, −1.2]) and the overall value F1 (−17). It is therefore not used on web pages: the model runs on plain text only, and a web page is read by the markup and the rules, identical to rules-only (shipped configuration rescored: every row +0.00). The web restriction was chosen after the first two candidates' web rows were seen, as a structural choice (rule precision against model precision), not a threshold.
- Address-field and name rows on the Tier 1 address and name sets are byte-identical to before (the encoder is frozen).


## Development-block rows

Development-block rows (`blocks-us-ca-v3`, 6,000 dev blocks: contact blocks inside pages with navigation, prose, product names and look-alike lines, plus notes and signatures; exact-span field F1 of `ExtractText` with a US default region, rules + model at cut 0.4): address 93.3, company 85.8, person 91.2, phone 96.1, email 94.5, website 76.0. The rules alone find no names or companies without a label; email and website sit below 100 because the dev pages hold look-alike lines the rules take (`logo@2x.png`, `brandname.com`), and the address row is capped the same way by lines such as "Now serving Portland, OR 97201". The cut is the largest from 0.0 to 0.9 whose scores stay within 0.5 points of no cut on every field: 0.4 (0.5 loses 2.5 points on company).

Development rows (`blocks-us-ca-v3` dev, first 5,000 blocks, exact span, rules only): person P 98.1 R 80.7 F1 88.6; company P 80.2 R 70.2 F1 74.9; email 99.4 (94.3 before). With the model added: person 93.9, company 80.2, address 92.7. Before the rules the same rows were person 0 and company 0 without labels.
