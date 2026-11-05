#!/usr/bin/env bash
# Runs every system over the evaluation sets in one directory and prints the scorer's aggregate rows (no item text).
# usage: tools/Eval/run-all.sh <set directory> <country>     the directory holds gold-address-<country>.jsonl and gold-name-<country>.jsonl
# Predictions are written next to the sets; they are as private as the sets.
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
dir=${1:?set directory}
cc=${2:?country}
oracles=${ONECONTACT_ORACLES:-$HOME/onecontact/oracles}
lp=$oracles/libpostal
mkdir -p "$dir/pred-$cc"
p=$dir/pred-$cc
dotnet run -c Release --project "$repo/tools/Eval/Rules" -- address "$dir/gold-address-$cc.jsonl" "$p/rules-address.jsonl"
dotnet run -c Release --project "$repo/tools/Eval/Rules" -- name "$dir/gold-name-$cc.jsonl" "$p/rules-name.jsonl"
PATH=$lp/mamba/envs/lp/bin:$PATH python "$repo/tools/Oracles/LibPostal/run-libpostal.py" "$dir/gold-address-$cc.jsonl" "$p/libpostal-address.jsonl" "$lp/data-base"
PATH=$lp/mamba/envs/lp/bin:$PATH python "$repo/tools/Oracles/LibPostal/run-libpostal.py" "$dir/gold-address-$cc.jsonl" "$p/senzing-address.jsonl" "$lp/data-senzing"
"$oracles/names-venv/bin/python" -W ignore "$repo/tools/Oracles/Names/run-eval-names.py" "$dir/gold-name-$cc.jsonl" "$p/names"
echo "== addresses $cc"
python3 "$repo/tools/Eval/score.py" address --gold "$dir/gold-address-$cc.jsonl" --pred rules="$p/rules-address.jsonl" \
  --pred libpostal="$p/libpostal-address.jsonl" --pred senzing="$p/senzing-address.jsonl" --reference libpostal --json "$p/scores-address.json"
echo "== names $cc"
python3 "$repo/tools/Eval/score.py" name --gold "$dir/gold-name-$cc.jsonl" --pred rules="$p/rules-name.jsonl" \
  --pred probablepeople="$p/names/probablepeople.jsonl" --pred nameparser="$p/names/nameparser.jsonl" --reference probablepeople --json "$p/scores-name.json"
