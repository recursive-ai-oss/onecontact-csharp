# IDNA (UTS #46) data and processing

Internationalized host names go through Unicode IDNA Compatibility Processing (UTS #46, non-transitional) the way the URL Standard asks for. Everything is in `src/OneContact/Text/Idna`.

## Inputs

Pinned in `upstream/unicode-idna/PIN` (Unicode Character Database 18.0.0, Unicode License v3, `upstream/unicode-idna/LICENSE`):

| File | Used for |
|---|---|
| `idna/IdnaMappingTable.txt` | status (valid, ignored, mapped, deviation, disallowed) and mapping of every code point |
| `ucd/UnicodeData.txt` | canonical combining class, general category Mark, canonical decompositions |
| `ucd/DerivedNormalizationProps.txt` | `Full_Composition_Exclusion` |
| `ucd/extracted/DerivedBidiClass.txt` | Bidi_Class, for the rules of RFC 5893 |
| `ucd/extracted/DerivedJoiningType.txt` | Joining_Type, for the ContextJ rules of RFC 5892 appendix A |

18.0.0 is the version the pinned web-platform-tests data (2026-11) is generated from: with 17.0.0 seven entries of `IdnaTestV2.json` fail on characters added in 18.0.

The ingest tool `tools/Ingest.Idna` writes them as tables of the compiled-data file (`docs/architecture.md`); all numbers are little-endian:

- `idna.ranges`: 12-byte entries `first u32, last u32, info u32`, sorted; `info = status | length << 3 | offset << 11`, where status is 0 valid, 1 ignored, 2 mapped, 3 deviation, 4 disallowed, and the mapping is `length` UTF-16 units at `offset` (in units) in `idna.map` (UTF-16 little-endian). Code points not listed are disallowed.
- `ccc`, `mark`, `compexcl`, `bidi`, `joining`: 8-byte entries `first u32, (last | value << 24) u32`, sorted, non-overlapping, listing only non-default values (ccc not 0; mark 1; excluded 1; bidi and joining value below).
  - `bidi` value: 0 L, 1 R, 2 AL, 3 EN, 4 ES, 5 ET, 6 AN, 7 CS, 8 NSM, 9 BN, 10 B, 11 S, 12 WS, 13 ON, 14 LRE, 15 LRO, 16 RLE, 17 RLO, 18 PDF, 19 LRI, 20 RLI, 21 FSI, 22 PDI; code points the file does not list count as L.
  - `joining` value: 1 L, 2 R, 3 D, 4 C, 5 T; unlisted is U.
- `decomp`: 12-byte entries `code point, first, second` of canonical decompositions of one or two code points (second is 0 for a singleton), sorted by code point.

## Processing

`Uts46.TryToAscii` and `TryToUnicode` follow section 4 of UTS #46: map; normalize to NFC (our own implementation over the tables, UAX #15, so the result does not depend on the operating system's ICU); break at `.`; convert `xn--` labels with Punycode (RFC 3492) and validate every label (NFC, hyphen rules when asked, no leading mark, status valid or deviation, ASCII rules when STD3 is asked, ContextJ, the Bidi rule of RFC 5893 for Bidi domain names); then Punycode-encode non-ASCII labels. The URL host parser uses `CheckBidi` and `CheckJoiners` on, everything else off.

Inputs longer than 4096 UTF-16 units fail: a hardening limit that is not part of UTS #46.
