# Provenance: RecursiveAI.OneContact.Models.Accurate

The trained head weights in this package (`accurate.onenlp`) and everything that went into them.

## The weights

| | |
|---|---|
| File | `accurate.onenlp` (a saved OneNLP pipeline holding the heads only; it names its encoder and does not contain it) |
| Size | 9,968,307 bytes |
| SHA-256 | `d3f788323884ee460a2305a81c39742f390263f48696d9e19cc70a9e23ed5447` |
| Stage hashes | address head `38b575eb9d5c7f8cfef43adf65b2357f54463188ee2b006efe9c1e2e226c6983` (training-run hash; file `addr.onenlp` 5,723,957 bytes); with the name-part and name-class heads (`heads.onenlp`, 7,950,089 bytes); with the contact-span head (this file) |
| Encoder | `RecursiveAI.OneNlp.Models.Multilingual.FrozenEncoder` 0.4.0-preview.1, `FrozenStudentEncoder.Create()` (student-v1: six layers, width 384, 113 languages), mean pooling of subword pieces, 512-token windows. Frozen: never trained here and not in this file. Its lineage (mmBERT-base, MIT; FineWeb-2 and FineWeb, ODC-By 1.0, Common Crawl terms; Wikipedia, CC BY-SA 3.0, so share-alike lineage) is credited in `THIRD-PARTY-NOTICES.md` |
| Shape | four heads on one shared frozen encoder, `address-fields` and `name-parts` and `contact-spans` (window recognizers: a window of 3, 2 and 2 words each side of the 384-wide vectors, a hidden layer of 512, 256 and 256, BILUO tags) and `name-class` (a classifier over the string with a hidden layer of 128). A linear layer per word was tried first and scored 0.64 address field F1 on development data against 0.97 for the window recognizer, so it was not used |
| Recipe | `recipes/accurate`: `run.sh` (address head), `names.sh` (name heads), `spans.sh` (span head); `Program.cs`, `data.manifest.tsv` with `AccuratePipeline.cs`, on OneNLP 0.4.0-preview.1 and OneModel 1.0.0 |
| Seed | 1 in every stage; the choice of examples from each file is fixed by a hash with salt 20261105 (20261106 for blocks) |
| Optimizer | AdamW, learning rate 0.003, weight decay 0.01, gradient clip 1, dropout 0.1, batches of about 1,500 words; the encoder output is the only input, so training ran on CPU in resumable chunks (no GPU) |
| Counts | address head: 359,666 training examples (3.35 M words) from the registers (200,000 shared over the Tier 2 countries, 20,000 each for the United States, Canada and the territories) and 120,000 OpenStreetMap addresses, 6 epochs, best development epoch kept (0.9730 mean span F1); name heads: 129,331 examples (40,000 United States and Canada, 60,000 Tier 2 persons, 30,000 companies), 6 epochs (0.9786); span head: 40,000 contact blocks (7.76 M words), 3 epochs (0.9663) |
| Labels | from the data renderers' templates, the registers and the OpenStreetMap file's own labels; no hosted-model labels, no labels from a parser |
| Licence of the weights | `MIT OR Apache-2.0`, with every source credited in `THIRD-PARTY-NOTICES.md` |

**OpenStreetMap.** These weights were trained in part on OpenStreetMap addresses. © OpenStreetMap contributors, ODbL 1.0 (https://opendatacommons.org/licenses/odbl/1-0/, https://www.openstreetmap.org/copyright). The weights are a produced work of that data (ODbL section 4.3); the data itself is not redistributed. The weights that use no OpenStreetMap data can be rebuilt from the committed recipe (`run.sh` without `OSM`).

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
- **Not used:** OpenAddresses, Common Crawl (in the heads), G-NAF, any evaluation set, any hosted-model label.
- **Evaluation data never trained or selected this model.** Selection used the development splits only; each real-page evaluation set was scored once, after training, with the cut and the rules unchanged.

## Known limits

- The model labels words from the frozen encoder's vectors. Line breaks and punctuation that the encoder's tokenizer drops get no vector, so the heads see them as gaps.
- It needs the encoder package (136 MB, read on first use; about 0.8 GB resident) and is several times slower than the fast tier; see `docs/models-accurate.md`.
- For languages without an open name list the person-name head has seen few distinct names, as for `RecursiveAI.OneContact.Models`.

## Attribution

See `THIRD-PARTY-NOTICES.md`.
