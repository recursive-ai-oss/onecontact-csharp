# Provenance: RecursiveAI.OneContact.Models.Tier1

The trained weights in this package (`tier1.onenlp`) and everything that went into them.

## The weights

| | |
|---|---|
| File | `tier1.onenlp` (a saved OneNLP pipeline) |
| Size | 9,843,463 bytes |
| SHA-256 | `ee73a8957f91114e81b3bf95a16c7ec25b10cb9333cd0bf320ac826d608722f0` |
| Training weights hash (OneNLP, checkpoint encoding) | `90161e7031f4e129327b80bb53ea27e491f7825e85dd863cd36f91cbb76ed292` |
| Shape | one fast-tier encoder (width 128, 3 window layers, embeddings of 64, hash tables of 12,288 / 2,048 / 6,144 / 2,048 / 4,096 rows for the lower-cased text, prefix, suffix, shape and cased text, 2,048-row neighbour tables) shared by three softmax heads: `address-fields`, `name-parts`, `name-class` |
| Recipe | `recipes/tier1` (`Program.cs`, `run.sh`) with `Tier1Pipeline.cs`, on OneNLP 0.2.0-preview.1 and OneModel 1.0.0 |
| Seed | 1 (initial weights, shuffling, dropout); selection data order fixed by a hash with salt 20261105 |
| Optimizer | AdamW, learning rate 0.002, 300 warm-up steps, weight decay 0.01, gradient clip 1, dropout 0.1, batches of about 1,500 words |
| Run | 2 chunks on the build machine (CPU only, 8 threads, no GPU): 26 + 20 minutes; 7 epochs (35,748 steps) of 899,984 examples (7.6 million words); the weights of the best dev evaluation (epoch 4, step 20,427) were kept |
| Dev score by epoch | 0.9877, 0.9912, 0.9901, 0.9921, 0.9916, 0.9918, 0.9908 (mean of address-field span F1, name-part span F1, name-class word accuracy on 17,889 dev examples) |
| Labels | from the data renderers' templates and the registers; no hosted-model labels, no labels from a parser |

## Training data

Examples come from the rendered training sets of the United States and Canada. Each source's licence was read at the publisher before use; the full terms, versions and hashes are in `tools/data/provenance/address-us-ca.md` and `tools/data/provenance/names-us-ca.md`. Attribution lines are in `THIRD-PARTY-NOTICES.md`.

| Role | Set | Examples used | File SHA-256 |
|---|---|---|---|
| train | US National Address Database (public domain), rendered, seed 20261105 | 200,000 of 1,270,061 | `bd5ec81bfc7c1634…bf3ebf3` (`us.train.jsonl`) |
| train | Statistics Canada Open Database of Addresses v1.0 (OGL-Canada), rendered | 200,000 of 369,786 | `9932d4a36ed2151e…09bb9` (`ca.train.jsonl`) |
| train | Census TIGER/Line 2025 and ZCTA relationship file (public domain), territories and five states, rendered | 200,000 of 248,662 | `637dbf7077f09b4d…10f9f` (`us-territories.train.jsonl`) |
| train | Names and companies: Census surnames, SSA given names, Retraite Québec, INSEE, GLEIF LEI (CC0), Companies House, SEC EDGAR; templates of our own; manifest `names-us-ca-v1` | 300,000 of 2,936,960 | train parts 00000–00011 (hashes in the set's manifest) |
| dev | the matching dev files of the same renders (fixed by a per-record hash before rendering) | 3,000 per address file, 9,000 names | `e2f59d49…2a02`, `eb448336…4053`, `43919718…612e`, `1187f3c9…76b1` |

- A training example's file is chosen by a hash of its text, so the same arguments give the same examples.
- **Not used:** OpenStreetMap, OpenAddresses, libpostal's training data, Common Crawl, G-NAF, any evaluation set, any hosted-model label.
- **Evaluation data never trained or selected this model.** The model was scored on the private evaluation sets once per candidate (below); the only choice made after seeing those numbers is none: the name-override confidence was chosen on dev names before the second candidate was scored.

## Candidates scored on the evaluation sets

Both candidates use the same weights; they differ in the parser's name policy.

1. **cand1**: the model decides names the rules leave unknown.
2. **cand2** (shipped): cand1 plus: a name the rules took as a person only by default (no title, suffix, nickname, particle, comma order, initial or known given name proved it) is read from the model when it says company or unknown with confidence 0.6 or more. The 0.6 was chosen on the dev names (class accuracy 93.5% rules alone; 96.96% at 0.5 and 0.6, 96.94–96.95% from 0.7 to 0.99).

Scores are in `docs/models.md`.

## Attribution

See `THIRD-PARTY-NOTICES.md`.
