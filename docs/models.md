# Models

Normative notes on how trained models reach the parsers, what a model package carries, and how a model is trained. Rules first: a model only fills what the rules leave open.

## What a model does and does not do

- **Addresses.** Rules read the last line (postal code, state or province, city), the delivery line and the country (`docs/address-rules-us-ca.md`). Input the rules refuse with `address.model-required`, `address.no-last-line` or `address.no-delivery-line` goes to the address model, which labels the words of the whole text; the parser turns the labels into the same `ParsedAddress` the rules make. When the rules succeed, every field they found stays; the model only labels the lines the rules keep as written (recipient, organization) where it can. A model field says `DecisionSource.Model` in `ParseDetail` with the model's confidence; a rule field says `Rule` with confidence 1. The standard form of a model field uses the authority tables where they have an entry (state, province, street type, directional, unit designator) and the upper-cased words otherwise. Nothing is invented: a field the model did not label is absent, and the country must come from a country name, the state or province, the postal code or the hint.
- **Names.** Rules classify a string as person, company, several people or unknown (`docs/names.md`). A string the rules leave `Unknown` goes to the name model. Its class counts when more than half of the words (weighted by their probability) vote for it. A person needs a given or a family name from the model's parts; a company is read by the company rules with the class decided by the model; several people are reported as a class only, because the model labels words and does not split people. A class or part a rule found is never replaced.
  The rules also take two or three plain words as a person for want of a sign of a company (their issue `person.no-evidence`); that is a default, not a rule hit, so when the model says company or unknown with confidence 0.6 or more it decides instead. The 0.6 was chosen on the development names (class accuracy 93.5% with rules alone, 96.96% at 0.5 and 0.6, flat above).
- **A state or province from the model must be one.** Of several admin-area spans the last one the authority tables know is taken, and a rejected one right before the city becomes the start of the city (`new york ny`).
- **Without a model package** the refusal stays: `address.model-required`, whose description names `RecursiveAI.OneContact.Models.Tier1`. Nothing is downloaded, ever.

## The seam in core

`RecursiveAI.OneContact.Models` (core, BCL only):

| Type | Role |
|---|---|
| `IAddressFieldModel.Tag(string)` | Labelled spans (`street.name`, `locality`, `recipient`, ...) with offsets into the text and a confidence. |
| `INameModel.Analyze(string)` | A `NameClass`, its confidence and labelled parts (`Given`, `Family`, `Base`, `LegalForm`, ...). |
| `ContactModels.Register(packageId, rank, address, name)` | A package makes its models known. The highest rank wins: Tier 1 is 1, the Tier 1+2 superset 2, the accurate tier 3. |
| `ContactModels.Address`, `.Name`, `.PackageId` | What the parsers use; `null` when no package is referenced. |

Registration is not discovered by scanning: each model package ships a one-line source file under `contentFiles/cs/any/` that NuGet adds to the referencing project. It holds a `[ModuleInitializer]` that calls the package's `Register()`, so the trimmer and NativeAOT see the model code and its embedded weights as reachable. `Register()` costs nothing; the weights are read on first use, once, and the model is shared by all callers (it is safe to call from many threads).

The 0.4 extraction span head and the 0.5 superset reuse this: a package registers more interfaces or a higher rank; the parsers do not change.

## The Tier 1 model

`RecursiveAI.OneContact.Models.Tier1` holds one saved OneNLP pipeline (`weights/tier1.onenlp`, embedded): a fast-tier encoder (hash embeddings of each word's lower-cased text, case-preserving text, prefix, suffix, shape and its neighbours, then three window layers) shared by three softmax heads:

| Head | Column | Labels |
|---|---|---|
| `address-fields` | Tag | `B-`/`I-` plus the field label, or `O` |
| `name-parts` | Pos | `B-`/`I-` plus the part label, or `O` |
| `name-class` | Morph | `person`, `company`, `multiple` or `unknown` on every word |

The encoder shape is written in `src/OneContact.Models.Tier1/Tier1Pipeline.cs`.

**Words.** A word is a run of letters, digits and combining marks; every other character that is not white space is a word; a line break is a word (where the lines end tells a recipient line from a street line). The training data is cut the same way (`Tier1Tokenizer`).

**Reading labels.** A span is a run of words with the same label; a `B-` tag starts a new span. A span's confidence is the mean probability of its words. The class is the vote of the words' class labels weighted by probability; line breaks and punctuation do not vote.

**Weights.** The pack takes the file from the directory in `ONECONTACT_TIER1_WEIGHTS`, checks it against `weights.manifest.props` (name, size, SHA-256) and embeds it; a missing or different file fails the build with both hashes. Packing without the directory fails saying what to set. A development build has no weights, and `Tier1Model.Instance` then throws `InvalidOperationException` naming the package. Retraining replaces the file and the manifest row, with no code change.

**Package contents.** The assembly with the embedded weights, `weights.manifest.props`, `PROVENANCE.md`, `THIRD-PARTY-NOTICES.md`, both licences, the registration file. No training or evaluation data.

## Training

The recipe is `recipes/tier1` (a console project over OneNLP's public `pipeline.Train`; see its README). Everything that shapes the model is in `Tier1Pipeline.cs` and the constants at the top of `recipes/tier1/Program.cs`; seeds are arguments. A run writes `last.ckpt` and `best.ckpt` and resumes from the last checkpoint with the same arguments; chunks of at most 30 minutes continue the same run.

- Address examples are the rendered United States and Canada sets (`docs/training-data-address-us-ca.md`), words as the sets supply them plus a word for each line break.
- Name examples are the rendered name and company set (`tools/data/provenance/names-us-ca.md`); a part's edges cut words, the class goes on every word.
- Examples are chosen from each file by a hash of their text, the same on every run, so a resumed run sees the same examples; dev examples come from the sets' dev files. Selection is on the dev score (the mean of address-field span F1, name-part span F1 and name-class word accuracy). No evaluation set is read by the recipe.
- A training example for one head has no label for the others; the loss ignores words without a label.

## Measured

Tier 1 model `tier1.onenlp` (9.84 MB), rules plus model, on the private United States and Canada evaluation sets, coarse labels, scorer `tools/Eval/score.py`, paired bootstrap 95% CI (1000 resamples, seed 20261105). Aggregates only. Reference: best of libpostal 1.1 and Senzing (addresses), probablepeople (names).

| Set | Measure | Rules + model | Rules only | Best reference | Difference to reference |
|---|---|---|---|---|---|
| US address (n=229) | field F1 | 97.22 [96.11, 98.12] | 92.01 | libpostal 97.18 | +0.04 [-0.96, +1.08] |
| US address | full parse | 86.46 [82.10, 90.39] | 74.67 | libpostal 86.03 | +0.44 [-2.18, +3.06] |
| CA address (n=231) | field F1 | 92.40 [90.36, 94.14] | 82.24 | libpostal 93.34 | -0.94 [-2.91, +0.85] |
| CA address | full parse | 76.19 [70.56, 81.39] | 56.71 | libpostal 75.32 | +0.87 [-4.76, +5.63] |
| US names | person token (n=96) | 97.98 [95.06, 100.0] | 97.98 | probablepeople 95.97 | +2.02 [-1.23, +5.58] |
| US names | company token (n=107) | 80.72 [73.53, 86.86] | 68.07 | probablepeople 76.81 | +3.92 [-4.82, +12.00] |
| US names | class (n=203) | 86.21 [81.28, 90.64] | 79.31 | probablepeople 90.15 | -3.94 [-9.36, +1.48] |
| CA names | person token (n=126) | 98.57 [96.43, 100.0] | 98.57 | probablepeople 90.00 | +8.57 [+3.94, +13.67] |
| CA names | company token (n=82) | 75.29 [65.98, 83.33] | 64.64 | probablepeople 78.33 | -3.04 [-12.31, +5.84] |
| CA names | class (n=208) | 85.10 [80.29, 89.42] | 80.77 | probablepeople 85.58 | -0.48 [-5.77, +5.29] |

The python-nameparser person token accuracy (US 100.0, CA 99.64) is above ours on person items, within noise of our rows on the US set. The rules' rows are optimistic because their pre-label seeded the adjudication; the model's are not. No several-people items exist in the sets, so that class is not measured.

Candidates scored (same weights; the policy differs): cand1 (model decides rule-unknown names only): US class 80.30, company token 68.67; CA class 82.21, company token 68.44; addresses as cand2 minus the admin-area repair. cand2 (adds the default-person override): US/CA addresses 97.14 / 86.46 and 92.40 / 76.19, US class 86.21, CA class 85.10. cand3 (adds the admin-area repair, shipped): the rows above. Selection used the development sets only; the repair was added after a unit test failed, not after a score.

Indicative speed and size (one run, loaded machine, AMD Ryzen 9 7950X3D, .NET 10.0.14, OneNLP 0.2.0-preview.1, OneModel 1.0.0; 5,000 development texts, not evaluation text): model load plus first call 149 ms; address parse rules only 47,700/s, with model 5,700/s (21 KB allocated per parse); `AddressParser` 1.0.3 3,260/s (regex, US only, a different task: no recipient, no CA). Name parse rules only 29,000/s, with model 10,300/s; NameParserSharp 125,900/s. Peak working set of the timing process 251 MB (it also holds both comparators and the benchmark inputs); the fast tier alone has not been measured separately. Gate rows run at 1.0.
