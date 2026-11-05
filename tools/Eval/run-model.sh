#!/usr/bin/env bash
# Scores a trained Tier 1 model on the evaluation sets next to the stored reference predictions, without touching them.
# usage: tools/Eval/run-model.sh <set directory> <country> <label> <model.onenlp>
# Needs the baselines already written by run-all.sh in <set directory>/pred-<country>. Writes <set directory>/pred-<country>-<label>/ and prints aggregates only.
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
dir=${1:?set directory}
cc=${2:?country}
label=${3:?label}
export ONECONTACT_TIER1_MODEL=${4:?model file}
base=$dir/pred-$cc
out=$dir/pred-$cc-$label
mkdir -p "$out"
tool="dotnet run -c Release --project $repo/tools/Eval/Models --"
$tool address "$dir/gold-address-$cc.jsonl" "$out/model-address.jsonl"
$tool name "$dir/gold-name-$cc.jsonl" "$out/model-name.jsonl"
echo "== addresses $cc ($label)"
python3 "$repo/tools/Eval/score.py" address --gold "$dir/gold-address-$cc.jsonl" --pred model="$out/model-address.jsonl" \
  --pred rules="$base/rules-address.jsonl" --pred libpostal="$base/libpostal-address.jsonl" --pred senzing="$base/senzing-address.jsonl" --reference libpostal --json "$out/scores-address.json"
echo "== names $cc ($label)"
python3 "$repo/tools/Eval/score.py" name --gold "$dir/gold-name-$cc.jsonl" --pred model="$out/model-name.jsonl" \
  --pred rules="$base/rules-name.jsonl" --pred probablepeople="$base/names/probablepeople.jsonl" --pred nameparser="$base/names/nameparser.jsonl" --reference probablepeople --json "$out/scores-name.json"
