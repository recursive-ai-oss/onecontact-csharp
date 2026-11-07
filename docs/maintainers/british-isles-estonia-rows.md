# British Isles and Estonian address rows: measurements

Field F1 and full-parse accuracy (coarse labels) on the frozen real-page sets for the Isle of Man, Ireland, Great Britain, Jersey, Guernsey and Estonia, with the shipped `.Models` weights and the rules before and after the rule changes in `docs/address-rules-tier2-b.md` and `docs/address-rules-tier2-c-north.md`. Aggregates only; paired bootstrap 95% intervals of the difference (1000 resamples, the scorer's fixed seed). Each set was scored once before and once after the changes.

| Country | Mode | Field F1 before | after | Difference [95% CI] | Full parse before | after | Best reference field F1 (libpostal 1.1.4 / Senzing) |
|---|---|---|---|---|---|---|---|
| IM (n=59) | no hint | 86.37 | 86.37 | 0.00 [0.00, 0.00] | 64.41 | 64.41 | 86.56 / 89.05 |
| IM | hinted | 85.77 | 90.46 | +4.69 [+1.05, +8.90] | 64.41 | 71.19 | |
| IE (n=92) | no hint | 67.32 | 73.34 | +6.02 [+2.88, +9.43] | 30.43 | 41.30 | 68.19 / 58.20 |
| IE | hinted | 74.35 | 74.77 | +0.42 [0.00, +1.61] | 43.48 | 43.48 | |
| GB (n=142) | both | 83.28 | 83.28 | 0.00 | 61.27 | 61.27 | 83.14 / 83.22 |
| JE (n=51) | no hint | 83.75 | 83.99 | +0.24 [0.00, +0.75] | 50.98 | 50.98 | 82.92 / 73.75 |
| JE | hinted | 83.75 | 85.06 | +1.31 [0.00, +3.66] | 50.98 | 52.94 | |
| GG (n=24) | no hint | 75.22 | 75.22 | 0.00 | 41.67 | 41.67 | 48.11 / 78.95 |
| GG | hinted | 75.22 | 76.11 | +0.88 [0.00, +2.73] | 41.67 | 45.83 | |
| EE (n=91) | no hint | 86.00 | 85.96 | -0.03 [-0.12, 0.00] | 62.64 | 62.64 | 89.35 / 90.71 |
| EE | hinted | 88.59 | 88.97 | +0.38 [0.00, +1.14] | 67.03 | 68.13 | |

Against libpostal after the change: IM hinted +3.90 [-1.47, +8.90], IE no hint +5.15 [-0.72, +11.51], EE hinted -0.38 [-3.90, +2.93]. **EE without a hint stays below libpostal** (-3.38 [-6.69, -0.26]); its hinted reading is within the interval, so the gap is in the readings the rules refuse without a hint and the model completes (state recall 3 of 14 gold states, house-number recall 83 against 91).

Differentials against the previous build: the first 300 lines of every register dev render (all Tier 2 and Tier 1 groups, hinted and no hint, 75,621 reads) are identical, keys included; the extraction outputs (119 pages, 115 texts, with and without the model) are identical.

Machine: the evaluation worker, .NET 10.0.403, OneNLP 0.2.0-preview.1, OneModel 1.0.0; weights `models.onenlp` sha256 c5cedca0d4d0991f272cdafa28471a7744423bd65e26f0dc5970dafc2ee9640d; libpostal 1.1.4 and Senzing parser 1.2.0 predictions as stored with the sets.
