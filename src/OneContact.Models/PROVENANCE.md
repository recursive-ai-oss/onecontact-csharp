# Provenance: RecursiveAI.OneContact.Models

The trained weights in this package (`models.onenlp`) and everything that went into them.

## The weights

| | |
|---|---|
| File | `models.onenlp` (a saved OneNLP pipeline) |
| Size | 22,784,861 bytes |
| SHA-256 | `0e31f3fd8ec06fe69cef2cc0c82d2cc495cd9313bbc9a4bfb64ef6c56d6dd60a` |
| Earlier weights | `adde4a664b622170235b49fc09da13916ac3f5b53031b1eb55d2378d0ef94d87` (name heads trained on our own Tier 2 lists); the encoder, address head and contact-span head of the current file are the same as in those weights |
| Training weights hash (OneNLP, checkpoint encoding) | encoder, address head and the earlier name heads `955076baf0cfcda5c0d75d8bad84f1d8f1b4c563a37b8d8afa5ffe034166a2e9`; the two retrained name heads alone `02a56cf1ace36d72b6aaa2a08e7686588d19187def385b8d2e9e25169b226142` |
| Shape | one fast-tier encoder (width 128, 3 window layers, embeddings of 64, hash tables of 32,768 / 4,096 / 16,384 / 2,048 / 12,288 rows for the lower-cased text, prefix, suffix, shape and cased text, 4,096-row neighbour tables) shared by three softmax heads, `address-fields`, `name-parts`, `name-class`, and the contact-span head |
| Recipe | `recipes/models` (`Program.cs`, `run.sh`, `spans.sh`, `names.sh`, `NameRecipe.cs`, `data.manifest.tsv`) with `SupersetPipeline.cs`, on OneNLP 0.2.0-preview.1 and OneModel 1.0.0 |
| Seed | 1 (initial weights, shuffling, dropout); the choice of examples from each file is fixed by a hash with salt 20261105 |
| Optimizer | AdamW, learning rate 0.002, 300 warm-up steps, weight decay 0.01, gradient clip 1, dropout 0.1, batches of about 1,500 words |
| Labels | from the data renderers' templates and the registers; no hosted-model labels, no labels from a parser |

## Training run, address and name heads

- 1,546,583 training examples (12.6 million words), 24,837 development examples; 8,450 steps per epoch, 6 epochs planned (50,700 steps), 3 chunks on the build machine (CPU only, 8 threads, no GPU): 25 + 25 + 18 minutes.
- Dev score by epoch (mean of address-field span F1, name-part span F1 and name-class word accuracy): 0.9875, 0.9891, **0.9906**, 0.9885, 0.9905, 0.9892. The weights of the best evaluation (epoch 2, step 25,350) were kept.
- Mix: address examples from 40 files (the United States 80,000, Canada 80,000, the territories 80,000, 25,000 from each Tier 2 country, all of a smaller file); 250,000 name examples from the United States and Canada set and 250,000 from the Tier 2 set. Development: 14,000 address examples (shared) + 1,500 for each of the three big files, 4,000 + 4,000 name examples.

## Name heads, second training (current weights)

- The two name heads (`name-parts`, `name-class`) were retrained from the first run's weights with `recipes/models names` (`names.sh`): encoder frozen, the address and contact-span heads untouched (their predictions on four address evaluation sets, hinted and not, and the contact-span dev score are identical before and after).
- Data: 250,000 examples of `names-us-ca-v1`; 100,000 of `names-tier2-2` persons, several-people strings and non-names (1,556,803 rendered; person names drawn from open national name statistics, seed 20261107, `tools/data/provenance/names-tier2.md`); all 123,948 company examples of `names-tier2-v1` (GLEIF Level 1 golden copy 2026-11-05 and ISO 20275 v1.6, CC0). Development: 4,000 + 8,000 + 3,000 examples.
- Seed 1, learning rate 0.001, 100 warm-up steps, dropout 0.1, batches of about 1,500 words, 4 epochs of 2,114 steps, patience 3; best development epoch kept (epoch 1: 0.9820; the first run's heads scored 0.9656 on the same development names). One chunk of 38 seconds on 8 CPU threads (OneNLP 0.2.0-preview.1, OneModel 1.0.0).
- Labels from the renderers' templates; no hosted-model labels. Evaluation sets were never read by training. Several mixes (450,000, 300,000, 100,000, 50,000 Tier 2 persons; a seed; no English-language units) were scored on the real-page sets of the United States and Canada while the mix was chosen, so those two rows are not an untouched test.
- The Tier 2 name override is allowed in AU, EE, FR, GB, IT, LI and MT at confidence 0.8, chosen on the development names (`docs/models-superset.md`).

## Contact-span head

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
- **Not used:** OpenStreetMap, OpenAddresses, libpostal's training data, Common Crawl, G-NAF, any evaluation set, any hosted-model label.
- **Evaluation data never trained or selected this model.** Selection used the development splits only. The real-page evaluation sets were scored after training; the rules' policy for model reads (the guard against reading a Tier 2 address as United States) was chosen on the development splits and a fault the first real-page run showed (below), not tuned on the sets.

## Known limits

- For languages without an open name list (Czech, Slovak, Croatian, Hungarian, Romanian, Portuguese, Spanish, Italian, Maltese, Catalan, Estonian, Lithuanian, Icelandic) the person-name head has seen few distinct names, and German, Austrian and Dutch persons use Swiss and Belgian surname lists as stand-ins. So for Tier 2 texts the parsers use the name model only where the rules leave a string open, except in the seven countries listed above where it may also replace the rules' default person reading; see `docs/models-superset.md`.
- Dependent-locality (settlement, sector) lines are learned by the address head, but the rules read most such lines as street text first, so the model rarely gets to label them (Czech, Croatian, Romanian).
- A text with no country hint that the Tier 2 rules cannot place is read as a United States or Canada address only when it carries a state or province; otherwise it is refused as country-undetermined.

## Attribution

See `THIRD-PARTY-NOTICES.md`.
