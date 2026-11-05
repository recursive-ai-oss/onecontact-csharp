# Third-party notices

OneContact's own code is licensed `MIT OR Apache-2.0`, Copyright (c) Recursive AI, LLC.

Each third-party data file that is packed with a package is listed below with its licence, its pinned upstream version and the SHA-256 of the file as fetched. A section is filled in when the data it covers is first packed; until then the sections are empty and no such data ships.

## libphonenumber metadata (Apache-2.0)

None packed yet. When packed: `PhoneNumberMetadata.xml`, `ShortNumberMetadata.xml` and the geocoding, carrier and time-zone tables of Google's libphonenumber, under the Apache License 2.0, with the upstream NOTICE.

## libaddressinput address data (Apache-2.0)

Packed in `RecursiveAI.OneContact`: the per-region address data of Google's libaddressinput (`testdata/countryinfo.txt`), compiled into compact tables.

- Source: https://github.com/google/libaddressinput at commit `81eb9628382b07d371d8ea0b11badf7de3857fd5`.
- File as fetched: `testdata/countryinfo.txt`, SHA-256 `51a5da9ac621f3457b96b99de798a47a02004f57e4289c1dd587a95730dad720`; licence text `LICENSE`, SHA-256 `389e6d12ea7853b6e9d797a98d19ddc6ccfaa021f29e21b2ac1ebc68c2607280`.
- Licence: Apache License, Version 2.0 (see `LICENSE-APACHE`). The upstream repository has no NOTICE file. Its README also says the data is available under the Creative Commons Attribution 4.0 International licence; the data is credited here under both.
- Copyright Google LLC and the libaddressinput contributors.
- The packed notice and the provenance record of the build are `notices/libaddressinput.notice.txt` and `notices/libaddressinput.provenance.json`.

## libpostal dictionaries (MIT)

The address expansion tables of `RecursiveAI.OneContact` are compiled from dictionary files of libpostal (https://github.com/openvenues/libpostal), tag v1.1.4 (commit ffafa3fcd687631955453df70906c53d5677ce9a), under the MIT licence, Copyright (c) 2015 openvenues. The files used (English, French and the language-independent ambiguous-expansions list), their SHA-256 hashes and the licence text are in `upstream/libpostal-dictionaries/`; the package carries the licence text as `notices/libpostal-dictionaries.notice.txt` and the provenance record as `notices/libpostal-dictionaries.provenance.json`.

## Public Suffix List (MPL-2.0)

None packed yet. When packed: `public_suffix_list.dat`, byte for byte as published, under the Mozilla Public License 2.0, with the licence notice. Additions of ours live in a separate file.

## GLEIF entity legal forms (CC0 1.0)

None packed yet. When packed: the ISO 20275 Entity Legal Form code list published by GLEIF, dedicated to the public domain under CC0 1.0, with a credit line.

## disposable-email-domains (CC0 1.0)

None packed yet. When packed: the domain list of the `disposable-email-domains` project, dedicated to the public domain under CC0 1.0, with a credit line.
