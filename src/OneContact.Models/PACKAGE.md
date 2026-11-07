# RecursiveAI.OneContact.Models

libphonenumber- and libpostal-class contact data for .NET: the Tier 1 + Tier 2 model package.

A small trained model (about 23 MB) for free-text address and name parsing in the United States, Canada and 37 Tier 2 countries. One shared fast-tier encoder with an address-field head, a name-part head, a name-class head and a contact-span head, trained with OneNLP and run on OneModel. Rules run first; the model reads only what the rules leave open and never replaces a rule hit. Reference it next to `RecursiveAI.OneContact` and it registers itself; no download, no network.

- Provenance, seeds, data and hashes: <https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/src/OneContact.Models/PROVENANCE.md>
- Measured rows and the recipe: <https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/docs/models-superset.md> and <https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/docs/models-challenger.md>
- Third-party notices: <https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/src/OneContact.Models/THIRD-PARTY-NOTICES.md>
- Changelog: <https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/CHANGELOG.md>
- Home: <https://recursiveai.org/onecontact/>

## Licence and data

The weights are licensed `MIT OR Apache-2.0`, with every training source credited in the package's `THIRD-PARTY-NOTICES.md`. Copyright (c) Recursive AI, LLC.

© OpenStreetMap contributors, ODbL 1.0. The address model was trained in part on OpenStreetMap addresses, which are made available under the Open Database License: <https://opendatacommons.org/licenses/odbl/1-0/>, <https://www.openstreetmap.org/copyright>. The weights are a produced work of that data (ODbL section 4.3); the data itself is not redistributed.

The weights that use no OpenStreetMap data can be rebuilt from the committed recipe (`recipes/models`, `run.sh` without an OpenStreetMap folder).
