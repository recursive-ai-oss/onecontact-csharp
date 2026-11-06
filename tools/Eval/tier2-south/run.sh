#!/usr/bin/env bash
# Runs every system over one Tier 2 country's evaluation sets and prints the scorer's aggregate rows (counts, scores and intervals only).
# usage: tools/Eval/tier2-south/run.sh <set directory> <country>
# The directory holds gold-address-<country>.jsonl and gold-name-<country>.jsonl. Predictions are written to <dir>/pred-<country>/ and
# are as private as the sets. Rules are scored twice: with no hint (the parser infers the country) and with the country as hint.
# Build the rules tool first (dotnet build -c Release tools/Eval/Rules); the oracles are the installs from tools/Oracles.
set -euo pipefail
repo=$(cd "$(dirname "$0")/../../.." && pwd)
dir=${1:?set directory}
cc=${2:?country}
oracles=${ONECONTACT_ORACLES:-$HOME/onecontact/oracles}
lp=$oracles/libpostal
rules=${ONECONTACT_RULES_DLL:-$(ls "$repo"/artifacts/bin/OneContact.Eval.Rules/release/*Rules.dll | head -1)}
dn=${DOTNET:-dotnet}
mkdir -p "$dir/pred-$cc"
p=$dir/pred-$cc
"$dn" "$rules" address "$dir/gold-address-$cc.jsonl" "$p/rules-address.jsonl"
"$dn" "$rules" address "$dir/gold-address-$cc.jsonl" "$p/rules-hint-address.jsonl" hint
"$dn" "$rules" name "$dir/gold-name-$cc.jsonl" "$p/rules-name.jsonl"
PATH=$lp/mamba/envs/lp/bin:$PATH python "$repo/tools/Oracles/LibPostal/run-libpostal.py" "$dir/gold-address-$cc.jsonl" "$p/libpostal-address.jsonl" "$lp/data-base"
PATH=$lp/mamba/envs/lp/bin:$PATH python "$repo/tools/Oracles/LibPostal/run-libpostal.py" "$dir/gold-address-$cc.jsonl" "$p/senzing-address.jsonl" "$lp/data-senzing"
"$oracles/names-venv/bin/python" -W ignore "$repo/tools/Oracles/Names/run-eval-names.py" "$dir/gold-name-$cc.jsonl" "$p/names"
echo "== addresses $cc"
python3 "$repo/tools/Eval/score.py" address --gold "$dir/gold-address-$cc.jsonl" --pred rules="$p/rules-address.jsonl" --pred rules-hint="$p/rules-hint-address.jsonl" \
  --pred libpostal="$p/libpostal-address.jsonl" --pred senzing="$p/senzing-address.jsonl" --reference libpostal --json "$p/scores-address.json"
echo "== names $cc"
python3 "$repo/tools/Eval/score.py" name --gold "$dir/gold-name-$cc.jsonl" --pred rules="$p/rules-name.jsonl" \
  --pred probablepeople="$p/names/probablepeople.jsonl" --pred nameparser="$p/names/nameparser.jsonl" --reference probablepeople --json "$p/scores-name.json"
echo "== legal forms $cc"
python3 "$repo/tools/Eval/tier2-south/legalform.py" --gold "$dir/gold-name-$cc.jsonl" --pred rules="$p/rules-name.jsonl" \
  --pred probablepeople="$p/names/probablepeople.jsonl" --pred cleanco="$p/names/cleanco.jsonl" --reference cleanco
