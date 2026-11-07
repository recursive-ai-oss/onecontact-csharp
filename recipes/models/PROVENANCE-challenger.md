# Provenance: address challenger models C1 and C2

**Lineage: challenger (ODbL).** Built and measured; never packed, never in a package or the feed. See `docs/maintainers/models-challenger.md`.

## Data

| Part | Role | Source | Licence | Version / hash |
|---|---|---|---|---|
| OpenStreetMap addresses, 50 country files | train, dev | libpostal parser training set, OpenStreetMap file `formatted_addresses_tagged.random.tsv.gz` (SHA-256 `3851ba7c9c6a0a2f5b27e419343faa9f01a571f7f44303bd123c6e4f45a83a69`), converted by `tools/data/AddressChallenger` (converter `challenger-osm-2026-11-06.1`, seed 20261106) | ODbL 1.0 | manifest `cd0bbe84c9960847264af43db8c5fc3a407352330e3e9a66a0e65f90e2d97eea`; 5,672,707 train / 115,968 dev; every line `set: challenger-osm` |
| Register-rendered address sets (C2 train; dev of every candidate) | train (C2), dev | the 40 address sets listed in `recipes/models/data.manifest.tsv` (permissive registers; sources, licences and attribution in `tools/data/provenance/`) | as logged there | hashes in `data.manifest.tsv` |
| Name examples (10,000 from each name set, name heads only) | train | the default recipe's US/CA and Tier 2 name sets | as logged in `src/OneContact.Models/PROVENANCE.md` | `data.manifest.tsv` |

Credit lines required by the data: "Contains information from OpenStreetMap, which is made available under the Open Database License (ODbL). © OpenStreetMap contributors, https://www.openstreetmap.org/copyright." and the register credits of `src/OneContact.Models/PROVENANCE.md` for C2.

Label source: the source file's own labels, mapped by the converter and the loader (`docs/training-data-address-challenger.md`, `recipes/models/Challenger.cs`). No hosted-LLM labels. Selection of examples is by a hash of the line (salt 20261105), so a rerun sees the same examples.

Evaluation data (the frozen real address sets) never entered training; the recipe reads only the folders it is given, none of which is an evaluation folder.

## Training

OneNLP 0.2.0-preview.1 `Train`, OneModel 1.0.0, .NET 10.0.403, CPU only (AMD Ryzen 9 7950X3D, 8 threads per chunk, a shared and loaded machine), seed 1. Recipe constants: `recipes/models/Program.cs`. Commands: `recipes/models/challenger.sh` with `EPOCHS=4`.

| Candidate | `OSM_COUNT` | `TRAIN_PERMISSIVE` | Steps | Best dev | Weights SHA-256 |
|---|---|---|---|---|---|
| C1 | 2000000 | 0 | 27,292 | 0.7710 (epoch 3) | `a098c079a86fb1a48f5fbf4f100a308dae00b988e92e5ec8874eb7c2e6be1830` |
| C2 (C2a) | 1200000 | 1 | 45,272 | 0.9763 (epoch 3) | `c77004dbf9bde985aba49eca5fa7285da4a294d569d66bee2d45b0c05fdfbd5a` |
| C2b | 400000 | 1 | 34,724 | 0.9693 (epoch 3) | `d22817ed4827be856d29ee394bd7d54fa3f567b6e6ead1647654bd11a10451e8` |

Each is 22.4 MB. Chunk log (wall seconds in the chunk, steps at its end): C1 1380 s → 17,554, 807 s → 27,292 (done); C2a 1380 s → 17,171, 1380 s → 35,761, 656 s → 45,272 (done); C2b 1380 s → 15,547, 1380 s → 34,068, 54 s → 34,724 (done). Dev curves: `docs/maintainers/models-challenger.md`.

Weights live on the build machine only.

## Full candidate (encoder, address, name and contact-span heads)

Lineage: challenger (ODbL data in the address mix). Not packed. Steps, data and hashes: `docs/maintainers/models-challenger.md` (Full candidate). Data on top of the table above: `names-tier2-2` person names (open national name statistics, seed 20261107, `tools/data/provenance/names-tier2.md`, manifest `19bb5bbaf7c2c1ff6d65fa0323f27addf706ae4f4bd4cd70f58a0e088d6710a0`), `names-us-ca-v1`, the Tier 2 company examples of `names-tier2-v1` (GLEIF Level 1 golden copy 2026-11-05 and ISO 20275 v1.6, CC0), `blocks-us-ca-v3` and `blocks-tier2-v1`. Seed 1 everywhere. Weights: encoder stage `0482a535466b5ab595d1244508bca8758b9dee70496c56e634ae93dcf08dbc50`, after the name heads `3ef21c6e8eff291ef893a42dc51b72bfc5a3537c41212d5c5a1f68ceb49ae6c3`, final `c5cedca0d4d0991f272cdafa28471a7744423bd65e26f0dc5970dafc2ee9640d` (22,785,395 bytes). No hosted-LLM labels. Credit: "© OpenStreetMap contributors, ODbL 1.0" plus the register credits of `src/OneContact.Models/PROVENANCE.md`.
