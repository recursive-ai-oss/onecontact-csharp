# Models

Normative notes on how trained models reach the parsers, what a model package carries, and how a model is trained. Rules first: a model only fills what the rules leave open.

## What a model does and does not do

- **Addresses.** Rules read the last line (postal code, state or province, city), the delivery line and the country (`docs/address-rules-us-ca.md`). Input the rules refuse with `address.model-required`, `address.no-last-line` or `address.no-delivery-line` goes to the address model, which labels the words of the whole text; the parser turns the labels into the same `ParsedAddress` the rules make. When the rules succeed, every field they found stays; the model only labels the lines the rules keep as written (recipient, organization) where it can. A model field says `DecisionSource.Model` in `ParseDetail` with the model's confidence; a rule field says `Rule` with confidence 1. The standard form of a model field uses the authority tables where they have an entry (state, province, street type, directional, unit designator) and the upper-cased words otherwise. Nothing is invented: a field the model did not label is absent, and the country must come from a country name, the state or province, the postal code or the hint.
- **Names.** Rules classify a string as person, company, several people or unknown (`docs/names.md`). A string the rules leave `Unknown` goes to the name model. Its class counts when more than half of the words (weighted by their probability) vote for it. A person needs a given or a family name from the model's parts; a company is read by the company rules with the class decided by the model; several people are reported as a class only, because the model labels words and does not split people. A class or part a rule found is never replaced.
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
