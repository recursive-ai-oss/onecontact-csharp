# Speed rows: method and results

How the 1.0 speed rows are measured, and the numbers of the publishable run. Rows are ours against the managed comparator, through its public API only.

## Method

- **Harness:** `bench/OneContact.Bench`, BenchmarkDotNet 0.15.8, in-process no-emit toolchain, the default job (`ONECONTACT_BENCH_JOB=default`: BenchmarkDotNet's own warm-up and iteration counts), `[MemoryDiagnoser]` on every row. Each group ran once; a group that was rerun after a code change is listed with both runs. Timings are the mean after warm-up (steady state), which is also how the extraction rows are taken.
- **Same inputs, same work:** every comparison row loops one fixed input list (`Inputs.cs`, written or seeded in the repository) through both libraries; `OperationsPerInvoke` makes the figures per item. Comparators: libphonenumber-csharp 9.0.42, MimeKit 4.18.2, `System.Net.Mail.MailAddress` (.NET 10), AngleSharp 1.9.1, Nager.PublicSuffix 3.8.0 (same list file), NameParserSharp 1.5.0, AddressParser 1.0.3 (published as a debug build, run as shipped), Microsoft.Recognizers.Text.Sequence 1.8.13 (on the page's visible text). OneHtml 1.6.0.
- **Queue:** every publishable group ran as a `wq --class bench` job, alone on the machine. Load is the 1/5/15-minute load average read before the group, every 20 s during it and after it (`tools/bench/run-rows.sh`). The first "during" sample can still show the decaying average of the previous job.
- **Accuracy beside speed** (the matched-accuracy clause): `dotnet … accuracy` and `tools/Eval.AddressGold` print agreement or accuracy of both sides on the inputs the rows time.
- **Peak memory:** `tools/bench/peak-rss.sh` runs each benchmark method alone in its own process and reads the process's peak resident set from the operating system. The figure includes the runtime and the harness (an idle runtime peaks at 32 MB on x64 and 43 MB on arm64), so only differences between the two sides of a row mean anything.
- **Differentials for tuning:** `dump-geo`, `dump-matches` and `dump-diff` (extraction, matcher, email and web answers) run on the base and the changed build; the files must be byte-identical.
- **Cold start:** `first-use <kind>` in a fresh process prints the first call's time and the working-set growth.

Machines:

| | x64 | arm64 |
|---|---|---|
| CPU | AMD Ryzen 9 7950X3D, 16 cores / 32 threads | Apple M2, 8 cores |
| OS, SDK | Linux Mint 22.3, .NET SDK 10.0.403 | macOS, .NET SDK 10.0.403 (user-local) |
| Load before, during, after | see the group table | see the group table |

## Results

Ratio = ours / comparator (below 1 is faster). Times are per operation (per item for the loops, per 1 MB text for findNumbers). Allocation is per operation; it is the same on both machines to within a few bytes, so it is given once.

| Row | x64 ours | x64 theirs | x64 ratio | arm64 ours | arm64 theirs | arm64 ratio | Allocated ours / theirs | Peak RSS x64 ours / theirs (MB) | Peak RSS arm64 ours / theirs (MB) |
|---|---|---|---|---|---|---|---|---|---|
| Phone parse | 275 ns | 390 ns | 0.71 | 324 ns | 435 ns | 0.75 | 293 B / 459 B | 129 / 128 | 94 / 92 |
| Parse and validate | 276 ns | 1,016 ns | 0.27 | 325 ns | 1,061 ns | 0.31 | 293 B / 503 B | 125 / 135 | 94 / 93 |
| Format (international, national) | 419 ns | 1,852 ns | 0.23 | 456 ns | 1,962 ns | 0.23 | 851 B / 1.7 KB | 128 / 143 | 90 / 92 |
| Number type | 0.7 ns | 560 ns | 0.00 | 1.0 ns | 633 ns | 0.00 | 0 / 67 B | 69 / 124 | 82 / 92 |
| As-you-type, per number typed | 1.47 µs | 2.87 µs | 0.51 | 1.49 µs | 3.04 µs | 0.49 | 3.05 KB / 3.93 KB | 121 / 128 | 88 / 90 |
| findNumbers 1 MB, Valid (matcher row) | 20.7 ms | 39.3 ms | 0.53 | 22.8 ms | 38.0 ms | 0.60 | 7.2 MB / 33.3 MB | 126 / 125 | 95 / 93 |
| findNumbers, Possible | 20.3 ms | 23.4 ms | 0.87 | 22.6 ms | 23.2 ms | 0.97 | 7.7 MB / 31.5 MB | 138 / 133 | 99 / 99 |
| findNumbers, Valid | 20.5 ms | 38.7 ms | 0.53 | 22.8 ms | 37.8 ms | 0.60 | 7.2 MB / 33.3 MB | (same process) | (same process) |
| findNumbers, StrictGrouping | 23.0 ms | 42.4 ms | 0.54 | 24.8 ms | 41.2 ms | 0.60 | 11.6 MB / 35.1 MB | (same process) | (same process) |
| findNumbers, ExactGrouping | 22.6 ms | 42.8 ms | 0.53 | 24.6 ms | 41.5 ms | 0.59 | 12.0 MB / 36.1 MB | (same process) | (same process) |
| Short number: valid and cost | 86 ns | 252 ns | 0.34 | 66 ns | 258 ns | 0.26 | 48 B / 64 B | 123 / 122 | 90 / 92 |
| Short number: emergency | 46 ns | 69 ns | 0.67 | 48 ns | 73 ns | 0.66 | 64 B / 32 B | 122 / 122 | 88 / 91 |
| Geocoder | 675 ns | 845 ns | 0.80 | 748 ns | 906 ns | 0.83 | 342 B / 256 B | 139 / 163 | 108 / 108 |
| Carrier | 355 ns | 677 ns | 0.53 | 390 ns | 741 ns | 0.53 | 131 B / 133 B | 137 / 139 | 107 / 107 |
| Time zones | 378 ns | 645 ns | 0.59 | 421 ns | 713 ns | 0.59 | 129 B / 216 B | 137 / 139 | 107 / 107 |
| Email, mixed (33), vs MimeKit | 127 ns | 151 ns | 0.84 | 131 ns | 161 ns | 0.81 | 205 B / 403 B | 121 / 126 | 87 / 86 |
| Email, ASCII (30), vs MimeKit | 117 ns | 136 ns | 0.86 | 121 ns | 152 ns | 0.80 | 209 B / 383 B | 120 / 123 | 86 / 85 |
| Email, IDN (3), vs MimeKit | 186 ns | 227 ns | 0.82 | 210 ns | 223 ns | 0.94 | 165 B / 608 B | 120 / 123 | 86 / 85 |
| Email, mixed, vs `MailAddress` | 127 ns | 64 ns | 1.98 | 131 ns | 87 ns | 1.50 | 205 B / 109 B | 121 / 118 | 87 / 85 |
| Email, ASCII, vs `MailAddress` | 117 ns | 64 ns | 1.85 | 121 ns | 85 ns | 1.43 | 209 B / 107 B | 120 / 118 | 86 / 85 |
| Email, IDN, vs `MailAddress` | 186 ns | 66 ns | 2.82 | 210 ns | 86 ns | 2.45 | 165 B / 128 B | 120 / 118 | 86 / 85 |
| URL vs AngleSharp | 341 ns | 338 ns | 1.01 | 337 ns | 359 ns | 0.94 | 657 B / 604 B | 125 / 125 | 91 / 86 |
| Public suffix vs Nager | 237 ns | 989 ns | 0.24 | 259 ns | 895 ns | 0.29 | 537 B / 2.5 KB | 123 / 122 | 90 / 89 |
| Person name vs NameParserSharp | 2.28 µs | 3.24 µs | 0.70 | 2.10 µs | 2.87 µs | 0.73 | 6.5 KB / 8.0 KB | 127 / 143 | 96 / 88 |
| US address rules, country given, vs AddressParser | 1.59 µs | 15.5 µs | 0.10 | 1.44 µs | 14.9 µs | 0.10 | 5.2 KB / 5.7 KB | 126 / 201 | 95 / 187 |
| US address rules, no country given (also infers it) | 14.9 µs | 15.5 µs | 0.96 | 15.5 µs | 14.9 µs | 1.05 | 20 KB / 5.7 KB | (as above) | (as above) |
| Extraction, text in, vs Recognizers | 750 µs | 1,471 µs | 0.51 | 694 µs | 2,062 µs | 0.34 | 1.18 MB / 0.47 MB | 143 / 130 | 112 / 95 |
| Extraction, HTML in, vs Recognizers | 1,139 µs | 1,471 µs | 0.77 | 1,036 µs | 2,062 µs | 0.50 | 1.57 MB / 0.47 MB | 151 / 130 | 122 / 95 |

Notes on the rows:

- **Email against `MailAddress`** is slower on every row. It falls under the matched-accuracy clause: agreement with the RFC verdict on 33 inputs is ours 32, `MailAddress` 30, MimeKit 31.
- **URL against AngleSharp** is 1.01× on x64 and 0.94× on arm64. The clause covers it: AngleSharp passes 503 of the 896 WHATWG test entries, ours 896.
- **US address:** the comparator reads American addresses only, so the matched row tells our parser the country (`Region.US`). Without a country our parser also works out which country the text is from, which costs about ten times as much; that row is shown for information (x64 0.96, arm64 1.05). Accuracy on the 212 US addresses of the synthetic gold set, which our own rules were written against (ours is optimistic): field 100.0 % vs 92.6 %, full parse 100.0 % vs 73.6 %. The peak-memory figure for the country-given row was measured before it existed (it is the no-country process).
- **Extraction:** recall on planted phones, emails and websites is 192/192 for both; HTML in includes the page parse, the Recognizers side reads the page's visible text.
- **Matcher:** our finder returns the same 3,478 matches as the libphonenumber jar at Valid; libphonenumber-csharp returns 3,514 (the accuracy script prints it).
- **Names:** same given and family name as NameParserSharp on 29 of 32 inputs; accuracy against gold is in the evaluation sets.
- **Peak memory:** the two sides differ by a few MB or less except where noted (our address parser without a country, the geocoder tables of libphonenumber-csharp 163 MB vs ours 139 MB, extraction pages). The loop of `Number type` is too short for the harness to grow.

## Groups: jobs and load

Each row of the table above was taken in the group shown. "Rerun" groups replaced the first run of the same rows after the matcher change below.

| Group | Rows | x64 `wq` job | x64 load before / during (min–max) / after | arm64 `wq` job | arm64 load before / during / after |
|---|---|---|---|---|---|
| 1 | phone, as-you-type, short numbers, geo | 006258 | 15.7 16.6 20.3 / 1.0–11.6 / 1.0 4.9 13.3 | 001987 | 2.28 2.21 2.03 / 2.0–2.5 / 2.32 2.21 2.09 |
| 2 | findNumbers (first run, replaced) | 006270 | 2.2 6.8 11.3 / 1.1–1.8 / 1.1 4.1 9.4 | 001988 | 2.37 2.22 2.09 / (log) / 2.01 2.20 2.11 |
| 2 rerun | findNumbers, after the matcher change | 006288 | 3.4 4.4 7.0 / 1.4–2.8 / 1.3 3.0 6.0 | 001991 | 1.86 1.71 1.68 / 1.8–2.1 / 2.09 1.91 1.77 |
| 3 | email, URL, PSL, names, address | 006274 | 1.0 4.0 9.3 / 1.0–1.2 / 1.3 2.1 6.8 | 001989 | 2.01 2.20 2.11 / 2.0 / 2.01 2.02 2.05 |
| 4 | extraction (first run, replaced) | 006276 | 1.2 2.0 6.8 / 1.2–1.3 / 1.3 1.9 6.4 | 001990 | 2.01 2.02 2.05 / 2.0–2.1 / 2.11 2.04 2.05 |
| 4 rerun | extraction, after the matcher change, OneHtml 1.6.0 on both machines | 006293 | 1.2 2.9 6.0 / 1.1–1.2 / 1.1 2.6 5.7 | 001992 | 2.09 1.91 1.77 / 2.1 / 2.07 1.95 1.79 |
| address | US address row with the country given | 006306 | 10.8 13.0 14.0 / (see the log) / 5.2 11.1 13.3 | 001994 | 1.81 1.61 1.69 / 1.8 / 1.75 1.63 1.68 |
| peak memory | one process per method | 006294 | (log) | 001993 | (log) |

The first group-1 run on x64 and the address run began while the machine's 15-minute average was still falling after earlier jobs; the 1-minute average during the groups was 1–3 on 32 logical cores (group 1: up to 11.6 in its first sample). The first findNumbers and extraction runs on arm64 (job 001988, 001990) were on OneHtml 1.6.0-preview.2 and the original matcher; they are superseded by the reruns. The arm64 findNumbers numbers of that first run showed Possible at 1.14× before the matcher change.

## Tuning

- **Phone mappers (geocoder, carrier, time zones).** A longest-prefix lookup tried every key length in turn, each a binary search plus a block decode. It now finds the block holding the largest key not after the digits and takes the last prefix in it, falling back to the length loop when that block has none; cached maps and zone lists are read without allocating a closure. Before and after in one job, x64, two repetitions each: time zones 2.74–2.82 µs to 0.60 µs, geocoder 1.47 µs to 1.09–1.10 µs, carrier 0.62–0.63 µs to 0.57–0.58 µs; answers byte-identical on 36,678 seeded lookups across every region in seven languages (`dump-geo`).
- **Number finder (findNumbers).** The candidate pattern was run by the phone pattern engine for every digit run; a scan written by hand now finds the same candidates (`PhoneCandidateScanner`; the pattern stays as the reference and a test compares the two on 400,000 random texts and on long digit runs). Matches at every leniency, three regions and two texts are byte-identical (100,805 lines, `dump-matches`), and the extraction, email and web answers are byte-identical (75,756 lines, `dump-diff`). At the first publishable run the row at the Possible leniency was 1.02× on x64 and 1.14× on arm64; it is 0.87× and 0.97× after. Every other findNumbers row also gained about 15 %.

## Cold start (fresh process each, median of 3)

| Kind | x64 first call | x64 peak working set (growth) | arm64 first call | arm64 working-set growth |
|---|---|---|---|---|
| Runtime baseline | | 32 MB (3 MB) | | 43 MB peak at idle |
| Phone parse and format | 38 ms | 37 MB (8 MB) | 20 ms | 5 MB |
| Phone geocoder and time zones | 51 ms | 47 MB (17 MB) | 26 ms | 14 MB |
| Email | 13 ms | 36 MB (7 MB) | 9 ms | 4 MB |
| URL and registrable domain | 35 ms | 40 MB (10 MB) | 19 ms | 7 MB |
| Address | 75 ms | 41 MB (12 MB) | 50 ms | 9 MB |
| Person name | 75 ms | 48 MB (19 MB) | 44 ms | 15 MB |
| Company name | 61 ms | 47 MB (17 MB) | 34 ms | 14 MB |
| Extraction, text | 235 ms | 63 MB (34 MB) | 153 ms | 31 MB |
| Extraction, HTML | 359 ms | 67 MB (38 MB) | 198 ms | 37 MB |

The x64 first-call times were taken with the machine's load average near 20 (other jobs), so they are pessimistic. The peak working set is not available from the runtime on macOS.

## Package sizes (0.6.0-preview.1 build, no model package)

| Package | Size |
|---|---|
| `RecursiveAI.OneContact` | 1.39 MB |
| `RecursiveAI.OneContact.Phones.Geo` | 1.52 MB |
| `RecursiveAI.OneContact.Extraction` | 0.21 MB |

libpostal's model and data files are about 2 GB on disk.
