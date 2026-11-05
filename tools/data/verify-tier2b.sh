#!/usr/bin/env bash
# Renders the Tier 2 group B examples twice from one selection file and compares every output byte, then runs the span check and the
# rule-parser sample. usage: verify-tier2b.sh <AddressTier2B dll> <train data dir> <out dir> (the out dir already holds selection.tsv)
set -euo pipefail
dll=${1:?dll} data=${2:?data dir} out=${3:?out dir}
again=$out-again
rm -f "$out"/*.jsonl "$out"/manifest.json
rm -rf "$again"; mkdir -p "$again"; cp "$out/selection.tsv" "$again/"
dotnet "$dll" render --data "$data" --out "$out" --seed 20261105 | tee "$out/render.log"
dotnet "$dll" render --data "$data" --out "$again" --seed 20261105 > /dev/null
(cd "$out" && sha256sum *.jsonl manifest.json) > "$out/sha256sums.txt"
(cd "$again" && sha256sum *.jsonl manifest.json) > "$again/sha256sums.txt"
diff "$out/sha256sums.txt" "$again/sha256sums.txt" && echo "IDENTICAL RE-RENDER ($(wc -l < "$out/sha256sums.txt") files)"
dotnet "$dll" check --out "$out" | tee "$out/check.log" | tail -3
dotnet "$dll" sample --out "$out" --per-country 300
