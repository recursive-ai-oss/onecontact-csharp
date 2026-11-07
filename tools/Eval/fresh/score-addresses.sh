#!/usr/bin/env bash
# One baseline scoring of fresh address sets. For each country, score-fresh.sh runs the rules and the rules with the shipped model (each without
# and with the country as a hint) and both libpostal parsers three ways: without a country, with the documented `country` keyword, and with the
# country name appended to the text. Two scorings follow: against the no-country libpostal (the reference for the no-hint rows) and against the
# country-name libpostal (the reference for the hinted rows). Prints the scorer's aggregate rows only.
# usage: score-addresses.sh <set directory> <log directory> <CC> [<CC> ...]
# Needs what score-fresh.sh needs (ONECONTACT_MODELS_MODEL, RULES_DLL, MODELS_DLL, the oracle installs).
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
repo=$(cd "$here/../../.." && pwd)
dir=${1:?set directory}; logs=${2:?log directory}; shift 2
mkdir -p "$logs"
for c in "$@"; do
  WITH_COUNTRY=1 REFERENCE=libpostal bash "$here/score-fresh.sh" "$dir" address "$c" > "$logs/score-$c-vs-libpostal.txt"
  p=$dir/pred-$c
  python3 "$repo/tools/Eval/score.py" address --gold "$dir/gold-address-$c.jsonl" \
    --pred rules="$p/rules-address.jsonl" --pred rules-hint="$p/rules-hint-address.jsonl" \
    --pred model="$p/model-address.jsonl" --pred model-hint="$p/model-hint-address.jsonl" \
    --pred libpostal="$p/libpostal-address.jsonl" --pred senzing="$p/senzing-address.jsonl" \
    --pred libpostal-country="$p/libpostal-country-address.jsonl" --pred senzing-country="$p/senzing-country-address.jsonl" \
    --pred libpostal-name="$p/libpostal-name-address.jsonl" --pred senzing-name="$p/senzing-name-address.jsonl" \
    --reference libpostal-name --json "$p/scores-address-vs-name.json" > "$logs/score-$c-vs-libpostal-name.txt"
  echo "scored $c"
done
