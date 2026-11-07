# RecursiveAI.OneContact.Models.Tier1

libphonenumber- and libpostal-class contact data for .NET.

The trained models for free-text address and name parsing in the United States and Canada (about 10 MB). Add the package next to `RecursiveAI.OneContact`; it registers itself, and the core parsers use it where their rules leave a decision open. A model never overrules a rule hit, and `ParseDetail` says per field whether a rule or the model decided it.

- One small shared encoder with an address-field head, a name-part head and a name-class head, plus a span head that `RecursiveAI.OneContact.Extraction` uses on plain text. Run by OneNLP and OneModel in managed code; no native runtime.
- For Tier 2 countries add `RecursiveAI.OneContact.Models`, which wins when both are referenced.
- Nothing is downloaded: the weights are in the package.
- The training data, registers and licences are listed in the package's provenance and notices.

Documentation: https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/docs/models.md. Changelog: https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/CHANGELOG.md. Source: <https://github.com/recursive-ai-oss/onecontact-csharp>

Licensed `MIT OR Apache-2.0`. Copyright (c) Recursive AI, LLC. Notices: https://github.com/recursive-ai-oss/onecontact-csharp/blob/main/src/OneContact.Models.Tier1/THIRD-PARTY-NOTICES.md
