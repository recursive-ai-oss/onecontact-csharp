# Architecture notes

Normative notes on how the code and its data fit together. Behaviour of each contact kind (key rules, rendering rules, label schemes) gets its own document here when the code for it lands.

## Layout

| Path | What |
|---|---|
| `src/OneContact` | `RecursiveAI.OneContact`: the value types, `Contacts`, parse results, keys, JSON converters and every kind's parsing. Base class library only. |
| `src/OneContact.Phones.Geo` | Geocoder, carrier and time-zone mappers. References core. |
| `src/OneContact.Models.*`, `src/OneContact.Extraction` | Placeholders until their stage; not packable yet. |
| `tests/OneContact.Tests` | xUnit v3 tests. |
| `tools/Ingest`, `tools/Ingest.<Source>` | The ingest harness and one converter per upstream source. |
| `tools/fetch` | Fetch scripts: exact files at a pinned ref into a cache outside the repository. |
| `tools/Oracles` | Drivers for reference implementations used as black boxes by parity tests. |
| `upstream/<source>` | Each source's `PIN` and the licence and notice files as upstream publishes them. |
| `samples/OneContact.AotSmoke` | NativeAOT console app; CI publishes it with warnings as errors and runs it. |
| `bench`, `fuzz` | BenchmarkDotNet rows; the fuzz host. |

Build output (`bin`, `obj`, packages) never goes in the source tree. See the comment at the top of `Directory.Build.props`: `ONECONTACT_ARTIFACTS`, a shared build area found next to the checkout, or `artifacts/`.

## Value types

`PhoneNumber`, `EmailAddress`, `WebAddress`, `PostalAddress`, `PersonName` and `CompanyName` are `readonly partial struct`s with the same shape:

- `Canonical` (faithful to the standard or the authority's style), `Key` / `GetKey(KeyVersion)` (aggressive, versioned), `Flags` (a per-kind `[Flags]` enum named `<Kind>Facts`), `Source` (a `SourceSpan` of UTF-16 offsets into the original input).
- `Parse` and `TryParse` over `string` and `ReadOnlySpan<char>`, `ISpanParsable<T>` (the `IFormatProvider` is ignored: nothing depends on culture), `ISpanFormattable` (format `C` or none: the canonical form), `IEquatable<T>`, `==` on canonical forms.
- A low-level `OperationStatus TryParse(span, [Region,] out T value, out ParseDetail detail)` that never throws.
- `default(T)` is the empty value: empty canonical form and key, empty source span.
- A `[JsonConverter]` attribute pointing at a reflection-free converter that writes the canonical form as a string, and an entry in the source-generated `ContactJsonContext`.

`Parse` throws `ContactParseException`, a `FormatException` carrying `Kind`, `Reason` (a `ReasonCode`) and `Span`.

### Reason codes

A `ReasonCode` is a kind plus a number that is unique within the kind. Numbers are never reused or renumbered. Codes shared by every kind live in `Parsing/ReasonCode.cs` (kind `Unknown`); each kind adds its own as static members of the partial struct in its own file (`Phones/ReasonCode.Phone.cs`, named `ReasonCode.Phone...`). `Id` is `<kind>.<name>`, such as `phone.region-required`.

### Keys

Each kind has a static `<Kind>Key.Compute(value, KeyVersion)` in the `Keys` namespace. A key-rule change ships as a new `KeyVersion`; a published version's output never changes, and its rules are written down here (one file per kind) with golden fixtures.

### Seams: who edits which files

Each kind owns one folder under `src/OneContact/`: the struct, its parser (`<Kind>Parser.cs`, an `internal static` class with one `TryParse` method), its key class, its facts enum, and its reason codes. The parser files currently hold a placeholder body (trim, keep the text), so the shared contracts have something to run on. Implementing a kind means replacing that parser body and growing the struct, key class and enum; no shared file needs to change. `Contacts.Normalize` calls the structs' `TryParse`, and `ContactKindDetector` (shape tests only) is the one shared place that decides a kind from text.

## Compiled data

Upstream data becomes compact tables at build time and reaches the library as an embedded resource, read lazily.

**Why an embedded resource and not generated C#:** a table of a megabyte or more as a C# array literal makes the compiler slow and the source diff unreadable; a resource is read with `Assembly.GetManifestResourceStream`, which the trimmer and NativeAOT treat as plain data (no reflection over types), and nothing is read until a table is asked for.

**Format "OCDT" version 1** (little-endian):

```
offset 0   "OCDT"
offset 4   u16 format version (1)
offset 6   u16 reserved (0)
offset 8   u32 table count N (at most 4096)
offset 12  N directory entries: u8 name length (1..255), name (UTF-8), u32 offset from file start, u32 length
...        table bytes
```

Tables are written in ordinal name order, so the same inputs give the same bytes. The reader validates the whole directory before handing out anything: bad magic or version, a count over the limit, truncation, duplicate names, and any table outside the file all raise `InvalidDataException`. What is inside a table is up to its source; each source documents its own table layouts here when it lands.

**Wiring a source into a project.** A source needs `upstream/<source>/PIN` plus licence files, `tools/fetch/<source>.sh`, a converter project `tools/Ingest.<Source>` (an `Exe` whose `Main` calls `IngestRunner.Main(args, new MySource())`), and one props file in the consuming project, for core `src/OneContact/Data/<source>.ingest.props`:

```xml
<Project>
  <ItemGroup>
    <IngestSource Include="phonenumber" Tool="../../tools/Ingest.Phone/OneContact.Ingest.Phone.csproj" Pin="../../upstream/phonenumber/PIN" />
  </ItemGroup>
</Project>
```

The core project imports `Data/*.ingest.props`, so adding a source never edits a shared file. The tool is also added as a project reference with no output reference, so restoring the consuming project restores the tool; a fresh checkout needs no extra step. Before compiling, `build/Ingest.targets` builds the tool, runs it over the fetched files (`$ONECONTACT_UPSTREAM/<source>`, or the `Files` metadata), and embeds `<source>.ocd` as the resource `OneContact.Data.<source>.ocd`. The notice text and the provenance record are packed under `notices/` in the package. In code:

```csharp
private static readonly EmbeddedTables Tables = new(typeof(Marker).Assembly, "OneContact.Data.phonenumber.ocd");
ReadOnlyMemory<byte> regions = Tables.GetTable("regions");   // first call reads and validates the file
```

`tests/OneContact.Tests` uses a made-up `synthetic` source the same way, which is what proves the path.

## Ingest procedure

- A **pin** (`PIN`) is a text file: `source`, `licence` (SPDX), `repo`, `ref` (tag or full commit), `urlbase`, and one `file <path> <sha256>` line per file. Paths are plain relative paths.
- `tools/fetch/fetch-pin.sh <PIN>` fetches each file by exact path (never a clone or archive) into the cache and checks every hash. A mismatch fails and the file is not kept.
- `tools/fetch/all.sh` runs `fetch-pin.sh` for every `upstream/*/PIN`; CI runs it before building, with `ONECONTACT_UPSTREAM` pointing at a temporary folder. A new source needs no registration there.
- The harness (`IngestRunner`) checks every hash again before it converts anything, then writes `<source>.ocd`, `<source>.provenance.json` (source, licence, repo, ref, tool version, the path, hash and size of each input, and the hash and size of the output) and `<source>.notice.txt`. Exit codes: 0 ok, 2 file missing or hash differs, 3 bad pin, 64 bad arguments.
- Conversion must be deterministic: no clocks, no random numbers, no dependence on dictionary order. Rerunning on the same inputs gives byte-identical outputs; files whose content is unchanged are not rewritten.
- Notices that must ship (Apache-2.0 for the libphonenumber and libaddressinput data, MIT for the libpostal dictionaries, MPL-2.0 for the Public Suffix List, CC0 credit lines for the legal-form list and the disposable-domain list) are listed in `THIRD-PARTY-NOTICES.md`, which every package carries.
