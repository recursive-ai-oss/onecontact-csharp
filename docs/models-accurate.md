# The accurate tier

> **From 1.1.** The model packages described here ship in 1.1, after OneNLP 1.0. Version 1.0 runs on rules and markup alone; the seam in core that registers a model (`ContactModels`) is part of 1.0.

`RecursiveAI.OneContact.Models.Accurate` is the opt-in model package for harder text. It carries the same four heads as the fast tier on one frozen encoder: OneNLP's distilled multilingual student (`RecursiveAI.OneNlp.Models.Multilingual.FrozenEncoder`, six layers, width 384, 113 languages).

## Using it

Reference the package; it registers itself through a source file the package adds to your project (no reflection, no call from you). `AccurateModel.Instance` is the loaded model; `AccurateModel.Load(stream)` loads a retrained head file.

- **Which package serves what.** With several model packages referenced, the fast tier (`.Models.Tier1`, `.Models`) keeps normalizing single addresses and names, and the accurate tier serves the contact-span model that reads running text for `ContactExtractor`. With the accurate tier alone it serves addresses, names and spans. This is `ContactModels.Register(..., normalizationRank: 0)`.
- **Rules first.** Nothing changes about precedence: a rule hit is never overruled, and `ParseDetail` says whether a rule or the model decided each field.
- **No model referenced.** An address the rules cannot decide fails with `address.model-required`, whose message names the packages to add, this one included.

## Cost

The encoder is a transformer run on OneModel's CPU kernels, so the tier is slower and larger than the fast tier: about 140 MB of encoder files, about 0.8 GB resident once the encoder is loaded, and tens of milliseconds for a short text instead of fractions of one. Measured figures for maintainers are in `docs/maintainers/models-accurate.md`. Use the fast tier when you read single addresses and names in bulk.

## OneNLP version

This package is built on OneNLP 0.4. A project that references it and a fast-tier package must resolve one OneNLP version; the fast tiers are retrained against 0.4 for that.

## Retraining

`recipes/accurate` trains the heads with OneNLP's public `Train` over the frozen encoder, in resumable chunks on CPU: `run.sh` (address head), `names.sh` (name heads), `spans.sh` (span head). Its README lists the inputs. Provenance: `src/OneContact.Models.Accurate/PROVENANCE.md`.
