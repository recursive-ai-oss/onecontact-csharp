# Fuzzing

`OneContact.Fuzz` is a small host with one target per parser or loader the library ships. Each target takes the bytes of one input and passes when it returns, or throws only what the API documents for hostile input: `ContactParseException` from `Parse`, `InvalidDataException` from loaders, `JsonException` from the JSON converters, `HtmlLimitExceededException` from the page parser. Any other exception, a hang, or a broken invariant (a phone match outside the text, an offset outside the input) is a finding.

| Target | What it feeds |
|---|---|
| `phone` | `PhoneNumber` parse with and without a region, every format, key, type, short-number checks |
| `phone-match` | the matcher over text, every leniency, several regions |
| `phone-typing` | `AsYouTypeFormatter` key presses |
| `phone-metadata`, `short-number-metadata` | `PhoneMetadata.LoadXml`, `ShortNumberInfo.LoadXml` on user-supplied files |
| `email`, `web`, `idna` | `EmailAddress`, `WebAddress`, UTS #46 processing under many option sets |
| `address`, `address-hint`, `address-validate` | free-text addresses without and with each of twelve country hints; layout, key and structural `Validate` of whatever parses |
| `person`, `company`, `normalize` | the name parsers and `Contacts.Normalize` |
| `extract-html`, `extract-text` | `ContactExtractor` over raw bytes (through OneHtml) and over text |
| `model-tier1`, `model-superset` | the low-level model loaders on arbitrary bytes |
| `json` | every value type, and dictionary keys, read from arbitrary JSON bytes |
| `tables` | the compiled-table directory reader |
| `canary` | a deliberate crash, to prove the harness reports one |

## Engine

Coverage-guided fuzzing uses [AFL++](https://github.com/AFLplusplus/AFLplusplus) (run as a program, pinned at v4.35c) with [SharpFuzz](https://github.com/Metalnem/sharpfuzz) 2.3.0 instrumenting the .NET assemblies. AFL++ builds with a C compiler and `make`; nothing needs root and nothing is installed system-wide. `fuzz/run.sh` instruments our assemblies and the OneHtml, OneNlp and OneModel assemblies they read through, so a model or page that reaches those libraries is steered too.

```
fuzz/setup.sh                    # once: AFL++ and the sharpfuzz tool, under $ONECONTACT_FUZZ (default ~/onecontact/fuzz)
fuzz/run.sh <target> <seconds>   # build, instrument, seed, fuzz; prints and saves the result
fuzz/run.sh list                 # the targets
```

The release run is `fuzz/run.sh <target> 600` for every target except `canary`, which must report crashes. The result (executions, executions per second, corpus size, edges found, map coverage, crashes, hangs) is saved in `$ONECONTACT_FUZZ/work/results/<target>.txt`. The exit status is 1 when AFL++ saved a crash or a hang.

The library needs its upstream data files fetched first (`tools/fetch/all.sh`, see `docs/tools-environment.md`), as for any build.

## Seed corpora

Seeds come from files in this repository only:

- `testdata/fuzz/<target>/` holds hand-written seeds and hostile cases (bad UTF-8, deep nesting, entity declarations, huge counts).
- `run.sh` adds, per target, one seed per line or file of our own fixtures: the address and name gold files and authority examples, the email conformance corpus, the extraction test pages and text.
- The model targets also use prefixes of a weights file when `ONECONTACT_TIER1_WEIGHTS` or `ONECONTACT_MODELS_WEIGHTS` names a folder that holds one (weights are not in the repository), including copies whose header declares a short weights section so that mutation reaches the config reader.

No personal data and no crawled pages are used.

## When a target finds something

AFL++ saves the inputs under `$ONECONTACT_FUZZ/work/out/<target>/default/crashes` (and `hangs`). Replay one with the host:

```
dotnet run --project fuzz/OneContact.Fuzz -c Release -- <target> path/to/input
```

Fix the cause in the library, minimize the input to under 1 MB, put it in `testdata/fuzz-repros/<target>/` and add a test that feeds it to the public API.

## Other smoke tools

- `fuzz <target> <seed-directory> --seconds N` is a seeded mutation loop without coverage feedback, for a quick check where AFL++ is not installed.
- `address-smoke <testdata/addresses> <seconds>` and `names-smoke <testdata/names> <seconds>` are bounded mutation runs over the address and name parsers with invariant checks.
