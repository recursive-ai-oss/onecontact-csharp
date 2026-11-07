#!/usr/bin/env bash
# usage: outcomes.sh <tag> <output directory> <country>...
# Runs the rules and the model tool (without a hint and with one) on the named countries' address sets and prints, with aggregates only, the outcome
# counts (refused, read with no country, right country, wrong country) and the per-label precision/recall next to libpostal's.
# Needs ONECONTACT_MODELS_MODEL, and the builds of tools/Eval/Rules and tools/Eval/Models (score-real.sh builds them).
set -uo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
tag=${1:?tag}; out=${2:?output directory}; shift 2
root=${ONECONTACT_EVAL:-$HOME/onecontact/eval/private}
export DOTNET_PROCESSOR_COUNT=4
for p in Rules Models; do
  nice dotnet build "$repo/tools/Eval/$p" -c Release -m:4 -v quiet -clp:NoSummary 2>&1 | grep -E 'error|warn' | sort -u | head
done
rules=$(realpath "$(find "$repo/artifacts/bin/OneContact.Eval.Rules/release" -name 'RecursiveAI.OneContact.Eval.Rules.dll' | head -1)")
models=$(realpath "$(find "$repo/artifacts/bin/OneContact.Eval.Models/release" -name 'RecursiveAI.OneContact.Eval.Models.dll' | head -1)")
mkdir -p "$out"
for cc in "$@"; do
  dir=""
  for g in group-a group-b c-north c-south; do [ -f "$root/tier2/$g/sets/gold-address-$cc.jsonl" ] && dir="$root/tier2/$g/sets"; done
  [ -z "$dir" ] && { echo "no set for $cc"; continue; }
  gold=$dir/gold-address-$cc.jsonl
  dotnet "$models" address "$gold" "$out/$cc-model.jsonl" > /dev/null
  dotnet "$models" address "$gold" "$out/$cc-model-hint.jsonl" hint > /dev/null
  dotnet "$rules" diag "$gold" "$out/$cc-diag.jsonl" > /dev/null
  echo "=== $cc ($tag)"
  python3 "$repo/tools/Eval/outcomes.py" --gold "$gold" --pred model="$out/$cc-model.jsonl" --pred model-hint="$out/$cc-model-hint.jsonl" --pred libpostal="$dir/pred-$cc/libpostal-address.jsonl"
  python3 "$repo/tools/Eval/diag-outcomes.py" --diag "$out/$cc-diag.jsonl" --pred "$out/$cc-model.jsonl"
  python3 "$repo/tools/Eval/per-label.py" --gold "$gold" --pred model="$out/$cc-model.jsonl" --pred libpostal="$dir/pred-$cc/libpostal-address.jsonl"
done
