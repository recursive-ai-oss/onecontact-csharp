# Tier 1 + Tier 2 recipe

Trains the shared encoder and the heads of `RecursiveAI.OneContact.Models` with OneNLP's `pipeline.Train` on OneModel.Training, from the rendered training sets of the United States, Canada and every Tier 2 country. The model shape is `src/OneContact.Models/SupersetPipeline.cs`; the training constants are in `Program.cs`.

```
dotnet build -c Release recipes/models
ADDRESSES=<folder with <cc>.{train,dev}.jsonl for us, ca, ust and every Tier 2 country> NAMES_US_CA=<names-us-ca-v1> NAMES_TIER2=<names-tier2-v1> \
  OUT=<run folder> EPOCHS=6 SEED=1 THREADS=8 BUDGET=1500 recipes/models/run.sh      # repeat until it prints model=...
BASE=<OUT>/models.onenlp BLOCKS_US_CA=<blocks-us-ca-v3> BLOCKS_TIER2=<blocks-tier2-v1> OUT=<span run folder> EPOCHS=4 BLOCKS_COUNT=500000 recipes/models/spans.sh   # repeat until it prints model=...
```

The name heads can be retrained alone, with the encoder frozen and the other heads untouched, from a finished pipeline:

```
BASE=<OUT>/models.onenlp NAMES_US_CA=<names-us-ca-v1> NAMES_TIER2=<names-tier2-2> COMPANIES=<folder with train/ and dev/ of the company lines of names-tier2-v1> OUT=<names run folder> NAMES2_COUNT=100000 recipes/models/names.sh
```

Each run trains for `BUDGET` seconds, writes `last.ckpt` and `best.ckpt`, and stops; running it again with the same arguments continues, and the result equals one uninterrupted run. The finished pipeline is `OUT/models.onenlp`.

- `data.manifest.tsv` lists every data file the run may read with its role (`train` or `dev`), size and SHA-256; the recipe reads only those. Evaluation sets are not among them.
- `blocks-tier2.sh` renders the Tier 2 contact blocks with `tools/data/blocks` (`render-tier2`). The Tier 2 persons come from `tools/data/names` (`render-tier2`, open national name lists) and the Tier 2 companies from `tools/data/names-tier2`.
- Data choice: 25,000 address examples per Tier 2 country and 80,000 for each of the United States, Canada and the territories, 250,000 + 250,000 name examples, 500,000 blocks; chosen by a hash of the example text, so every run sees the same examples. Seed 1, learning rate 0.002 (0.003 for the span head), 300 warm-up steps, batches of about 1,500 words, dropout 0.1, patience 6.

Provenance of the data and of the trained weights: `src/OneContact.Models/PROVENANCE.md`.

## Address challenger mode

`challenger.sh` trains an address challenger on the converted OpenStreetMap sets (ODbL), alone or mixed with the register sets, with the same pipeline shape and the same resumable chunks. It is a separate lineage and never feeds a package; `run.sh` and `data.manifest.tsv` never name its data. See `docs/models-challenger.md` and `PROVENANCE-challenger.md`.
