# Speed rows: method and indicative results

How the 1.0 speed rows are measured, and the last indicative numbers. The publishable rows replace the indicative table when the gate runs.

## Method

- **Harness:** `bench/OneContact.Bench`, BenchmarkDotNet 0.15.8, in-process no-emit toolchain, `[MemoryDiagnoser]` on every row. Indicative runs use the short job (3 iterations); publishable rows use `ONECONTACT_BENCH_JOB=default`.
- **Same inputs, same work:** every comparison row loops one fixed input list (`Inputs.cs`, written or seeded in the repo) through both libraries; `OperationsPerInvoke` makes the figures per item. Comparators are called through their documented public API only: libphonenumber-csharp 9.0.42, MimeKit 4.18.2, `System.Net.Mail.MailAddress` (.NET 10), AngleSharp 1.9.1, Nager.PublicSuffix 3.8.0 (same list file), NameParserSharp 1.5.0, AddressParser 1.0.3, Microsoft.Recognizers.Text.Sequence 1.8.13.
- **Steady state:** the mean after BenchmarkDotNet's warm-up. Extraction rows are steady state for the same reason.
- **Accuracy beside speed:** `dotnet … accuracy` (phones, email, URL, names, extraction) and `tools/Eval.AddressGold` print the agreement or accuracy of both sides on the inputs the rows time. A row where the comparator is less accurate counts under the matched-or-better-accuracy clause.
- **Differentials for tuning:** `dump-geo <file>` (mapper answers for seeded numbers of every region) and the extraction and address dumps are run on the base and the changed build; the files must be byte-identical.
- **Cold start:** `first-use <kind>` in a fresh process prints the first call's time and the peak and growth of the working set; `none` is the runtime baseline.
- **Record per run:** CPU, cores, 1/5/15-minute load before, during (every 20 s) and after, SDK, runtime, comparator versions.
- Native libphonenumber and libpostal numbers are information, not gates.

## Indicative results (x64, shared machine under load about 25 on 32 logical cores, so pessimistic)

Ryzen 9 7950X3D, 16 cores / 32 threads, .NET SDK 10.0.403, short job. Ratio = ours / comparator (below 1 is faster). Allocation is per operation.

| Row | Ours | Comparator | Ratio | Allocated (ours / theirs) |
|---|---|---|---|---|
| Phone parse | 464 ns | libphonenumber-csharp 653 ns | 0.71 | 293 B / 459 B |
| Parse and validate | 469 ns | 1,684 ns | 0.28 | 293 B / 503 B |
| Format (international and national) | 710 ns | 3,155 ns | 0.23 | 851 B / 1,696 B |
| Number type | 10 ns | 942 ns | 0.01 | 0 / 67 B |
| As-you-type, per number typed | 2.52 µs | 4.93 µs | 0.51 | 3.05 KB / 3.93 KB |
| findNumbers, 1 MB, Possible | 40.8 ms | 41.5 ms | 0.98 | 7.7 MB / 31.5 MB |
| findNumbers, Valid | 40.5 ms | 67.4 ms | 0.60 | 7.2 MB / 33.3 MB |
| findNumbers, StrictGrouping | 46.0 ms | 73.2 ms | 0.63 | 11.6 MB / 35.1 MB |
| findNumbers, ExactGrouping | 47.9 ms | 72.7 ms | 0.66 | 12.0 MB / 36.1 MB |
| Short number: valid and cost | 135 ns | 412 ns | 0.33 | 48 B / 64 B |
| Short number: emergency | 78 ns | 118 ns | 0.66 | 64 B / 32 B |
| Geocoder | 1,097 ns | 1,370 ns | 0.80 | 342 B / 256 B |
| Carrier | 575 ns | 1,080 ns | 0.53 | 131 B / 133 B |
| Time zones | 602 ns | 1,060 ns | 0.57 | 129 B / 216 B |
| Email, mixed (33) vs MimeKit | 217 ns | 254 ns | 0.85 | 205 B / 403 B |
| Email, ASCII (30) vs MimeKit | 200 ns | 236 ns | 0.85 | 209 B / 383 B |
| Email, IDN (3) vs MimeKit | 311 ns | 388 ns | 0.80 | 165 B / 608 B |
| Email vs `MailAddress` (mixed / ASCII / IDN) | 217 / 200 / 311 ns | 110 / 111 / 111 ns | 1.96 / 1.80 / 2.81 | `MailAddress` is less accurate (below) |
| URL vs AngleSharp | 552 ns | 541 ns | 1.02 | 657 B / 604 B |
| Public suffix vs Nager | 406 ns | 1,604 ns | 0.25 | 537 B / 2,534 B |
| Person name vs NameParserSharp | 3.69 µs | 5.22 µs | 0.71 | 6.5 KB / 8.0 KB |
| US address rules vs AddressParser | 24.5 µs | 25.5 µs | 0.96 | 20 KB / 5.7 KB |
| Extraction, text in vs Recognizers | 1.27 ms | 2.53 ms | 0.50 | 1.2 MB / 0.47 MB |
| Extraction, HTML in vs Recognizers (on the visible text) | 2.01 ms | 2.53 ms | 0.80 | 1.6 MB / 0.47 MB |

Matched-accuracy clause, same inputs:

- Email: agreement with the RFC verdict on 33 inputs, ours 32, `MailAddress` 30, MimeKit 31.
- URL: WHATWG test data (896 entries), ours 896, AngleSharp 503.
- Names: same given and family as NameParserSharp on 29 of 32; accuracy against gold is on the evaluation sets.
- US addresses (synthetic gold set of 212 US addresses, written from the same word lists as our own tests, so ours is optimistic): field accuracy ours 100.0 %, AddressParser 92.6 %; full-parse 100.0 % vs 73.6 %.
- Extraction: recall on planted phones, emails and websites 192/192 for both.

## Tuning

- **Phone mappers (geocoder, carrier, time zones).** A longest-prefix lookup tried every key length in turn, each a binary search plus a block decode. It now finds the block holding the largest key not after the digits and takes the last prefix in it, falling back to the length loop when that block has none; cached maps and zone lists are read without allocating a closure. Time zones 2.78 µs to 0.60 µs, geocoder 1.47 µs to 1.10 µs, carrier 0.63 µs to 0.58 µs; answers byte-identical on 36,678 seeded lookups across every region in seven languages.

## Cold start (x64, fresh process each, median of 3)

| Kind | First call | Peak working set | Growth over the runtime baseline |
|---|---|---|---|
| runtime baseline | | 32 MB | 3 MB |
| Phone parse and format | 37 ms | 38 MB | 8 MB |
| Phone geocoder and time zones | 46 ms | 47 MB | 17 MB |
| Email | 16 ms | 37 MB | 7 MB |
| URL and registrable domain | 34 ms | 40 MB | 11 MB |
| Address | 84 ms | 41 MB | 12 MB |
| Person name | 75 ms | 48 MB | 19 MB |
| Company name | 65 ms | 47 MB | 18 MB |
| Extraction, text | 261 ms | 63 MB | 34 MB |
| Extraction, HTML | 341 ms | 67 MB | 38 MB |

## Package sizes (0.6.0-preview.1 build, no model package)

| Package | Size |
|---|---|
| `RecursiveAI.OneContact` | 1.39 MB |
| `RecursiveAI.OneContact.Phones.Geo` | 1.52 MB |
| `RecursiveAI.OneContact.Extraction` | 0.21 MB |

libpostal's model and data files are about 2 GB on disk.
