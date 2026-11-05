# Fuzz host

`OneContact.Fuzz` runs one target over an input file, a directory of files, or standard input:

```
dotnet run --project fuzz/OneContact.Fuzz -- email path/to/input
```

Targets: `phone`, `email`, `web`, `address`, `person`, `company`, `normalize`. A target must not throw anything but `ContactParseException`. Seed inputs and minimized reproductions (under 1 MB each) go in `testdata/fuzz/<target>/`.

`names-smoke <testdata/names> <seconds>` is a bounded mutation run over the name and company parsers (seeded, no fuzzer needed): it checks that nothing but `ContactParseException` is thrown, that offsets lie inside the input and that canonical forms parse again.
