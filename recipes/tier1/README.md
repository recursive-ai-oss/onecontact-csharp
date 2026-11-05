# Tier 1 recipe

Trains the shared encoder and the three heads of `RecursiveAI.OneContact.Models.Tier1` with OneNLP's `pipeline.Train` on OneModel.Training, from the rendered United States and Canada training sets. It is the whole recipe: the model shape is `src/OneContact.Models.Tier1/Tier1Pipeline.cs`, the training constants are at the top of `Program.cs`.

```
dotnet build -c Release recipes/tier1
ONECONTACT_DATA=<folder> OUT=<run folder> BUDGET=1500 EPOCHS=7 SEED=1 recipes/tier1/run.sh   # repeat until it prints model=...
```

`ONECONTACT_DATA` holds the rendered sets: `addresses/{us,ca,us-territories}.{train,dev}.jsonl` (`docs/training-data-address-us-ca.md`) and `names/{train,dev}/*.jsonl` (`tools/data/provenance/names-us-ca.md`). Each run trains for `BUDGET` seconds, writes `last.ckpt`, `best.ckpt` and the dev scores, and stops; running it again with the same arguments continues from `last.ckpt`, and the result equals one uninterrupted run. When training finishes (the epochs are done or patience ends it) it writes `tier1.onenlp`, the saved pipeline.

`Program.cs dev --model tier1.onenlp --addresses <dev files> --names <dev files>` scores a saved model on dev files.

- **Data chosen:** 600,000 address examples (200,000 from each of the three files, chosen by a hash of the example text) and 300,000 name and company examples; 9,000 + 9,000 dev examples. The hash and the counts are fixed, so every run sees the same examples.
- **Seed:** 1 (initial weights, shuffling, dropout). Learning rate 0.002 with 300 warm-up steps, batches of about 1,500 words, dropout 0.1, patience 6, selection on the dev score.
- **No evaluation data is read.** The recipe takes only the training and dev files named on its command line.

Provenance of the data and of the trained weights: `src/OneContact.Models.Tier1/PROVENANCE.md`.
