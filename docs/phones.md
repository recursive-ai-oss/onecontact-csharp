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
- The letters of an extension label never make a number a vanity number: for `tel:+12025550100;ext=204`, `+12025550100 ext. 204` and `1-800-FLOWERS ext 5` the reference's `isAlphaNumber` answers false, false and true (the last because of FLOWERS). The `Vanity` fact is decided after the extension is cut off. (`tools/Oracles/Phones/AlphaProbe.java`; the reference also answers false for a whole `tel:` string with letters, because that text is not a viable number for that call; the fact here looks at the number only.)

## As-you-type formatting

`AsYouTypeFormatter` (namespace `RecursiveAI.OneContact.Phones`) follows one number being typed. `InputDigit(char)` returns the text to show after each key press; `InputDigitAndRememberPosition(char)` additionally marks the key for `RememberedPosition`, the index in the last output just after that digit; `Clear()` starts again. The formatter never validates and never throws on odd input.

- Digits of any script count; a plus sign counts only as the first key. Any other key stops formatting: from then on the output is the input exactly as typed, until `Clear()`.
- The first two keys echo. At the third the formatter tries the region's international prefix, then a national prefix, then picks the region's formats by the leading digits of the number (three digits narrow the candidates, each further digit narrows them again with the format's later leading-digits patterns) and fills the first format's template, whose room is the longest number the format can match. When a typed number outgrows its template the next candidate format takes over.
- A plus sign or an international prefix switches to the international formats of the region and then, once the calling code is read, of the region that owns it. Typing the national prefix first keeps it in front of the groups. A national prefix found late (Madagascar's seven-digit pattern) is taken out and formatting restarts after it.
- The formatter works from the main region of a calling code, so Canada formats with the NANPA data.

Settled by experiment against the pinned reference (the data files do not say):

- In a region that uses a national prefix, a number typed **without** the prefix may use formats whose national-prefix rule is empty or is only the first group, and formats that make the prefix optional; a number typed **with** the prefix may use formats that make it optional or that carry a national-prefix or carrier-code rule. A number with an international prefix may use any. (United Arab Emirates "0800..." stays unformatted, "800..." is grouped; Bolivia groups "022..." through the carrier-code rule; the United States seven-digit format is skipped after a typed "1".)
- Typing the NANPA trunk "1" keeps national formats; it does not switch to international ones.
- Gabon has a national prefix pattern but no prefix of its own and behaves like a region with a prefix.
- A full-width plus sign at the start is accepted as a key but never becomes a plus; it is carried as an unusable character.

## Matcher

`PhoneNumberMatcher` (and the happy-path `PhoneNumber.FindAll(text, region, leniency)`) finds numbers in running text. A candidate is a run of digits with up to four punctuation characters between digit groups, optionally led by brackets or a plus sign and followed by an extension; candidates that look like slash-separated dates, time stamps (`2012-12-22 20:12` followed by `:30`), page ranges or unbalanced brackets are dropped. A candidate that fails is split at slashes, bracketed starts, dashes, dots and spaces and the pieces are tried. `maxTries` counts rejected candidates and pieces.

Leniency, each level including the one before:

| Level | Accepts |
|---|---|
| `Possible` | parses, and the length is plausible for the region |
| `Valid` | valid; not preceded or followed by Latin letters or a currency sign or `%`; `x` only as an extension or carrier-code marker; the national prefix present where the region's format asks for it |
| `StrictGrouping` | valid and not more than one slash inside the national number, and each group of digits of the region's national format (or of an alternate format in the data) stays together in order; separators between groups may be missing |
| `ExactGrouping` | the groups are exactly the region's (or an alternate format's), from the end; the first may follow a national prefix |

Alternate formats (`PhoneNumberAlternateFormats.xml`) are applied wherever their pattern matches inside the national number, not only when it covers it, which the reference does too (observed: Cambodia's `(2)(2)(2)(2-3)` alternate groups the first nine digits of a ten-digit number and leaves the tenth). The alternates are the library's own data even when a parser is built over user metadata.

Matches carry `Start`, `End`, the raw text and the parsed number (with its raw input kept). The low-level `TryFindNext(span, region, leniency, ref index, ref triesLeft, out match)` continues a search without allocating an iterator.

## Short numbers

`ShortNumberInfo` answers possible / valid / expected cost / emergency / carrier-specific / SMS-service questions from `ShortNumberMetadata.xml`; examples come straight from the data. `Default` uses the compiled data; `LoadXml(PhoneMetadata, Stream)` reads a newer file with the usual bounds.

- A short number of a region is possible when its length is one of the lengths listed by any of the region's short-number descriptions, valid when it is possible and matches the short-code description. Questions "for a region" answer false when the number's calling code is not the region's.
- Cost: premium rate, then standard rate, then toll free, otherwise toll free for an emergency number and unknown for the rest. Over all regions of a calling code the answer is the most expensive one (premium, unknown, standard, toll free).
- `IsEmergencyNumber` matches the whole text; `ConnectsToEmergencyNumber` also accepts extra digits after an emergency number, except in Brazil, Chile and Nicaragua. A leading plus sign is never an emergency number; surrounding punctuation is ignored.
- `IsCarrierSpecific(number)` asks the first region of the calling code whose short codes contain the number (settled by experiment: a region whose data lists the number as carrier-specific is not asked when an earlier region also has it as a short code).
- The reference does not expose example short numbers or the list of regions with short-number data, so those two are tested against the data file, not the reference.

## Parity method

`tools/Oracles/Phones` generates cases per region from the metadata's example numbers and patterns plus mutations (prefixes, punctuation, extensions, wrong lengths, other regions' numbers, `tel:` URIs, letters, Unicode digits, garbage), runs them through a small Java driver of ours that calls the jar's public API, runs the same cases through the C# engine, and diffs per operation and region. The jar and a user-local JDK are black boxes on the build machine; see `tools/Oracles/Phones/README.md`.

The as-you-type, matcher and short-number cases come from the same generator (`generate-extra`, answered by `ours-extra`, with `ExtraDriver.java` on the jar side; `run-extra.sh` runs all steps). As-you-type: for every region the example number of each type and numbers sampled from each type's pattern, typed nationally, with the national prefix, with `+` and the calling code, with the region's international prefix, with punctuation typed in, as `+<code><prefix>...`, plus random key presses. Each is checked after every key press, with a remembered position at the middle key, and typed again after `Clear()`. Matcher: numbers of every region in several formats and groupings embedded in prose templates, with dates, ids, digit runs, brackets and slashes, plus longer stitched texts, at every leniency and with three `maxTries` bounds. Short numbers: examples and pattern samples of every description of every region, mutated (one digit more or less, prefixed), asked in the region, in a sibling region of the calling code and in an unrelated region; emergency texts with trailing digits, spaces, plus signs and other scripts' digits.
