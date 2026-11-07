# Changelog

All notable changes are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Repository skeleton: the `RecursiveAI.OneContact` and `RecursiveAI.OneContact.Phones.Geo` projects, placeholders for the model and extraction packages, tests, an ingest harness, a benchmark project, a fuzz host shape and a NativeAOT smoke app.
- Shared contracts: the six value types (`PhoneNumber`, `EmailAddress`, `WebAddress`, `PostalAddress`, `PersonName`, `CompanyName`) with `Parse`/`TryParse`, canonical forms, keys by `KeyVersion`, flags and offsets; `Region`; `ParseDetail` and reason codes; `Contacts.Normalize` and `Contacts.SameKey`; System.Text.Json converters.

## Tier 2 (unreleased)

- Addresses: authority standardization, expansions and rule-based parsing for Mexico, Australia, the United Kingdom with its Crown dependencies, the Latin-script EU, Switzerland, Norway, Iceland, Liechtenstein, Andorra, Monaco, San Marino and the Vatican; country inference without a hint from the evidence the text carries.
- `RecursiveAI.OneContact.Models`: the fast-tier encoder and heads for the United States, Canada and the Tier 2 countries; it is preferred over `.Models.Tier1` when both are referenced.
- Extraction: people and organizations read by rules from visible text, CRM notes and signatures.
- Faster phone parsing, formatting and matching, URL and email parsing, and public-suffix lookups.

## Models (unreleased)

- `RecursiveAI.OneContact.Models.Tier1`: a shared fast-tier encoder with address-field, name-part and name-class heads for the United States and Canada, registered by a module initializer the package adds to the referencing project.
- Core: `ContactModels`, `IAddressFieldModel`, `INameModel`; the address and name parsers use a registered model only where the rules leave a decision open, and `ParseDetail` says per field whether a rule or a model decided it.
- `address.model-required` now names the package to add.
