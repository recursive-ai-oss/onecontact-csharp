# Name and company training data

Renders labelled training examples for the person-name and company-name models from open name lists and company registers, through our own templates.
The provenance, licences, counts and the split rule are in `../provenance/names-us-ca.md`.

```
tools/data/names/fetch.sh                      # fetch and hash-check the sources of pins/names.pin into $ONECONTACT_DATA/train/names
dotnet run -c Release --project tools/data/names -- render \
    --data  $ONECONTACT_DATA/train/names \
    --elf   <path to 2026-02-19-elf-code-list-v1.6.csv> \
    --out   $ONECONTACT_DATA/train/names-us-ca-v1        # about 2 minutes; seed 20261105
dotnet run -c Release --project tools/data/names -- report --out $ONECONTACT_DATA/train/names-us-ca-v1 --sample 5000
```

`render` writes `train/` and `dev/` (JSON Lines), `text-hashes.u64` and `manifest.json` (inputs with hashes, template hashes, seed, counts per split, class and pattern, and the SHA-256 of
every output file). Output is deterministic: the same pinned inputs and seed give byte-identical files. Options: `--seed`, `--persons`, `--multiple`, `--unknown`, `--synth`
(attempt counts), `--ch-percent` (share of Companies House rows), `--registers skip` (name lists and templates only, for a quick check).

`report` runs the rule parser over a deterministic sample of the dev split and writes `rule-report.md`: how often rules alone give the gold class and parts. It reads only the
dev split and never changes labels.

Layout: `Pools.cs` (name lists), `Registers.cs` (GLEIF, Companies House, EDGAR, the ISO 20275 form list), `Person.cs` and `Company.cs` (the renderers), `Example.cs` (builder and
writer), `Templates.cs` and `templates/*.tsv` (our hand-written lists), `Report.cs`.

The 95–99 hash buckets are never rendered: evaluation sets are to be drawn from them, or checked against `text-hashes.u64`.
