# Provenance: RecursiveAI.OneContact.Models

The trained weights in this package (`models.onenlp`) and everything that went into them.

## The weights

| | |
|---|---|
| File | `models.onenlp` (a saved OneNLP pipeline) |
| Size | 22,785,395 bytes |
| SHA-256 | `c5cedca0d4d0991f272cdafa28471a7744423bd65e26f0dc5970dafc2ee9640d` |
| Earlier weights | `0e31f3fd8ec06fe69cef2cc0c82d2cc495cd9313bbc9a4bfb64ef6c56d6dd60a` (trained on the open registers only, no OpenStreetMap data), and before them `adde4a664b622170235b49fc09da13916ac3f5b53031b1eb55d2378d0ef94d87` |
| Stage hashes | encoder and address head with jointly trained name heads `0482a535466b5ab595d1244508bca8758b9dee70496c56e634ae93dcf08dbc50`; after the name-head retraining `3ef21c6e8eff291ef893a42dc51b72bfc5a3537c41212d5c5a1f68ceb49ae6c3`; with the contact-span head (this file) `c5cedca0d4d0991f272cdafa28471a7744423bd65e26f0dc5970dafc2ee9640d` |
| Shape | one fast-tier encoder (width 128, 3 window layers, embeddings of 64, hash tables of 32,768 / 4,096 / 16,384 / 2,048 / 12,288 rows for the lower-cased text, prefix, suffix, shape and cased text, 4,096-row neighbour tables) shared by three softmax heads, `address-fields`, `name-parts`, `name-class`, and the contact-span head |
| Recipe | `recipes/models`: `challenger.sh` (encoder and address head on the register sets mixed with OpenStreetMap addresses), `names.sh`, `spans.sh`; `Program.cs`, `Challenger.cs`, `NameRecipe.cs`, `data.manifest.tsv` with `SupersetPipeline.cs`, on OneNLP 0.2.0-preview.1 and OneModel 1.0.0 |
| Seed | 1 in every stage (initial weights, shuffling, dropout); the choice of examples from each file is fixed by a hash with salt 20261105 |
| Optimizer | AdamW, learning rate 0.002 (0.001 for the name heads, 0.003 for the span head), weight decay 0.01, gradient clip 1, dropout 0.1, batches of about 1,500 words |
| Labels | from the data renderers' templates, the registers and the OpenStreetMap file's own labels; no hosted-model labels, no labels from a parser |
| Licence of the weights | `MIT OR Apache-2.0`, with every source credited in `THIRD-PARTY-NOTICES.md` |

**OpenStreetMap.** These weights were trained in part on OpenStreetMap addresses. © OpenStreetMap contributors, ODbL 1.0 (https://opendatacommons.org/licenses/odbl/1-0/, https://www.openstreetmap.org/copyright). The weights are a produced work of that data under ODbL section 4.3; the notice is repeated in `THIRD-PARTY-NOTICES.md`. The OpenStreetMap examples are not packed or redistributed.

**Permissive-only weights.** The previous weights (`0e31f3fd…`) used no OpenStreetMap data. They are rebuilt from the same recipe without the OpenStreetMap folder: `run.sh` for the encoder and heads, `names.sh`, `spans.sh` (see `recipes/models/README.md`).

## Full training run (current weights)

- **Encoder, address head, joint name heads** (`challenger.sh`, `NAMES_JOINT=1`, `TRAIN_PERMISSIVE=1`): 2,394,521 training examples and 38,999 development examples; 50,467 steps, 4 epochs, 3 chunks of up to 23 minutes (CPU only, 8 threads). Dev score by epoch 0.9784, 0.9826, 0.9830, **0.9839** (kept). Mix: 1,200,000 OpenStreetMap examples (target, shared over 50 country files, the converted libpostal OSM set, manifest `cd0bbe84c9960847264af43db8c5fc3a407352330e3e9a66a0e65f90e2d97eea`, converter `challenger-osm-2026-11-06.1`, seed 20261106, source file SHA-256 `3851ba7c9c6a0a2f5b27e419343faa9f01a571f7f44303bd123c6e4f45a83a69`); register-rendered addresses from 40 files (925,000 shared by the Tier 2 files, 80,000 each for the United States, Canada and the territories); 250,000 + 250,000 name examples. Development: 20,000 OpenStreetMap examples, 14,000 register examples (shared) + 1,500 for each of the three big files, 4,000 + 4,000 name examples.
- **Name heads** (`names.sh`, encoder frozen): 250,000 examples of `names-us-ca-v1`; 100,000 of `names-tier2-2` (manifest `19bb5bbaf7c2c1ff6d65fa0323f27addf706ae4f4bd4cd70f58a0e088d6710a0`; person names drawn from open national name statistics, seed 20261107, `tools/data/provenance/names-tier2.md`); all 123,948 Tier 2 company examples of `names-tier2-v1` (GLEIF Level 1 golden copy 2026-11-05 and ISO 20275 v1.6, CC0). 5,380 steps, 4 epochs, one chunk of 40 seconds; dev 0.9770, 0.9779, 0.9776, **0.9781** (kept).
- **Contact-span head** (`spans.sh`, encoder frozen): 500,000 blocks of `blocks-us-ca-v3` (generator `blocks-3`, seed 20261107) and `blocks-tier2-v1` (seed 20261108); 294,646 steps, 4 epochs, 3 chunks; dev 0.8940, 0.8855, 0.8826, **0.8963** (kept, the last epoch). Confidence cut used by extraction: 0.4.
- **Tier 2 name override.** Allowed in AU, EE, FR, GB, IT, LI and MT at confidence 0.8, as chosen for the previous weights on their development names. With the current rules the same development rule (a gain of at least 0.3 points over the rules at confidence 0.8 on a country with at least 150 items) selects no country for these weights or the previous ones; the list is kept as shipped.
- Evaluation sets were never read by training; selection used the development splits only. The weights were scored once on the real-page sets (`docs/maintainers/models-challenger.md`).

## Earlier training run (previous weights `0e31f3fd…`): address and name heads

- 1,546,583 training examples (12.6 million words), 24,837 development examples; 8,450 steps per epoch, 6 epochs planned (50,700 steps), 3 chunks on the build machine (CPU only, 8 threads, no GPU): 25 + 25 + 18 minutes.
- Dev score by epoch (mean of address-field span F1, name-part span F1 and name-class word accuracy): 0.9875, 0.9891, **0.9906**, 0.9885, 0.9905, 0.9892. The weights of the best evaluation (epoch 2, step 25,350) were kept.
- Mix: address examples from 40 files (the United States 80,000, Canada 80,000, the territories 80,000, 25,000 from each Tier 2 country, all of a smaller file); 250,000 name examples from the United States and Canada set and 250,000 from the Tier 2 set. Development: 14,000 address examples (shared) + 1,500 for each of the three big files, 4,000 + 4,000 name examples.

## Earlier name-head training (previous weights)

- The two name heads (`name-parts`, `name-class`) were retrained from the first run's weights with `recipes/models names` (`names.sh`): encoder frozen, the address and contact-span heads untouched (their predictions on four address evaluation sets, hinted and not, and the contact-span dev score are identical before and after).
- Data: 250,000 examples of `names-us-ca-v1`; 100,000 of `names-tier2-2` persons, several-people strings and non-names (1,556,803 rendered; person names drawn from open national name statistics, seed 20261107, `tools/data/provenance/names-tier2.md`); all 123,948 company examples of `names-tier2-v1` (GLEIF Level 1 golden copy 2026-11-05 and ISO 20275 v1.6, CC0). Development: 4,000 + 8,000 + 3,000 examples.
- Seed 1, learning rate 0.001, 100 warm-up steps, dropout 0.1, batches of about 1,500 words, 4 epochs of 2,114 steps, patience 3; best development epoch kept (epoch 1: 0.9820; the first run's heads scored 0.9656 on the same development names). One chunk of 38 seconds on 8 CPU threads (OneNLP 0.2.0-preview.1, OneModel 1.0.0).
- Labels from the renderers' templates; no hosted-model labels. Evaluation sets were never read by training. Several mixes (450,000, 300,000, 100,000, 50,000 Tier 2 persons; a seed; no English-language units) were scored on the real-page sets of the United States and Canada while the mix was chosen, so those two rows are not an untouched test.
- The Tier 2 name override is allowed in AU, EE, FR, GB, IT, LI and MT at confidence 0.8, chosen on the development names (`docs/maintainers/models-superset.md`).

## Earlier contact-span head (same data as the current one)

- Head `contact-spans` (OneNLP entity recognizer, BILUO, 2 words of context, hidden width 128), 12 labels: the six fields, each plain and with `:start`. The encoder and older heads are frozen, so their predictions are those of the weights without this head.
- Data: contact blocks inside synthetic pages (`blocks-us-ca-v3`, generator version `blocks-3`, seed 20261107, and `blocks-tier2-v1`, seed 20261108, 400,000 ids tried, 20,138 reserved, 0 span failures). 500,000 training blocks (96.1 million words) and 7,954 development blocks. Page chrome is English text written for the generator, with the labels, titles and navigation words of the Tier 2 languages added; people, companies and addresses of different countries are drawn independently.
- 4 epochs (294,646 steps), 3 chunks (25 + 25 + 5 minutes). Dev score by epoch (mean of field F1 and block-start F1): 0.8828, 0.8758, 0.8707, **0.8905** (kept, the last epoch).
- Confidence cut used by extraction: 0.4, as for the Tier 1 package.

## Training data

Each source's licence was read at the publisher before use; the quoted terms, versions and file hashes are in `tools/data/provenance/address-us-ca.md`, `address-tier2-a.md`, `address-tier2-b.md`, `address-tier2-c.md`, `names-us-ca.md`, `names-tier2.md` and `blocks-tier2.md`. The role and SHA-256 of every file the run read are in `recipes/models/data.manifest.tsv`. Attribution lines are in `THIRD-PARTY-NOTICES.md`.

| Set | Source and licence | Role |
|---|---|---|
| US and territories | National Address Database (public domain); Census TIGER/Line 2025 (public domain) | train, dev |
| Canada | StatCan Open Database of Addresses v1.0 (OGL-Canada 2.0) | train, dev |
| FR, BE, NL, LU, AT, CH, LI, DE (NRW), MC | BAN (Licence Ouverte 2.0); BeST (CC BY 4.0); BAG (CC0); BD-L-TC (CC0); BEV (CC BY 4.0); swisstopo (free use, source reference); Geobasis NRW (dl-de/zero-2-0); Monaco synthesized | train, dev |
| GB, JE, GG, IM, IE, ES, PT, IT, MT, SM, VA, AD | OS Open Names (OGL v3); Catastro INSPIRE (author and owner credit); ANNCSU and ISTAT (CC BY 4.0); GeoNames postal codes (CC BY 4.0) with our street names where no register is open | train, dev |
| NO, CZ, LV, EE, FI, IS | Kartverket (CC BY 4.0); RÚIAN (CC BY 4.0); VARIS (CC BY 4.0); ADS (open spatial-data licence, CC BY 4.0); DVV (CC BY 4.0); HMS Staðfangaskrá (CC BY) | train, dev |
| DK, SE, HU, AU (and MC, PT, IE, MT, SM, VA, AD, JE, GG, IM) | synthesized from GeoNames postal codes (CC BY 4.0), ABS ASGS Edition 3 correspondences (CC BY 4.0; Geoscape lineage noted) and street vocabulary of our own | train, dev |
| MX, PL, SK, SI, LT, HR, RO | INEGI DENUE; PRG (GUGiK); Register adries (CC0); GURS (CC BY 4.0); Registrų centras (CC BY 4.0); DGU Adrese (Otvorena dozvola); Poșta Română Infocod (OGL-ROU-1.0) | train, dev |
| Names and companies, US and Canada | Census 2010 surnames, SSA names (public domain); Retraite Québec (CC BY 4.0); INSEE (Licence Ouverte 2.0); GLEIF LEI (CC0); Companies House (OGL v3 / CC BY); SEC EDGAR (public) | train, dev |
| Person names, Tier 2 languages | national name statistics (counts of how many people carry a name) from Poland (CC0), Finland, Ireland, Belgium, Austria, Norway, Australia, Denmark (CC BY 4.0), Latvia, Sweden, Germany (Rostock) and Zurich (CC0), the United Kingdom (OGL v3.0), Switzerland (open use, source required), Slovenia (free use, source named) and France (Licence Ouverte 2.0), combined with our own lists of common given and family names, titles, particles and joining words (`tools/data/names/templates`) where no open list was found (CZ, SK, HR, HU, RO, PT, ES, MX, IT, MT, CA, EE, LT, IS). Given and family names are drawn independently, so no string is a real person's name. Terms, counts and proxies (Swiss surnames for DE and AT, Belgian lists for NL): `tools/data/provenance/names-tier2.md` | train, dev |
| Companies, Tier 2 | GLEIF Level 1 golden copy of 2026-11-05 (CC0), 33 country codes, at most 7,000 per country, the legal form located by the ISO 20275 list v1.6 (CC0). Few records exist for some countries (PT 4, SM 6, IM 8, MX 48, MC 49) | train, dev |
| Contact blocks | our own templates over the sets above and word lists of ours; phone numbers from libphonenumber example numbers with random tail digits (Apache-2.0 data) | train, dev |

- A training example's file is chosen by a hash of its text, so the same arguments give the same examples. Rendering is seeded and byte-identical on a second run.
- **OpenStreetMap addresses (ODbL 1.0)**: the address part of the libpostal parser training set of 2017-03-04, OpenStreetMap file `formatted_addresses_tagged.random.tsv.gz`, converted by `tools/data/AddressChallenger` (`tools/data/provenance/address-challenger-osm.md`; 5,672,707 train and 115,968 dev examples in 50 country files, 1,200,000 used). © OpenStreetMap contributors, ODbL 1.0. Role: train, dev.
- **Not used:** OpenAddresses, Common Crawl, G-NAF, any evaluation set, any hosted-model label.
- **Evaluation data never trained or selected this model.** Selection used the development splits only. The real-page evaluation sets were scored after training; the rules' policy for model reads (the guard against reading a Tier 2 address as United States) was chosen on the development splits and a fault the first real-page run showed (below), not tuned on the sets.

## Known limits

- For languages without an open name list (Czech, Slovak, Croatian, Hungarian, Romanian, Portuguese, Spanish, Italian, Maltese, Catalan, Estonian, Lithuanian, Icelandic) the person-name head has seen few distinct names, and German, Austrian and Dutch persons use Swiss and Belgian surname lists as stand-ins. So for Tier 2 texts the parsers use the name model only where the rules leave a string open, except in the seven countries listed above where it may also replace the rules' default person reading; see `docs/maintainers/models-superset.md`.
- Dependent-locality (settlement, sector) lines are learned by the address head, but the rules read most such lines as street text first, so the model rarely gets to label them (Czech, Croatian, Romanian).
- A text with no country hint that the Tier 2 rules cannot place is read as a United States or Canada address only when it carries a state or province; otherwise it is refused as country-undetermined.

## Attribution

See `THIRD-PARTY-NOTICES.md`.
