# Company names

`CompanyName` and `CompanyNameParser` read a company name into: leading article ("The"), base name, legal form (with its ISO 20275 ELF code and a canonical abbreviation),
"trading as" part and "c/o" part. Every part carries offsets into the input. Rules only; the legal form comes from the compiled GLEIF list, so a new jurisdiction is a data update.
Nothing is invented: a form that fits several codes has no code (see Candidates), and the base name is never completed or corrected.

## The ELF data set

Source: the GLEIF Entity Legal Forms code list (ISO 20275), version 1.6 of 2026-02-19, CSV, pinned in `upstream/gleif-elf/PIN` with its SHA-256 (4002 rows, 3599 codes, 130 country codes).
Licence: CC0, as GLEIF states on its open-data and LEI-data terms pages; the credit line is in `THIRD-PARTY-NOTICES.md` and in the notice packed with the library.
`tools/fetch/gleif-elf.sh` fetches the file by exact URL and checks the hash; the ingest tool `tools/Ingest.GleifElf` checks the CSV header names, then writes the tables below at build time. The same pinned file gives byte-identical tables.

Table `forms`: a 32-bit little-endian row count, then per row one byte `active` (1 for ACTV, 0 for INAC) and eight strings, each a 16-bit little-endian byte length followed by UTF-8:
code, country code, sub-division code, local name, language code, transliterated name, local abbreviations, transliterated abbreviations (abbreviations separated by `;`).
Rows are sorted by country, code, language, then file order. Table `version` holds the list version text.

## Matching

A legal form is looked up in an index of the folded text (see docs/name-keys.md for folding) of every name and abbreviation, plus our own alias list for spellings people use every day that the list does not carry (CZ/SK `s.r.o.`, `a.s.`; PT `Lda`; IE `DAC`; FR `SARL`; ES `S.A.`, ...).
Each alias names a listed form of its country; one that no longer names a listed form is skipped, never fails the load. The aliases are ours, written from the forms' published names.

- The form is taken from the end of the name, longest match first. A prefix form is taken only if it is an abbreviation of the hinted country, or one of a few unambiguous prefix abbreviations (`UAB`, `SIA`, `ZAO`, ...).
- Forms of at most two letters need capitals or real dotted letters (`S.A.`, `A/S`), or a country hint, so that a word such as "as" or "sa" is not a form.
- `d/b/a` and `DBA` are trading-as markers, never legal forms.
- A hinted country restricts the candidates to its rows when it has any; if it has none, the result says the form lies outside the hinted country (`company.legal-form-outside-country`).
- Inactive (INAC) rows are used only if no active row matches (`company.legal-form-inactive`).

## Candidates and the ELF code

The result lists every distinct code the written form can mean (`Candidates`: code, country, name, active). The ELF code is set only when exactly one code remains; otherwise it is empty and the info issue `company.legal-form-ambiguous` is reported.

## Canonical abbreviation

For a single code the abbreviation is taken from the rows that carry the written form: the one with the fewest letters; among equals mixed case over capitals when it is longer than three letters ("Pty Ltd" over "PTY LTD"), then fewer punctuation marks, then a capital initial.
The canonical text replaces what was written ("Limited" gives "Ltd", "Aktiengesellschaft" gives "AG"), except when the two differ only in case ("Oy" is not rewritten to "oy"). A composite form ("GmbH & Co. KG") is kept as typed. With several codes the common abbreviation is used if there is one.

## Other parts

- **Leading "The"**: split off when more words follow. It is not part of the base or the key.
- **Trading as**: `d/b/a`, `dba`, `doing business as`, `t/a`, `trading as`, `trading under`; the canonical text writes `d/b/a` or `t/a`. An empty trading name is `company.empty-trading-name`.
- **Care of**: `c/o`, `care of`. A text that starts with `c/o` and holds nothing before it is read as the company after it (`company.care-of-only`).
- A comma between words is kept ("Smith, Jones & Partners"), a comma before the legal form is not.

## Company key V1

`base | token`: the base name folded to lower-case words (an ampersand is the word "and"; the leading "The", the trading name and the care-of part are left out), a bar, then the token of the legal form:

- the ELF code when the written form maps to one code;
- otherwise `~` followed by the folded common canonical abbreviation, with English synonyms unified ("Incorporated" and "Inc" give `~inc`, "Limited" and "Ltd" give `~ltd`), computed without any hint so that the key never depends on one;
- nothing when there is no form.

Examples (fixtures in `NameRuleTests`): `The ACME Muster GmbH & Co. KG` gives `acme muster|8Z6G`; "Acme Holdings Ltd" and "ACME HOLDINGS LIMITED" share a key; "Acme" and "Acme Ltd" do not.
A published version's output never changes; a rule change ships as a new `KeyVersion`.
