#!/usr/bin/env bash
# usage: score-nohint.sh <outcomes directory> <country>...   Scores the predictions outcomes.sh wrote (no hint and hinted) against libpostal's stored ones; prints the scorer's aggregate rows.
set -uo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
out=${1:?outcomes directory}; shift
root=${ONECONTACT_EVAL:-$HOME/onecontact/eval/private}
for cc in "$@"; do
  dir=""
  for g in group-a group-b c-north c-south; do [ -f "$root/tier2/$g/sets/gold-address-$cc.jsonl" ] && dir="$root/tier2/$g/sets"; done
  python3 "$repo/tools/Eval/score.py" address --gold "$dir/gold-address-$cc.jsonl" --pred model="$out/$cc-model.jsonl" --pred model-hint="$out/$cc-model-hint.jsonl" \
    --pred libpostal="$dir/pred-$cc/libpostal-address.jsonl" --reference libpostal 2>&1 | grep -E 'address (field|full)' | sed "s/^address /$cc /" | cut -c1-150
done
