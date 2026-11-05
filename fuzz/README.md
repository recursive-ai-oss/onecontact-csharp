# Fuzz host

`OneContact.Fuzz` runs one target over an input file, a directory of files, or standard input:

```
dotnet run --project fuzz/OneContact.Fuzz -- email path/to/input
```

Targets: `phone`, `phone-metadata`, `email`, `web`, `address`, `person`, `company`, `normalize`. A target must not throw anything but `ContactParseException`. Seed inputs and minimized reproductions (under 1 MB each) go in `testdata/fuzz/<target>/`.

`fuzz <target> <seed-directory> --seconds N` runs a seeded mutation loop (byte flips, truncations, splices) over the seed files for N seconds and saves the first failing input. It has no coverage feedback; it is a smoke run.
