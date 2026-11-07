# Third-party notices

OneContact's own code is licensed `MIT OR Apache-2.0`, Copyright (c) Recursive AI, LLC.

Each third-party data source that is packed with a package is listed below with its licence and its pinned upstream version. The SHA-256 of every file as fetched is in the `PIN` file of its folder under `upstream/`. The trained models carry their own notices and provenance in their packages.

## libphonenumber metadata (Apache-2.0)

Compiled into the core package: the phone, short-number and alternate-format metadata of Google's libphonenumber, version 9.0.43 (git commit 86ea33cee5c22ab7e23b3de368e7cc7fa439a2b7), under the Apache License 2.0. The upstream NOTICE and the licence text are packed under `notices/`.

| File | SHA-256 |
|---|---|
| `PhoneNumberMetadata.xml` | `5bbfb6f2e63ba1131fc867cd1cf48aa131047772523723a10c230db12fb6e2df` |
| `ShortNumberMetadata.xml` | `11071b42d43f9ea0bab99a77cd7bf48b302bd28223b754e2ad6ae28ac3f371b9` |
| `PhoneNumberAlternateFormats.xml` | `8645e64f568506abee96308c7f7013bfc864a09aeeecfe9845ba0dc27cfa8093` |
| `phonemetadata.proto` (read as a format document, not packed) | `c7b7e75a7f7944cb6264cc7d96f0a46eed7ec12eecb5aa633a67bc6113821d38` |

The geocoding, carrier and time-zone tables belong to the `.Phones.Geo` package and are listed below.

## libaddressinput address data (Apache-2.0)

Packed in `RecursiveAI.OneContact`: the per-region address data of Google's libaddressinput (`testdata/countryinfo.txt`), compiled into compact tables.

- Source: https://github.com/google/libaddressinput at commit `81eb9628382b07d371d8ea0b11badf7de3857fd5`.
- File as fetched: `testdata/countryinfo.txt`, SHA-256 `51a5da9ac621f3457b96b99de798a47a02004f57e4289c1dd587a95730dad720`; licence text `LICENSE`, SHA-256 `389e6d12ea7853b6e9d797a98d19ddc6ccfaa021f29e21b2ac1ebc68c2607280`.
- Licence: Apache License, Version 2.0 (see `LICENSE-APACHE`). The upstream repository has no NOTICE file. Its README also says the data is available under the Creative Commons Attribution 4.0 International licence; the data is credited here under both.
- Copyright Google LLC and the libaddressinput contributors.
- The packed notice and the provenance record of the build are `notices/libaddressinput.notice.txt` and `notices/libaddressinput.provenance.json`.

## libpostal dictionaries (MIT)

The address expansion tables of `RecursiveAI.OneContact` are compiled from dictionary files of libpostal (https://github.com/openvenues/libpostal), tag v1.1.4 (commit ffafa3fcd687631955453df70906c53d5677ce9a), under the MIT licence, Copyright (c) 2015 openvenues. The files used (English and French; German, Dutch, Italian and Luxembourgish; Spanish, Portuguese, Catalan, Basque, Galician, Maltese and Irish; Danish, Swedish, Finnish, Norwegian (Bokmål), Icelandic, Estonian, Latvian and Lithuanian; Polish, Czech, Slovak, Slovenian, Croatian, Hungarian and Romanian; and the language-independent ambiguous-expansions list), their SHA-256 hashes and the licence text are in `upstream/libpostal-dictionaries/`; the package carries the licence text as `notices/libpostal-dictionaries.notice.txt` and the provenance record as `notices/libpostal-dictionaries.provenance.json`.

## Public Suffix List (MPL-2.0)

Packed with `RecursiveAI.OneContact`: `public_suffix_list.dat` of https://github.com/publicsuffix/list at commit aae37acca5d0f3d09e7c1fe4da9024c514674ac9, byte for byte as published (SHA-256 `762b6cbdd2dacc597b398fcd7860e2184b684125a58aa177dbdf54916a1f3430`), under the Mozilla Public License 2.0. The file is not modified; the licence text is packed under `notices/psl/`. Additions of ours live in a separate file.

## GLEIF entity legal forms (CC0 1.0)

Packed as compiled tables (`gleif-elf`): the ISO 20275 Entity Legal Forms (ELF) code list, version 1.6 (2026-02-19), published by the Global Legal Entity Identifier
Foundation (GLEIF) at https://www.gleif.org/en/lei-data/code-lists/iso-20275-entity-legal-forms-code-list, source file SHA-256 `c55edc421e49ce362457f772d6bfa41f5fc63ecaadea74db5735722625506ef4`.
GLEIF provides this data under the Creative Commons CC0 1.0 Universal public domain dedication (https://creativecommons.org/publicdomain/zero/1.0/).
Credit: Entity Legal Forms code list, GLEIF (gleif.org). The notice is packed with the library as `notices/gleif-elf.notice.txt` next to a provenance record.

## disposable-email-domains (CC0 1.0)

The list of disposable email domains (`disposable_email_blocklist.conf`) of the disposable-email-domains project, https://github.com/disposable-email-domains/disposable-email-domains, commit a72c0cecfcdd940d666265e44c0f661282e2d4aa, file SHA-256 e66aa85a1f16d2afa14cbf8a82517091d6df811cf405e29481238c365649c93e. The project dedicates it to the public domain under CC0 1.0 Universal. Credit: the disposable-email-domains contributors. Compiled into the library as the `Disposable` flag data.

## Unicode data files (Unicode License v3)

Compiled into `RecursiveAI.OneContact`: the IDNA mapping table and the character properties UTS #46 processing needs (`IdnaMappingTable.txt`, `UnicodeData.txt`, `DerivedNormalizationProps.txt`, `DerivedBidiClass.txt`, `DerivedJoiningType.txt`), Unicode Character Database 18.0.0 (https://www.unicode.org/Public/18.0.0/; file hashes in `upstream/unicode-idna/PIN`). Copyright (c) 1991-2026 Unicode, Inc., distributed under the Unicode License v3; the licence text is packed under `notices/unicode/`.

## libphonenumber geocoding, carrier and time-zone data (Apache-2.0)

Compiled into `RecursiveAI.OneContact.Phones.Geo`: the area description (`resources/geocoding/`), carrier name (`resources/carrier/`) and time-zone map (`resources/timezones/map_data.txt`) data files of Google's libphonenumber, version 9.0.43 (commit 86ea33cee5c22ab7e23b3de368e7cc7fa439a2b7), under the Apache License 2.0, Copyright (C) The Libphonenumber Authors. Only data files are used. The file hashes are in `upstream/libphonenumber-geocoding/PIN`, `upstream/libphonenumber-carrier/PIN` and `upstream/libphonenumber-timezones/PIN`; the licence text is packed under `notices/libphonenumber/`.

## Unicode CLDR territory names (Unicode License v3)

Compiled into `RecursiveAI.OneContact.Phones.Geo`: the territory display names of every locale (`cldr-localenames-full/main/*/territories.json`) and the parent-locale table (`cldr-core/supplemental/parentLocales.json`) of the CLDR JSON distribution, release 43 (https://github.com/unicode-org/cldr-json at commit c3349f36f4d4808a0c5e52b63fff16de8b867edf; file hashes in `upstream/cldr-territories/PIN`). Copyright (c) 1991-2023 Unicode, Inc., distributed under the Unicode, Inc. License Agreement for Data Files and Software (the text shipped with the CLDR JSON distribution; the same permissive terms as the Unicode License v3); the licence text is packed under `notices/cldr/`.

## OpenStreetMap (ODbL 1.0), trained into the `RecursiveAI.OneContact.Models` weights

Contains information from OpenStreetMap, which is made available under the Open Database License (ODbL) 1.0, https://opendatacommons.org/licenses/odbl/1-0/. © OpenStreetMap contributors, ODbL 1.0, https://www.openstreetmap.org/copyright. The address-field model of `RecursiveAI.OneContact.Models` was trained in part on OpenStreetMap addresses; the weights are a produced work under ODbL section 4.3 and the data itself is not redistributed. The package's own `THIRD-PARTY-NOTICES.md` and `PROVENANCE.md` list every other source.
