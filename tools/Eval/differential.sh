#!/usr/bin/env bash
# usage: tools/Eval/differential.sh <label>
# Builds this tree and writes, under $HOME/onecontact/differential/<label>/, what the rule parser reads from the first lines of every register dev render (hinted and with no hint)
# and what the extractor finds on the private extraction sets, rules only and with the Tier 1 + Tier 2 model (ONECONTACT_MODELS_MODEL). Run it in two trees and compare the
# outputs with cmp or diff: only counts and hashes of the private outputs should leave the machine.
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
label=${1:?label}
out=$HOME/onecontact/differential/$label
mkdir -p "$out"
export DOTNET_PROCESSOR_COUNT=4
for p in bench/OneContact.Bench tools/Eval/Extract; do
  nice dotnet build "$repo/$p" -c Release -m:4 -v quiet -clp:NoSummary 2>&1 | grep -E 'error|warn' | sort -u | head || true
done
bench=$(find "$repo/artifacts/bin" -path '*release*' -name 'RecursiveAI.OneContact.Bench.dll' | head -1)
extract=$(find "$repo/artifacts/bin/OneContact.Eval.Extract/release" -name 'RecursiveAI.OneContact.Eval.dll' | head -1)
rendered=${ONECONTACT_RENDERED:-$HOME/onecontact/data/rendered}
dirs=()
for d in "$rendered"/*/; do dirs+=("$d"); done
dotnet "$bench" dump-address 300 "$out/address.txt" "${dirs[@]}" > /dev/null
frozen=${ONECONTACT_EVAL:-$HOME/onecontact/eval/private}/extraction
ugc=${ONECONTACT_UGC_IN:?set ONECONTACT_UGC_IN to the CRM/UGC evaluation inputs (jsonl)}
python3 -c "
import json, sys
for l in open(sys.argv[1]): print(json.loads(l)['id'])" "$frozen/frozen/gold-web.jsonl" > "$out/web.ids"
for mode in rules model; do
  if [ $mode = model ]; then export ONECONTACT_MODELS_MODEL=${ONECONTACT_MODELS_MODEL:?model}; fi
  if [ $mode = rules ]; then env -u ONECONTACT_MODELS_MODEL dotnet "$extract" ours-web "$frozen/web" "$out/web-$mode.jsonl" --ids "$out/web.ids" > /dev/null
  else dotnet "$extract" ours-web "$frozen/web" "$out/web-$mode.jsonl" --ids "$out/web.ids" > /dev/null; fi
  if [ $mode = rules ]; then env -u ONECONTACT_MODELS_MODEL dotnet "$extract" ours-text "$ugc" "$out/text-$mode.jsonl" > /dev/null
  else dotnet "$extract" ours-text "$ugc" "$out/text-$mode.jsonl" > /dev/null; fi
done
sha256sum "$out"/*.txt "$out"/*.jsonl | sed "s#$out/##" > "$out/SHA256SUMS"
wc -l "$out"/address.txt "$out"/*.jsonl
