# RecursiveAI.OneContact.Models.Accurate

libphonenumber- and libpostal-class contact data for .NET: the accurate-tier model package.

The same heads as the fast tier (address fields, name parts and class, and the contact-span recognizer that finds people, companies, addresses, phones, emails and websites in running text), trained over OneNLP's distilled multilingual encoder instead of the small fast-tier encoder. Reference it next to `RecursiveAI.OneContact` (and `RecursiveAI.OneContact.Extraction`) and it registers itself; no download, no network. Rules run first; the model reads only what the rules leave open and never replaces a rule hit.

- The head weights (about 10 MB) are packed in this assembly. The encoder (136 MB, a six-layer transformer, width 384) comes from the dependency `RecursiveAI.OneNlp.Models.Multilingual.FrozenEncoder` and is read on first use.
- Combined with `RecursiveAI.OneContact.Models.Tier1` or `RecursiveAI.OneContact.Models`: the fast tier keeps normalizing single addresses and names, and this package reads running text. Alone, it serves all three.
- Provenance, seeds, data and hashes: <https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/src/OneContact.Models.Accurate/PROVENANCE.md>
- Behaviour and costs: <https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/docs/models-accurate.md>
- Third-party notices: <https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/src/OneContact.Models.Accurate/THIRD-PARTY-NOTICES.md>
- Home: <https://recursiveai.org/onecontact/>

## Licence and data

The code and the head weights are licensed `MIT OR Apache-2.0`, with every training source credited in the package's `THIRD-PARTY-NOTICES.md`. Copyright (c) Recursive AI, LLC. The encoder dependency has share-alike lineage (Wikipedia text), credited in its own notices.

© OpenStreetMap contributors, ODbL 1.0. The address head was trained in part on OpenStreetMap addresses, which are made available under the Open Database License: <https://opendatacommons.org/licenses/odbl/1-0/>, <https://www.openstreetmap.org/copyright>. The weights are a produced work of that data (ODbL section 4.3); the data itself is not redistributed.

The weights that use no OpenStreetMap data can be rebuilt from the committed recipe (`recipes/accurate`, `run.sh` without an OpenStreetMap folder).
