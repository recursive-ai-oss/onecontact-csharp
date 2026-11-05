# Changelog

All notable changes are recorded here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Repository skeleton: the `RecursiveAI.OneContact` and `RecursiveAI.OneContact.Phones.Geo` projects, placeholders for the model and extraction packages, tests, an ingest harness, a benchmark project, a fuzz host shape and a NativeAOT smoke app.
- Shared contracts: the six value types (`PhoneNumber`, `EmailAddress`, `WebAddress`, `PostalAddress`, `PersonName`, `CompanyName`) with `Parse`/`TryParse`, canonical forms, keys by `KeyVersion`, flags and offsets; `Region`; `ParseDetail` and reason codes; `Contacts.Normalize` and `Contacts.SameKey`; System.Text.Json converters.

## Models (unreleased)

- `RecursiveAI.OneContact.Models.Tier1`: a shared fast-tier encoder with address-field, name-part and name-class heads for the United States and Canada, registered by a module initializer the package adds to the referencing project.
- Core: `ContactModels`, `IAddressFieldModel`, `INameModel`; the address and name parsers use a registered model only where the rules leave a decision open, and `ParseDetail` says per field whether a rule or a model decided it.
- `address.model-required` now names the package to add.
