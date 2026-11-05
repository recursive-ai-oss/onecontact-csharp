# Fuzz host

`OneContact.Fuzz` runs one target over an input file, a directory of files, or standard input:

```
dotnet run --project fuzz/OneContact.Fuzz -- email path/to/input
```

Targets: `phone`, `email`, `web`, `address`, `person`, `company`, `normalize`. A target must not throw anything but `ContactParseException`. Seed inputs and minimized reproductions (under 1 MB each) go in `testdata/fuzz/<target>/`.
