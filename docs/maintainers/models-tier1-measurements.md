# Tier 1 model: measured rows and span-head history

Maintainer notes. The user documentation is `docs/models.md`.

## The span head: how it was added

The head was trained on the shared encoder with the encoder **frozen** (`FreezeEncoder`), after the address and name heads: `recipes/tier1/spans.sh` loads the trained pipeline, trains the new head alone with OneNLP's `Train` on the rendered contact blocks (`tools/data/provenance/blocks-us-ca.md`), and saves the pipeline with the head added. The encoder and the three older heads are the same bytes, so the address and name results cannot change: the model's predictions on the private US and CA sets are byte-identical to those of the weights without the head (SHA-256 of the prediction files equal). Retraining the whole encoder jointly was not tried; the frozen head reaches 98.6 field F1 and 94.6 block-start F1 on 18,191 dev blocks, and the dev score peaked at epoch 4 of 6 (0.9666, mean of field F1 and block-start F1 on 5,970 dev blocks) before falling to 0.957, so the best evaluation was kept.

| Dev blocks (18,191) | precision | recall | F1 |
|---|---|---|---|
| address | 98.73 | 98.80 | 98.77 |
| company | 95.83 | 94.78 | 95.30 |
| email | 99.93 | 99.94 | 99.94 |
| person | 97.38 | 96.96 | 97.17 |
| phone | 99.63 | 99.45 | 99.54 |
| website | 99.57 | 99.69 | 99.63 |

Block starts: F1 94.61. The dev blocks are rendered from the same templates as the training blocks (with different people, companies and addresses), so these numbers are optimistic for real pages.

## Measured

Tier 1 model `tier1.onenlp` (9.84 MB), rules plus model, on the private United States and Canada evaluation sets, coarse labels, scorer `tools/Eval/score.py`, paired bootstrap 95% CI (1000 resamples, seed 20261105). Aggregates only. Reference: best of libpostal 1.1 and Senzing (addresses), probablepeople (names).

| Set | Measure | Rules + model | Rules only | Best reference | Difference to reference |
|---|---|---|---|---|---|
| US address (n=229) | field F1 | 97.22 [96.11, 98.12] | 92.01 | libpostal 97.18 | +0.04 [-0.96, +1.08] |
| US address | full parse | 86.46 [82.10, 90.39] | 74.67 | libpostal 86.03 | +0.44 [-2.18, +3.06] |
| CA address (n=231) | field F1 | 92.40 [90.36, 94.14] | 82.24 | libpostal 93.34 | -0.94 [-2.91, +0.85] |
| CA address | full parse | 76.19 [70.56, 81.39] | 56.71 | libpostal 75.32 | +0.87 [-4.76, +5.63] |
| US names | person token (n=96) | 97.98 [95.06, 100.0] | 97.98 | probablepeople 95.97 | +2.02 [-1.23, +5.58] |
| US names | company token (n=107) | 80.72 [73.53, 86.86] | 68.07 | probablepeople 76.81 | +3.92 [-4.82, +12.00] |
| US names | class (n=203) | 86.21 [81.28, 90.64] | 79.31 | probablepeople 90.15 | -3.94 [-9.36, +1.48] |
| CA names | person token (n=126) | 98.57 [96.43, 100.0] | 98.57 | probablepeople 90.00 | +8.57 [+3.94, +13.67] |
| CA names | company token (n=82) | 75.29 [65.98, 83.33] | 64.64 | probablepeople 78.33 | -3.04 [-12.31, +5.84] |
| CA names | class (n=208) | 85.10 [80.29, 89.42] | 80.77 | probablepeople 85.58 | -0.48 [-5.77, +5.29] |

The python-nameparser person token accuracy (US 100.0, CA 99.64) is above ours on person items, within noise of our rows on the US set. The rules' rows are optimistic because their pre-label seeded the adjudication; the model's are not. No several-people items exist in the sets, so that class is not measured.

Candidates scored (same weights; the policy differs): cand1 (model decides rule-unknown names only): US class 80.30, company token 68.67; CA class 82.21, company token 68.44; addresses as cand2 minus the admin-area repair. cand2 (adds the default-person override): US/CA addresses 97.14 / 86.46 and 92.40 / 76.19, US class 86.21, CA class 85.10. cand3 (adds the admin-area repair, shipped): the rows above. Selection used the development sets only; the repair was added after a unit test failed, not after a score.

Indicative speed and size (one run, loaded machine, AMD Ryzen 9 7950X3D, .NET 10.0.14, OneNLP 0.2.0-preview.1, OneModel 1.0.0; 5,000 development texts, not evaluation text): model load plus first call 149 ms; address parse rules only 47,700/s, with model 5,700/s (21 KB allocated per parse); `AddressParser` 1.0.3 3,260/s (regex, US only, a different task: no recipient, no CA). Name parse rules only 29,000/s, with model 10,300/s; NameParserSharp 125,900/s. Peak working set of the timing process 251 MB (it also holds both comparators and the benchmark inputs); the fast tier alone has not been measured separately. Gate rows run at 1.0.
