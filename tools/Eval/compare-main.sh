#!/usr/bin/env bash
# usage: tools/Eval/compare-main.sh <baseline outcomes dir> <new outcomes dir> <country>...
# Scores two prediction sets (written by outcomes.sh for the same countries) against the gold and libpostal/Senzing: each system against libpostal, and against the first set as reference (paired
# bootstrap deltas). Prints the scorer's aggregate rows only.
set -uo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
base=${1:?baseline dir}; new=${2:?new dir}; shift 2
root=${ONECONTACT_EVAL:-$HOME/onecontact/eval/private}
for cc in "$@"; do
  dir=""
  for g in group-a group-b c-north c-south; do [ -f "$root/tier2/$g/sets/gold-address-$cc.jsonl" ] && dir="$root/tier2/$g/sets"; done
  sen=()
  [ -f "$dir/pred-$cc/senzing-address.jsonl" ] && sen=(--pred "senzing=$dir/pred-$cc/senzing-address.jsonl")
  echo "== $cc vs libpostal"
  python3 "$repo/tools/Eval/score.py" address --gold "$dir/gold-address-$cc.jsonl" \
    --pred "main=$base/$cc-model.jsonl" --pred "main-hint=$base/$cc-model-hint.jsonl" --pred "new=$new/$cc-model.jsonl" --pred "new-hint=$new/$cc-model-hint.jsonl" \
    --pred "libpostal=$dir/pred-$cc/libpostal-address.jsonl" "${sen[@]}" --reference libpostal 2>&1 | grep -E 'address (field|full).*coarse' | sed 's/  */ /g' | cut -c1-150
  echo "== $cc new vs main"
  python3 "$repo/tools/Eval/score.py" address --gold "$dir/gold-address-$cc.jsonl" \
    --pred "main=$base/$cc-model.jsonl" --pred "new=$new/$cc-model.jsonl" --reference main 2>&1 | grep -E 'address (field|full).*coarse' | sed 's/  */ /g' | cut -c1-150
  python3 "$repo/tools/Eval/score.py" address --gold "$dir/gold-address-$cc.jsonl" \
    --pred "main-hint=$base/$cc-model-hint.jsonl" --pred "new-hint=$new/$cc-model-hint.jsonl" --reference main-hint 2>&1 | grep -E 'address (field|full).*coarse' | sed 's/  */ /g' | cut -c1-150
done
