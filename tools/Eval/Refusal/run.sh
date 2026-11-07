#!/usr/bin/env bash
# usage: run.sh <output directory>
# Runs the honest-refusal measurement (see docs/maintainers/refusal-rows.md) over every frozen address set under $ONECONTACT_EVAL
# (default ~/onecontact/eval/private) and writes refusal.tsv (counts only) and diagnosis.tsv (ids and offending words; stays with the sets).
set -euo pipefail
repo=$(cd "$(dirname "$0")/../../.." && pwd)
out=${1:?output directory}
root=${ONECONTACT_EVAL:-$HOME/onecontact/eval/private}
mkdir -p "$out"
export DOTNET_PROCESSOR_COUNT=4
nice dotnet build "$repo/tools/Eval/Refusal" -c Release -m:4 -v quiet -clp:NoSummary || exit 1
dll=$(find "$repo/artifacts/bin/OneContact.Eval.Refusal/release" -name 'RecursiveAI.OneContact.Eval.Refusal.dll' | head -1)
args=()
for f in "$root"/sets/gold-address-*.jsonl "$root"/tier2/u31/ctx/sets/gold-address-*.jsonl "$root"/tier2/u32/sets/gold-address-*.jsonl \
         "$root"/tier2/u33/sets/gold-address-*.jsonl "$root"/tier2/u34/sets/gold-address-*.jsonl \
         "$root"/fresh/addresses/gold-address-*.jsonl "$root"/fresh/ro-mx/gold-address-*.jsonl; do
  [ -f "$f" ] || continue
  case "$f" in
    "$root"/sets/*) set_name=tier1 ;;
    "$root"/fresh/addresses/*) set_name=fresh-addresses ;;
    "$root"/fresh/ro-mx/*) set_name=fresh-ro-mx ;;
    "$root"/tier2/u31/ctx/*) set_name=group-a ;;
    "$root"/tier2/u32/*) set_name=group-b ;;
    "$root"/tier2/u33/*) set_name=c-north ;;
    *) set_name=c-south ;;
  esac
  cc=$(basename "$f" .jsonl); cc=${cc#gold-address-}
  args+=("$set_name/$cc" "$f")
done
dotnet "$dll" "${args[@]}" "$out/diagnosis.tsv" > "$out/refusal.tsv"
wc -l "$out/refusal.tsv" "$out/diagnosis.tsv"
