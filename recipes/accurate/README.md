# Accurate-tier recipe

Trains the heads of `RecursiveAI.OneContact.Models.Accurate` with OneNLP's `Train` over the frozen multilingual student encoder (`RecursiveAI.OneNlp.Models.Multilingual.FrozenEncoder`), from the same rendered training sets as `recipes/models`. The model shape is `src/OneContact.Models.Accurate/AccuratePipeline.cs`; the training constants are in `Program.cs`.

```
dotnet build -c Release recipes/accurate
ADDRESSES=<folder with <cc>.{train,dev}.jsonl> OSM=<optional OpenStreetMap folder> OUT=<run>/addr EPOCHS=6 recipes/accurate/run.sh    # repeat until it prints model=...
BASE=<run>/addr/addr.onenlp NAMES_US_CA=<names-us-ca-v1> NAMES_TIER2=<names-tier2-2> COMPANIES=<company lines> OUT=<run>/names recipes/accurate/names.sh
BASE=<run>/names/heads.onenlp BLOCKS_US_CA=<blocks-us-ca-v3> BLOCKS_TIER2=<blocks-tier2-v1> OUT=<run>/spans BLOCKS_COUNT=40000 recipes/accurate/spans.sh
```

Each run trains for `BUDGET` seconds (default 1500), writes `last.ckpt`/`best.ckpt` and stops; running it again with the same arguments continues, and the result equals one uninterrupted run. The encoder output of each document is kept in memory for the length of a run, so the first epoch is the slow one (about 10 minutes for the address set on 8 cores) and the next ones take a minute or two; a restarted chunk pays the first epoch's encoder cost again.

- `probe` times the encoder alone; `dev` and `spans-dev` score a saved file on the development splits.
- Data choice and sizes are in `src/OneContact.Models.Accurate/PROVENANCE.md`. `data.manifest.tsv` lists the files the run may read with their roles (`train` or `dev`); evaluation sets are not among them.
- The finished file is `OUT/accurate.onenlp`; embed it with `ONECONTACT_ACCURATE_WEIGHTS` after updating `weights.manifest.props` and the provenance together.
- OneNLP 0.4 is referenced from this project and from the Accurate package only (`VersionOverride`); the rest of the solution is on an older OneNLP until its models are retrained.
