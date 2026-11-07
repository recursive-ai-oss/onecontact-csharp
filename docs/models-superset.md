# The Tier 1 + Tier 2 model (`RecursiveAI.OneContact.Models`)

The same design as the Tier 1 package (see `models.md`): one fast-tier encoder shared by an address-field head, a name-part head, a name-class head and a contact-span head, trained with OneNLP's `Train` on OneModel. It covers the United States, Canada and every Tier 2 country. Rules run first; the model reads only what the rules leave open, and never replaces a rule hit. `ParseDetail` says per field whether a rule or the model decided it.

- **Size.** About 23 MB of weights, embedded in the package. Load time and memory are reported with the results at each release.
- **Resolution.** When both model packages are referenced, this one wins. The Tier 1 package never reads Tier 2 text; this package reads US and Canada text as Tier 1 does. Without a model package, a Tier 2 address the rules refuse stays refused, and the error names both packages.
- **Country.** The model labels fields; it does not tell countries apart. A Tier 2 read takes the country from the hint, or from the country the rules had decided before they refused the address, and is otherwise refused as country-undetermined. A model read of text with no hint never displaces a Tier 2 rule reading, and is not made a United States or Canada address on a postal code alone (a German, Spanish or Czech postcode looks like a ZIP): it needs a state or province. A wrong country would be invented content, so the refusal is the decision.
- **Names.** For Tier 2 texts the name heads read only what the rules refuse, except in the countries where the model's person reading is allowed to replace the rules' "person for want of company evidence" default (a confidence of at least 0.8). US and Canada names follow Tier 1.
- **Dependent locality** (the settlement or sector between street and postcode) is a label of the address head and its own field and line in a model read. Where the rules read such a line themselves, the model is not asked.
- **Single encode.** A name is encoded once for its class and part heads; the outputs equal those of two encodes.
- **Training data and licences.** The default weights are trained on open national address registers mixed with OpenStreetMap addresses (© OpenStreetMap contributors, ODbL 1.0), national name lists, and company names from GLEIF, Companies House and SEC EDGAR. The weights ship `MIT OR Apache-2.0` with every source credited: see the package's `PROVENANCE.md` and `THIRD-PARTY-NOTICES.md`. A recipe that rebuilds a model from the permissive registers alone is `recipes/models`.
- **Retraining.** `recipes/models` (train and span stages) is a console project over OneNLP's public `Train`.

Measured rows (development splits, real-page sets, candidates) are kept in `docs/maintainers/models-superset.md`.
