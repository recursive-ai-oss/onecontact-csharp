# OneContact documentation

Start with the [README](../README.md) for the packages, the quick start and the results. Coming from another library? Read [migrating.md](migrating.md) first. The rest of this folder is ordered the way you will probably need it. Documents marked *maintainer* describe how the project is built and measured, not how to use it.

## Using the library

### Phones
- [phones.md](phones.md): parsing, validation, number types, every format, the as-you-type formatter, the matcher and its leniency levels, short numbers, example numbers, the metadata and how to load newer metadata.
- [phones-geo.md](phones-geo.md): the geocoder, carrier and time-zone mappers (`RecursiveAI.OneContact.Phones.Geo`), with the original-carrier caveat.

### Email
- [email.md](email.md): what `EmailAddress` reads (RFC 5322, RFC 6531), the canonical form, flags (role account, free provider, disposable), the key rules per provider, typo suggestions.

### Web addresses
- [web.md](web.md): `WebAddress`, WHATWG URL parsing, canonical form, tracking parameters, the key, social-profile flags.
- [psl.md](psl.md): the Public Suffix List lookups and how our additions sit beside the unmodified list.
- [idna.md](idna.md): internationalized host names (UTS #46).

### Addresses
- [address-structural.md](address-structural.md): the worldwide structural layer: fields, layout, required fields, postcode patterns, `Validate()`.
- [address-rules-us-ca.md](address-rules-us-ca.md): United States and Canada: parsing and the postal authorities' styles (USPS Publication 28, Canada Post).
- Tier 2 countries, one document per group of postal authorities: [address-rules-tier2-a.md](address-rules-tier2-a.md) (FR, BE, NL, LU, DE, AT, CH, LI, MC), [address-rules-tier2-b.md](address-rules-tier2-b.md) (GB and Crown dependencies, IE, ES, PT, IT, MT, SM, VA, AD), [address-rules-tier2-c-north.md](address-rules-tier2-c-north.md) (DK, SE, FI, NO, IS, EE, LV, LT), [address-rules-tier2-c-south.md](address-rules-tier2-c-south.md) (PL, CZ, SK, SI, HR, HU, RO, MX, AU).
- [address-last-line-orders.md](address-last-line-orders.md): how the last line (postcode, town, region) is read in each country's order.
- [address-country-undetermined.md](address-country-undetermined.md): inferring the country from the text, and what is reported when several fit.
- [address-dictionaries.md](address-dictionaries.md) and [address-keys.md](address-keys.md): expansion dictionaries and the address key.

### Names and companies
- [names.md](names.md): person names: hints, classes, parts, casing restoration, order, multi-person splits.
- [name-keys.md](name-keys.md): the person key.
- [company-names.md](company-names.md): company names, ISO 20275 legal forms with ELF codes, DBA and c/o, the company key.

### Extraction
- [extraction.md](extraction.md): `ContactExtractor`: structured markup, visible text, obfuscation decoding, records, site merge, disagreements, limits. It carries the data-protection note.

### Models
- [models.md](models.md): how a model package reaches the parsers, what the Tier 1 model is, how it was trained.
- [models-superset.md](models-superset.md): the `RecursiveAI.OneContact.Models` package (United States, Canada and the Tier 2 countries).

### Keys, data and notices
- Keys are described next to each kind: [phones.md](phones.md), [email.md](email.md), [web.md](web.md), [address-keys.md](address-keys.md), [name-keys.md](name-keys.md), [company-names.md](company-names.md). Any change to key rules ships as a new `KeyVersion`; a published version never changes.
- [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md): the licences and credits of the data the packages carry.
- `upstream/<source>/PIN`: the exact upstream release and file hashes behind each compiled data table.

## Maintainers and evaluation

- [architecture.md](architecture.md) *(maintainer)*: how code and compiled data fit together, the data file format, how value types are shaped.
- [tools-environment.md](tools-environment.md) *(maintainer)*: where the tools keep data, models and evaluation sets.
- [evaluation-sets.md](evaluation-sets.md) and [extraction-eval-sets.md](extraction-eval-sets.md) *(evaluation)*: how the real-text evaluation sets are built, labelled and scored. The sets themselves are private; only aggregate numbers are published.
- [models-challenger.md](models-challenger.md) *(evaluation)*: the address-field challenger models measured against the shipped weights.
- Training data *(maintainer)*: [training-data-address-us-ca.md](training-data-address-us-ca.md), [training-data-address-tier2-a.md](training-data-address-tier2-a.md), [training-data-address-tier2-b.md](training-data-address-tier2-b.md), [training-data-address-tier2-c.md](training-data-address-tier2-c.md), [training-data-address-challenger.md](training-data-address-challenger.md): which open registers are rendered into training examples, under which licences.
