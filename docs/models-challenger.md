# Address challenger models (OpenStreetMap data)

Two address-field models trained on the converted OpenStreetMap address sets (ODbL, `docs/training-data-address-challenger.md`), built to be measured against the shipped `RecursiveAI.OneContact.Models` weights. **Status: the full candidate built on the C2 mix (last section) is now the shipped default of `RecursiveAI.OneContact.Models`** (weights `c5cedca0…`, MIT OR Apache-2.0, © OpenStreetMap contributors, ODbL 1.0 credited in the package notices). The two address-only models C1 and C2 below stay unpacked measurements. The permissive-only weights remain reproducible: `run.sh` (and `names.sh`, `spans.sh`) never read the OpenStreetMap folder, `data.manifest.tsv` names no OpenStreetMap file, and the loader reads OpenStreetMap data only when `--osm` is given.

## Models

Same pipeline shape as the default (`src/OneContact.Models/SupersetPipeline.cs`: width 128, depth 3, embedding 64, the superset tables), trained with OneNLP's `Train` on OneModel.Training through `recipes/models/challenger.sh` (the `train` command with `--osm`). Only the address head gets address examples; 10,000 name examples from each default name set keep the two name heads valid so the file loads. The name heads are not scored and play no part in choosing an epoch; address reading never calls them.

| Candidate | Training examples | Epochs |
|---|---|---|
| **C1** | OpenStreetMap only: target 2,000,000 over 50 country files (1,375,146 after the per-file cap and the hash selection) | 4 |
| **C2a** (reported as C2) | 1.2M target (about 0.8M) OpenStreetMap + the default recipe's register-rendered address sets (about 1.09M: 925,000 shared by the Tier 2 files, 80,000 each for US, CA and the territories) | 4 |
| C2b (dev only) | 0.4M target (about 0.27M) OpenStreetMap + the same register-rendered sets | 4 |

Labels: the converted sets write a street as `street.road` and keep official districts apart; the loader writes `street.road` as `street.name` and merges `dependent-locality.district` into `dependent-locality` and `admin-area.district` into `admin-area`. Nothing else is changed. A C1 model therefore reads a street as one name span (type inside it), as the OpenStreetMap labels do; C2 sees both conventions.

Recipe: seed 1, learning rate 0.002, 300 warm-up steps, batches of about 1,500 words, dropout 0.1, patience 6 (the default recipe's constants), checkpoints every 500 steps, 8 threads, chunks of 1,380 s that resume from `last.ckpt`. Selection is on dev only: a dev set of 20,000 OpenStreetMap dev examples (hash-fixed split of the converted set) plus the register sets' dev (14,000 shared by the Tier 2 files, 1,500 each for US, CA and the territories), the same for every candidate, scored as address field F1 over all of it.

| Run | Chunks | Steps | Dev F1 by epoch (best kept) | Weights |
|---|---|---|---|---|
| C1 | 2 | 27,292 | 0.7554, 0.7654, 0.7695, **0.7710** | 22,419,884 B, sha256 `a098c079…1830` |
| C2a | 3 | 45,272 | 0.9595, 0.9693, 0.9740, **0.9763** | 22,431,177 B, sha256 `c77004db…5a` |
| C2b | 3 | 34,724 | 0.9386, 0.9583, 0.9648, **0.9693** | 22,431,177 B, sha256 `d22817ed…e8` |

C1's mixed-dev score is low because half the dev set is register-rendered text in the register label convention, which C1 never saw; the split dev scores show it (address field F1 on each dev part, `recipes/models/challenger.sh` runs and `recipes/models/Program.cs dev`):

| Model | OpenStreetMap dev (14,162) | Register dev (16,864) |
|---|---|---|
| default (`models.onenlp`, adde4a66…) | 0.5635 (its street labels differ: type and name apart) | 0.9945 |
| C1 | 0.9773 | 0.6502 |
| C2a | 0.9588 | 0.9863 |
| C2b | 0.9318 | 0.9907 |

**Mix choice (dev only):** C2a (more OpenStreetMap) beat C2b on the mixed dev (0.9763 against 0.9693) and on the mean of the two parts (0.9726 against 0.9613), so C2a is the C2 reported on the real sets; C2b was not scored there. Dev needed 2 to 3 chunks of about 23 minutes (8 cores, a loaded machine) per candidate: CPU training is practical, so no GPU plan is proposed.

## Real-page rows

The shipped default, C1 and C2 each read every frozen private real address set (Tier 1: US, CA; Tier 2: groups A, B, C-north and C-south, 40 country rows), rules first and the model where the rules leave a decision open, with the country as hint and without. Each candidate was scored once. `tools/Eval/run-challenger.sh` writes the predictions and `tools/Eval/score.py` the paired bootstrap (1000 resamples over items, seed 20261105; `tools/Eval/challenger-summary.py` the table). The full table with confidence intervals for every system, country, mode and metric, field F1 and full-parse accuracy, is `recipes/models/challenger-results.tsv`; only aggregates are kept in the repository.

Difference to the default (paired 95% interval excluding zero counted as a gain or a loss):

| Row | C1 gains | C1 losses | C2 gains | C2 losses |
|---|---|---|---|---|
| field F1, hinted (40) | CH DE DK EE GB HR HU IE IM LI | ES (−4.94), IT (−1.21) | AD AT DE GB HU IE IM MT MX | none |
| field F1, no hint (40) | DE EE GB HR HU IE IM LI | IT (−4.04) | AD DE EE GB HR HU IE IM MT MX | none |
| full parse, hinted (40) | HR HU | none | GB IE | none |
| full parse, no hint (40) | EE HR HU | AU (−4.88), IT (−6.67) | EE GB IE | none |

Largest C2 gains in field F1 (hinted): IM +8.27, IE +7.93, HU +6.33, MX +6.53 (n=27), MT +5.01, AD +4.70, GB +4.54. US and CA are unchanged within noise (C2 −0.09 [−0.51, +0.33], +0.44 [0.00, +1.03]). The only C2 row with an interval close to excluding zero on the wrong side is LT (n=19, −2.42 [−7.27, 0.00], hinted).

**Reading.** C2 never loses significantly and gains in the countries where the register data is thin or synthetic (GB, IE, IM, MT, HU, MX, AD, DE), which is what OpenStreetMap should add; C1 gains in the same places but loses on ES and IT, where only the mixed training keeps the register conventions. Gains show only where the rules refuse and the model reads; where the rules decide (most of US, CA, NL, SE), nothing moves. The gold of these sets was labelled over the rules' pre-labels, so rows where the rules already agree are flattering to both models.

## Not measured

* The raw model (without the rules) on the real sets.
* Further seeds: one seed per candidate; intervals are over items, not over training runs.
* The licence question for shipping: ODbL §4.4 (share-alike if the weights count as a derivative database rather than a produced work) is not settled here.
* The libpostal and Senzing rows with the country given (a separate piece of work).

## Rebuilding

```
dotnet build -c Release recipes/models
OSM=<osm-lp-v1 folder> ADDRESSES=<register sets folder> NAMES_US_CA=<names-us-ca-v1> NAMES_TIER2=<names-tier2-v1> OUT=<run folder> \
  OSM_COUNT=2000000 recipes/models/challenger.sh                                         # C1, repeat until it prints model=...
  ... TRAIN_PERMISSIVE=1 OSM_COUNT=1200000 recipes/models/challenger.sh                   # C2a
MODELS_DLL=<tools/Eval/Models build> DEFAULT_MODEL=... C1_MODEL=... C2_MODEL=... tools/Eval/run-challenger.sh <set folder> <out folder>
```

Provenance: `recipes/models/PROVENANCE-challenger.md`.

## Full candidate on the C2 mix (encoder, address, name and contact-span heads)

A complete `.Models` candidate built on the C2 address mix, scored once against the shipped weights (`0e31f3fd…`) on the rules of the same checkout. It is the shipped default, accepted with one address row that loses beyond its interval (below, stated plainly).

**Build (all three stages `recipes/models`, seed 1, CPU, 8 threads, chunks of 1,380 s that resume from the last checkpoint):**

| Stage | Command | Data | Result |
|---|---|---|---|
| Encoder + address head (+ name heads, jointly, as `run.sh` does) | `challenger.sh` with `NAMES_JOINT=1 NAMES_COUNT=250000 TRAIN_PERMISSIVE=1 OSM_COUNT=1200000`, 4 epochs | 1.2M OSM + register sets (925,000 shared by the Tier 2 files, 80,000 each US, CA, territories) + 250,000 + 250,000 name examples | 2,394,521 examples, 3 chunks, 50,467 steps, best dev 0.9839 (epoch 3), sha256 `0482a535…` |
| Name heads, encoder frozen | `names.sh`, `NAMES2_COUNT=100000`, 4 epochs | `names-us-ca-v1` 250,000; `names-tier2-2` 100,000; Tier 2 companies 123,948 | 1 chunk of 40 s, best dev 0.9781 (epoch 3), sha256 `3ef21c6e…` |
| Contact-span head, encoder frozen | `spans.sh`, 4 epochs, 500,000 blocks | `blocks-us-ca-v3`, `blocks-tier2-v1` | 3 chunks, 294,646 steps, best dev 0.8963 (epoch 3) |

The finished file: 22,785,395 bytes, sha256 `c5cedca0d4d0991f272cdafa28471a7744423bd65e26f0dc5970dafc2ee9640d` (weights stay on the build machine). The default recipe (`run.sh`, `names.sh`, `spans.sh`, without `challenger.sh`) is unchanged and remains permissive-only.

**Tier 2 name override.** Re-running the dev-name choice (`names-dev`, confidence 0.8, at least 150 items, a gain of at least 0.3 points over the rules) on `names-tier2-2` dev gives no country for the candidate and none for the shipped weights: the rules alone already score 0.99 or more there, so the earlier list (AU, EE, FR, GB, IT, LI, MT) is not reproduced by the current rules on this dev set (`recipes/models/full-candidate/names-dev-override.txt` is the candidate's table). The override list was left as shipped, so both models run with the same rule wiring.

**Rows** (`recipes/models/full-candidate/`; paired 95% bootstrap, 1000 resamples, seed 20261105; candidate minus shipped):

* Addresses, 40 countries, hinted and no hint, field F1 and full parse (`address-rows.tsv`). Gains (interval above zero), field F1 hinted: DE +1.5, LI +1.3, GB +3.3, IE +7.2, IM +7.6, MT +4.7, HU +5.7; no hint: those plus SM +2.3, EE +2.2, HR +1.9. Full parse hinted: IE +9.8, MT +8.2, LV +6.1; no hint: IE, MT, EE +5.5, LV. **One row loses: ES hinted field F1, 84.62 to 83.49, difference -1.13 [-2.46, -0.02]** (ES no hint -0.62 [-1.97, +0.47], ES full parse unchanged). US and CA are within noise.
* Tier 2 names (`name-rows.txt`, 29 countries; the US and CA rows in the file are retired and not counted): no row's interval lies wholly below zero. Six have an upper end of exactly 0: EE person tokens -3.77 [-11.5, 0.0] and class -2.17 [-6.5, 0.0], FR company tokens -3.39 [-11.3, 0.0] and class -1.72 [-5.2, 0.0], LI company tokens -7.04 [-18.3, 0.0] and class -3.39 [-8.5, 0.0] (EE, FR and LI are override countries). Gain: AU company tokens +4.9 [0.0, +13.6], class +3.9 [0.0, +9.8].
* Extraction (u18 sets, text and web, one run each, cut 0.4; `extraction-*.txt`): no row loses beyond its interval. Text address 72.7 to 84.6 (+11.9 [+3.1, +22.5]); text record F1 28.9 to 35.3 (+6.5 [+1.0, +13.1]); web rows within 0.4; text person +1.0 and org -0.5 within noise; website text -1.15 [-3.64, 0.00].

**Verdict and decision.** One row loses beyond its interval: **ES hinted address field F1, 84.62 to 83.49, a difference of -1.13 [-2.46, -0.02] on 69 items**. The build accepted the candidate as the default anyway: that ES set was already used while the rules were developed (fresh ES sets are being drawn), the candidate stays about 7 points above libpostal on ES, and the gains elsewhere are large and many (GB, IE, IM, MT, HU, DE; extraction addresses +11.9). ES register data is the thin set (Catastro covers 12% of municipalities); a larger OSM share for ES chosen on dev is the obvious next try. The Tier 2 person override list (AU, EE, FR, GB, IT, LI, MT) is kept as shipped; the development rule that chose it no longer selects any country with the current rules, for either set of weights.

**Reproducing the permissive-only weights** (`0e31f3fd…`, no OpenStreetMap data): `recipes/models/run.sh` without `--osm` (see `recipes/models/README.md`), then `names.sh` and `spans.sh`. The full candidate is `challenger.sh` with `NAMES_JOINT=1 TRAIN_PERMISSIVE=1 OSM_COUNT=1200000`, then `names.sh`, then `spans.sh`, as in the table above.
