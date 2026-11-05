# Phones

Normative notes for the phone core. Public API: `PhoneNumber` (value type) and `PhoneNumberParser` (engine with explicit `PhoneMetadata`) in `RecursiveAI.OneContact.Phones`.

## Metadata ingest

`tools/Ingest.LibPhoneNumber` converts the four pinned upstream files (`upstream/libphonenumber/PIN`: tag v9.0.43, commit 86ea33cee5c22ab7e23b3de368e7cc7fa439a2b7) into the compiled-data resource `OneContact.Data.libphonenumber.ocd` at build time. `tools/fetch/libphonenumber.sh` fetches the files by exact path and checks their SHA-256 against the PIN; the build fails on a missing or altered file. The same input gives byte-identical output.

- Tables `main` (from `PhoneNumberMetadata.xml`), `short` (`ShortNumberMetadata.xml`) and `alt` (`PhoneNumberAlternateFormats.xml`) use one codec (`Phones/Metadata/PhoneMetadataCodec.cs`) over the XML's own structure. Whitespace inside pattern text is stripped, as the files' comments say it is not significant.
- `phonemetadata.proto` is a format document only; nothing is generated from it.
- Territory inheritance: a territory without its own number formats, or with `mainCountryForCode`, resolves exactly as the metadata comments describe; `$NP` and `$FG` in format rules are expanded once at load; international formats fall back to the national format when no `intlFormat` is given.
- A loader for newer metadata: `PhoneMetadata.LoadXml(Stream)`. It reads the same XML format, ignores any DTD (no entity expansion), and enforces bounds: 32 million characters, 4096 territories, 64 K characters per pattern, element nesting 64, repeat count 1000. Violations and malformed input throw `InvalidDataException`; nothing else escapes. All patterns are compiled when the metadata loads.

## Pattern matching

Metadata patterns (about 3,000, plus any the user supplies) run on our own backtracking matcher (`Phones/Metadata/PhoneRegex.cs`) over code points, with a step budget so a hostile pattern cannot run unbounded. It supports the subset the metadata uses and the case modes the parsing rules need (exact, ASCII-insensitive, Unicode-folding). `[GeneratedRegex]` cannot serve patterns that arrive at run time, and `new Regex` is not allowed in the library, so one engine serves both. `bench/OneContact.Bench` holds the comparison against source-generated patterns.

Indicative measurement (BenchmarkDotNet short job, in process, AMD Ryzen 9 7950X3D, 4 cores reserved, machine load 16 to 31 during the run, .NET 10): matching 3 general-description patterns (US, GB, DE) over 5 inputs, 15 whole-input matches per operation, took 994 ns with our matcher and 235 ns with `[GeneratedRegex]`, no allocations in either. Generated patterns are about four times faster per match, so the matcher stays a tuning candidate (a first-character prefilter or a compiled automaton); it is kept because the metadata patterns are data, not source. End to end on the same run: `PhoneNumber.Parse` of a US national number 4.7 µs and 1.1 KB, parse plus international format 5.5 µs and 1.5 KB, parse plus validity of a GB number 5.9 µs and 1.3 KB. These are not publishable rows.

## Parsing

Input classes accepted, each matching the reference's observable behaviour: surrounding junk before the first digit or `+`; punctuation; letters (vanity numbers: three or more letters map by the keypad, after upper-casing); full-width and other Unicode decimal digits; extensions (`ext`, `ext.`, `x`, `;ext=`, `#` and others); RFC 3966 `tel:` with `;ext=` and `phone-context` (global digits or a domain name; an empty context is refused); international prefixes of the default region; national prefixes and carrier-selection codes of the default region.

- No region: only `+`-prefixed text parses; anything else fails with `phone.region-required`. A well-formed region with no metadata fails with `phone.unknown-region`.
- Text longer than 250 characters fails with `phone.too-long` before anything else.
- `PhoneNumber.Source` is the extent of the number after leading junk is skipped (for `tel:`/`phone-context` input, the whole trimmed text).
- String overloads keep the raw input (needed by original-format output and carrier codes); span overloads do not unless `keepRawInput` is set.
- Reason codes: `phone.not-a-number`, `phone.invalid-country-code`, `phone.too-short-after-idd`, `phone.too-short`, `phone.too-long`, `phone.unknown-region`, `phone.region-required`; empty input uses the shared `empty` code.

## Canonical form, key, flags

- Canonical: `+<country code><national number>`, then `;ext=<extension>` when there is one (the RFC 3966 parameter form).
- Key, `KeyVersion.V1`: `+<country code><national number>` then `x<extension>` when there is one. The key never depends on the formatting of the input.
- Flags (`PhoneFacts`): `Valid`, `HasExtension`, `Vanity`, `InternationalInput`, `NonGeographic`.

## Operations

Validity, validity for a region, region of a number, number type, possibility (`PhonePossibility`: possible, local only, invalid country code, too short, invalid length, too long; also per number type), formats (E.164, international, national, RFC 3966, out-of-country from a calling region, for mobile dialing with and without formatting, original format, national with the carrier code), example numbers per region and type, supported types per region, geographical area-code and national-destination-code lengths, national prefix and international prefixes of a region. Batch form: `ParseMany`.

## Behaviours settled by experiment against the pinned reference

The metadata and its comments leave these open; each was settled by feeding crafted input to the released reference jar and recording the output. All are covered by the parity run.

- A region's preferred international prefix wins over the plain one when formatting out-of-country (Russia gives `8~10`).
- An empty national prefix counts as none.
- The supported types of a region never list "fixed line or mobile"; the metadata's shared pattern lists both.
- Only ASCII digits and mapped letters pass through the keypad mapping used for vanity numbers; other scripts' digits count as digits elsewhere. Upper-casing precedes the mapping, so dotless i and long s map to the keypad 4 and 7; the Kelvin sign is not mapped.
- The vanity test is ASCII-case-insensitive; the viability and extension tests fold Unicode case.
- Local-only lengths of a general description are the union of all type lengths minus the national lengths.
- A number whose national part is all zeros keeps and echoes its raw input when formatted; E.164 prepends `+<country code>` only when the code came from the default region.
- A well-formed but unknown region and an unknown country code both report an invalid country code in the reference; we keep two reason codes and the harness maps both.
- Possibility counts a local-only length as possible for the yes/no question.

## Parity method

`tools/Oracles/Phones` generates cases per region from the metadata's example numbers and patterns plus mutations (prefixes, punctuation, extensions, wrong lengths, other regions' numbers, `tel:` URIs, letters, Unicode digits, garbage), runs them through a small Java driver of ours that calls the jar's public API, runs the same cases through the C# engine, and diffs per operation and region. The jar and a user-local JDK are black boxes on the worker; see `tools/Oracles/Phones/README.md`.
