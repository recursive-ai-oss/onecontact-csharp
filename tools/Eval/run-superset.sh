#!/usr/bin/env bash
# Scores the current rules with the Tier 1 + Tier 2 model on one country's evaluation sets, next to the stored reference predictions, without touching them.
# usage: [SUPERSET_TAG=<name>] tools/Eval/run-superset.sh <set directory> <country>   (the tag keeps an earlier run: output goes to pred-<country>-<tag>)
# Needs: the baselines already written in <set directory>/pred-<country> (libpostal, senzing, names/), ONECONTACT_MODELS_MODEL (the saved pipeline),
# MODELS_DLL (tools/Eval/Models build) and RULES_DLL (tools/Eval/Rules build). Writes <set directory>/pred-<country>-superset/ and prints aggregates only:
# the model rows are the rules first and the model where the rules refuse, with the country as hint ("-hint") and without one.
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
dir=${1:?set directory}
cc=${2:?country}
: "${ONECONTACT_MODELS_MODEL:?saved pipeline}" "${MODELS_DLL:?}" "${RULES_DLL:?}"
dn=${DOTNET:-dotnet}
base=$dir/pred-$cc
out=$dir/pred-$cc-${SUPERSET_TAG:-superset}
mkdir -p "$out"
ga=$dir/gold-address-$cc.jsonl
gn=$dir/gold-name-$cc.jsonl
"$dn" "$MODELS_DLL" address "$ga" "$out/model-address.jsonl" > /dev/null
"$dn" "$MODELS_DLL" address "$ga" "$out/model-hint-address.jsonl" hint > /dev/null
"$dn" "$MODELS_DLL" name "$gn" "$out/model-name.jsonl" > /dev/null
"$dn" "$RULES_DLL" address "$ga" "$out/rules-address.jsonl" > /dev/null
"$dn" "$RULES_DLL" address "$ga" "$out/rules-hint-address.jsonl" hint > /dev/null
"$dn" "$RULES_DLL" name "$gn" "$out/rules-name.jsonl" > /dev/null
echo "== addresses $cc"
python3 "$repo/tools/Eval/score.py" address --gold "$ga" --pred model="$out/model-address.jsonl" --pred model-hint="$out/model-hint-address.jsonl" \
  --pred rules="$out/rules-address.jsonl" --pred rules-hint="$out/rules-hint-address.jsonl" \
  --pred libpostal="$base/libpostal-address.jsonl" --pred senzing="$base/senzing-address.jsonl" --reference libpostal --json "$out/scores-address.json"
echo "== names $cc"
python3 "$repo/tools/Eval/score.py" name --gold "$gn" --pred model="$out/model-name.jsonl" --pred rules="$out/rules-name.jsonl" \
  --pred probablepeople="$base/names/probablepeople.jsonl" --pred nameparser="$base/names/nameparser.jsonl" --reference probablepeople --json "$out/scores-name.json"
if [ -f "$base/names/cleanco.jsonl" ]; then
  echo "== legal forms $cc"
  python3 "$repo/tools/Eval/tier2-south/legalform.py" --gold "$gn" --pred model="$out/model-name.jsonl" --pred rules="$out/rules-name.jsonl" \
    --pred probablepeople="$base/names/probablepeople.jsonl" --pred cleanco="$base/names/cleanco.jsonl" --reference cleanco
fi
