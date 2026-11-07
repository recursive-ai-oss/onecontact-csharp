# Address challenger models (OpenStreetMap data)

Two address-field models trained on the converted OpenStreetMap address sets (ODbL, `docs/training-data-address-challenger.md`), built to be measured against the shipped `RecursiveAI.OneContact.Models` weights. **They are challengers: never packed, never in a `Models.*` package, never in the feed.** Weights stay with the build machine. A challenger can replace the default only after the rows below say it wins and its licence lineage has been accepted for shipping; until then the default recipes never read the challenger folder (`recipes/models/run.sh` and `data.manifest.tsv` name no challenger file, and the loader reads challenger data only when `--osm` is given).

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
