# Phone parity harness

Compares OneContact's phone engine with a released libphonenumber jar, case by case, operation by operation.

1. `generate <cases.tsv> [--seed N]` writes the cases: for every region of the metadata, texts built from its example numbers (formatted, prefixed, mutated, extended, truncated, in other scripts), other regions' numbers, `tel:` URIs, vanity numbers and garbage, plus region-level and non-geographic questions.
2. `Driver.java` answers the same cases with the jar's public API (it is only executed; nothing here reads the jar):
   `java -cp libphonenumber-<version>.jar Driver.java cases.tsv jar.tsv`
3. `ours <cases.tsv> <ours.tsv>` answers them with OneContact.
4. `diff <ours.tsv> <jar.tsv> <cases.tsv> [--show N]` reports mismatches per operation and per region.

`run.sh` does all four steps. Result lines are `id<TAB>operation<TAB>value`; text is escaped as `\\` and `\uXXXX` for everything outside printable ASCII.

The jar is a pinned release downloaded from Maven Central and run on a user-local JDK; see the oracle notes in the repository documentation.

## As-you-type, matcher and short numbers

`run-extra.sh <work dir> [seed]` does the same four steps for the as-you-type formatter, the matcher and short numbers: `generate-extra` writes the cases (kinds `A`, `M`, `S`, `E`), `ExtraDriver.java` answers them with the jar (`PhoneNumberUtil.getAsYouTypeFormatter`, `findNumbers` at every `Leniency` and three `maxTries` bounds, `ShortNumberInfo`), `ours-extra` answers them with OneContact, and `diff` compares. The jar does not expose example short numbers or the list of regions with short-number data, so those are not compared here.
