# Accurate tier: measurements and notes (maintainers)

Heads over the frozen multilingual student (`RecursiveAI.OneNlp.Models.Multilingual.FrozenEncoder` 0.4.0-preview.1), trained with OneNLP 0.4.0-preview.1 `Train` and OneModel 1.0.0 on CPU in resumable chunks. Weights `accurate.onenlp`, 9,968,307 bytes, sha256 `d3f78832…ed5447`, worker `~/onecontact/u60/runs/spans/accurate.onenlp`. Provenance: `src/OneContact.Models.Accurate/PROVENANCE.md`. Shipped comparison model: `.Models` c5cedca0… (`models.onenlp`, OneNLP 0.2).

## Design

- **Frozen encoder, windowed heads.** A linear tagger over the frozen encoder's vectors reached 0.636 address field F1 (0.846 name-part F1, 0.928 class word accuracy) on development data, against 0.973 for a window recognizer (a window of three words each side, a hidden layer of 512). Address and name-part heads are therefore OneNLP `EntityRecognizer`s and the class head a `TextClassifier` with a hidden layer, each trained on its own examples in its own phase (the encoder is shared by name through `SharedEncoder`, so the saved file holds the heads only).
- **Fine-tuning the encoder was not tried.** OneNLP times one fine-tuning step of the student at 0.41 s for about 24 short sentences on 8 threads; the address set alone is 360k examples, so one epoch would be about 2.5 hours of CPU and the fine-tuned encoder (about 140 MB) would have to ship in this package. If a later round wants it, that is the question for a GPU plan (OneNLP's `Train` runs on CPU only; a GPU run needs approval).
- **Encoder speed.** Indicative, 8 threads, load average about 10: the encoder alone reads 3.4k words/s one document at a time, 4.9k with 32, 6.2k with 128. Training re-reads every document in the first epoch only (outputs are kept per document in memory for the run), so a chunk restart pays the first epoch again.
- **Registration.** `ContactModels.Register` gained an optional `normalizationRank`: address and name models compete on it, the span model on `rank`. The accurate tier registers `(rank 3, normalizationRank 0)`.
- **OneNLP 0.4 and 0.2 files.** The Tier 1 file (ac7b695a…) and the `.Models` file (c5cedca0…), both written by OneNLP 0.2, load under 0.4.0-preview.1 and label a sample address the same way as under 0.2 (`recipes/accurate loadcheck`). What the solution-wide bump needs: `using RecursiveAI.OneNlp;` for `Train`/`TrainOptions`/`Example`; `Token.Text`/`Span.Text` are strings; `EntityRuler` uses `Add`; `RecursiveAI.OneFont` 1.4.0 is a dependency of the 0.4 pack and restored without extra setup here. Bit-identical outputs were not checked, only that the files load and run.

## Development scores (dev splits, the numbers training selected on)

| Head | Metric | Score |
|---|---|---|
| address-fields | mean span F1 over 13,608 address dev items | 0.9730 |
| name-parts and name-class | mean of part F1 and class accuracy, 5,454 name dev items | 0.9786 |
| contact-spans | field F1 0.9738, block-start F1 0.9588 (mean 0.9663), 3,982 blocks; per field: person 0.992, phone 0.998, email 0.9996, address 0.967, company 0.954, website 0.743 (precision 0.59, recall 1.0) | 0.9663 |

All six span labels survive training (the label check for OneNLP's defect N3 is the per-field report above; the heads are fresh components, not packaged ones).

## Extraction rows (private sets, value F1 [bootstrap 95% CI], cut 0.4, run once)

Sets: web 119 pages, text 115 items (frozen CC-MAIN-2026-43 sets, gold on the worker only). `default` = shipped `.Models` (c5cedca0…) with the shipped configuration (span model on plain text only), `cand` = this model in the same configuration. Reference rows are the stored runs (spaCy `en_core_web_trf` 3.8, Recognizers 1.8.13, libphonenumber 9.0.43). Paired difference `cand` minus `default` in brackets.

| Set | Row | default | cand | cand − default | spaCy |
|---|---|---|---|---|---|
| text | person | 41.03 [19.51, 60.47] | 40.00 [18.18, 60.87] | −1.03 [−7.52, +7.18] | 61.29 [45.83, 75.76] |
| text | org | 53.52 [37.50, 66.67] | 52.08 [38.96, 64.08] | −1.44 [−11.48, +9.36] | 32.86 |
| text | address | 84.62 [71.79, 94.92] | 75.47 [60.00, 88.89] | −9.14 [−22.56, +2.38] | none |
| text | phone | 100.00 | 100.00 | 0 | libphonenumber 100.00; Recognizers 92.04 |
| text | email | 100.00 | 100.00 | 0 | Recognizers 96.55 |
| text | website | 33.33 [9.09, 55.00] | 40.00 [16.22, 61.90] | +6.67 [0.00, +19.23] | Recognizers 34.48 |
| text | record F1 | 35.29 [22.22, 47.06] | 31.48 [19.47, 42.86] | −3.81 [−16.62, +9.19] | none |
| web | person | 20.34 [5.00, 36.07] | 22.22 [6.06, 38.89] | +1.88 [−0.29, +5.03] | 8.41 [3.70, 13.97] |
| web | org | 35.29 [26.74, 44.55] | 35.60 [26.88, 44.55] | +0.31 [−1.41, +2.62] | 10.95 |
| web | address | 83.97 [76.64, 90.28] | 83.97 | 0 | none |
| web | phone / email / website | 99.03 / 98.88 / 58.18 | same | 0 | libphonenumber 99.03; Recognizers 90.00 / 92.22 / 58.18 |
| web | record F1 | 25.55 [17.22, 32.91] | 26.09 [17.81, 33.48] | +0.54 [−0.76, +2.73] | none |

The row this tier exists for, text person against spaCy's 61.3, is **not met**: 40.0, below spaCy by 21 points (the CI of the difference includes zero because the set has 20 gold persons), and no better than the fast tier. The extra capacity of the encoder did not help the span head on these items: its person recall stays at 40% in both, and text address F1 fell 9 points (within the CI) because the accurate span head is less precise on address blocks at the same cut. Web rows are the same as the fast tier by construction (the model reads plain text only on web pages; the rules and markup read the page),, apart from person and org rows that differ by a few items (+1.88 and +0.31, inside the noise; the cause was not traced). The cut (0.4) was the shipped one and was not tuned on these sets.

## Address and name rows (frozen sets, scored once, hinted unless stated)

Complete per-country rows: `models-accurate-rows.tsv` (aggregates only; produced by `tools/Eval/score-accurate.sh` and `summarize-accurate.py`). The reference systems are the stored libpostal 1.1.4 / Senzing runs (without a country), probablepeople, python-nameparser and cleanco. Sets: the Tier 1 address sets (US, CA), the Tier 2 group sets, and the fresh US/CA name and RO/MX sets.

- The accurate tier equals the shipped `.Models` on most rows because rules decide those items; where the model decides, differences are small. Hinted address field F1 (accurate minus shipped): MX +4.02, VA +4.25 (n=6), PT +1.64, IT +0.51, CA +0.32, US +0.21, RO +0.56; MT −1.35, LI −0.77, NO −0.61, SK −0.41, SI −0.30; all inside the paired noise.
- Names (accurate minus shipped): company tokens FR +10.17, AU +5.89, CA +5.87, US +1.06; class FR +3.45, AU +5.89, CA +3.26, US +0.37, RO +33.3 and MX +50.0 (n=9 and 12); person tokens US −1.04, MX −11.9, RO −3.6 (small n).
- United States address field F1 97.35 (libpostal 97.18); Canada 92.60 / hinted 93.04 (libpostal 93.34); US names class 94.83 (probablepeople 95.20), company tokens 90.24 (73.88), legal-form detection 93.75 (cleanco 98.21, the same miss as the fast tier).

## Speed and memory (indicative, heavy class, 8 cores, Ryzen 9 7950X3D, load average 12–17 so pessimistic)

Wall time of the evaluation tool over the sets, including process start and model load; two repetitions.

| Run | text, 115 items | web, 119 pages | peak RSS |
|---|---|---|---|
| rules only | 0.33–0.36 s | 3.97–4.05 s | 72 MB / 249 MB |
| `.Models` (fast tier) | 0.70–0.73 s | 4.01–4.11 s | 260 MB / 387 MB |
| accurate | 3.48–4.47 s | 6.02–7.42 s | 792–799 MB / 848–851 MB |

That is roughly 25–35 ms per text item and 20–30 ms per page above the rules (encoder load about 0.6 s included). The NativeAOT smoke app with the accurate package ran in 0.92 s wall and 746 MB peak RSS; the published binary is 189 MB (it embeds the encoder's ONNX file and the head weights). Not gated.

## AOT

`samples/OneContact.AotSmoke.ModelsAccurate` publishes with NativeAOT (gcc, `-p:SysRoot=/`) with no trim or AOT warnings and runs: the encoder package's ONNX graph loads and runs under NativeAOT, name class, plain-text extraction and the span head all work.

## Follow-ups

- Text person is still far behind spaCy for both tiers. A second look needs better training data for people in running prose, not a larger encoder (the agent-labelled spans round of the fast tier is the open item).
- `ModelOnWeb` was not scored with this model.
- Moving the fast tiers to OneNLP 0.4 (see Design) so one project can reference both.
