# Third-party notices

OneContact's own code is licensed `MIT OR Apache-2.0`, Copyright (c) Recursive AI, LLC.

Each third-party data file that is packed with a package is listed below with its licence, its pinned upstream version and the SHA-256 of the file as fetched. A section is filled in when the data it covers is first packed; until then the sections are empty and no such data ships.

## libphonenumber metadata (Apache-2.0)

Compiled into the core package: the phone, short-number and alternate-format metadata of Google's libphonenumber, version 9.0.43 (git commit 86ea33cee5c22ab7e23b3de368e7cc7fa439a2b7), under the Apache License 2.0. The upstream NOTICE and the licence text are packed under `notices/`.

| File | SHA-256 |
|---|---|
| `PhoneNumberMetadata.xml` | `5bbfb6f2e63ba1131fc867cd1cf48aa131047772523723a10c230db12fb6e2df` |
| `ShortNumberMetadata.xml` | `11071b42d43f9ea0bab99a77cd7bf48b302bd28223b754e2ad6ae28ac3f371b9` |
| `PhoneNumberAlternateFormats.xml` | `8645e64f568506abee96308c7f7013bfc864a09aeeecfe9845ba0dc27cfa8093` |
| `phonemetadata.proto` (read as a format document, not packed) | `c7b7e75a7f7944cb6264cc7d96f0a46eed7ec12eecb5aa633a67bc6113821d38` |

The geocoding, carrier and time-zone tables belong to the `.Phones.Geo` package and are listed there when packed.

## libaddressinput address data (Apache-2.0)

Packed in `RecursiveAI.OneContact`: the per-region address data of Google's libaddressinput (`testdata/countryinfo.txt`), compiled into compact tables.

- Source: https://github.com/google/libaddressinput at commit `81eb9628382b07d371d8ea0b11badf7de3857fd5`.
- File as fetched: `testdata/countryinfo.txt`, SHA-256 `51a5da9ac621f3457b96b99de798a47a02004f57e4289c1dd587a95730dad720`; licence text `LICENSE`, SHA-256 `389e6d12ea7853b6e9d797a98d19ddc6ccfaa021f29e21b2ac1ebc68c2607280`.
- Licence: Apache License, Version 2.0 (see `LICENSE-APACHE`). The upstream repository has no NOTICE file. Its README also says the data is available under the Creative Commons Attribution 4.0 International licence; the data is credited here under both.
- Copyright Google LLC and the libaddressinput contributors.
- The packed notice and the provenance record of the build are `notices/libaddressinput.notice.txt` and `notices/libaddressinput.provenance.json`.

## libpostal dictionaries (MIT)

None packed yet. When packed: the dictionary files of libpostal for the supported languages, under the MIT licence, with its copyright notice.

## Public Suffix List (MPL-2.0)

None packed yet. When packed: `public_suffix_list.dat`, byte for byte as published, under the Mozilla Public License 2.0, with the licence notice. Additions of ours live in a separate file.

## GLEIF entity legal forms (CC0 1.0)

None packed yet. When packed: the ISO 20275 Entity Legal Form code list published by GLEIF, dedicated to the public domain under CC0 1.0, with a credit line.

## disposable-email-domains (CC0 1.0)

The list of disposable email domains (`disposable_email_blocklist.conf`) of the disposable-email-domains project, https://github.com/disposable-email-domains/disposable-email-domains, commit a72c0cecfcdd940d666265e44c0f661282e2d4aa, file SHA-256 e66aa85a1f16d2afa14cbf8a82517091d6df811cf405e29481238c365649c93e. The project dedicates it to the public domain under CC0 1.0 Universal. Credit: the disposable-email-domains contributors. Compiled into the library as the `Disposable` flag data.
