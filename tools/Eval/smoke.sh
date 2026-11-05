#!/usr/bin/env bash
# Runs the whole evaluation pipeline over the synthetic sets (no real data) to check the tools end to end.
# usage: tools/Eval/smoke.sh <work directory>
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
work=${1:?work directory}
mkdir -p "$work"
oracles=${ONECONTACT_ORACLES:-$HOME/onecontact/oracles}
python3 "$repo/tools/Eval/synthetic-sets.py" "$repo" "$work"
dotnet run -c Release --project "$repo/tools/Eval/Rules" -- address "$work/synthetic-address.jsonl" "$work/rules-address.jsonl"
dotnet run -c Release --project "$repo/tools/Eval/Rules" -- name "$work/synthetic-name.jsonl" "$work/rules-name.jsonl"
lp=$oracles/libpostal
PATH=$lp/mamba/envs/lp/bin:$PATH python "$repo/tools/Oracles/LibPostal/run-libpostal.py" "$work/synthetic-address.jsonl" "$work/libpostal-address.jsonl" "$lp/data-base"
PATH=$lp/mamba/envs/lp/bin:$PATH python "$repo/tools/Oracles/LibPostal/run-libpostal.py" "$work/synthetic-address.jsonl" "$work/senzing-address.jsonl" "$lp/data-senzing"
"$oracles/names-venv/bin/python" "$repo/tools/Oracles/Names/run-eval-names.py" "$work/synthetic-name.jsonl" "$work/names"
python3 "$repo/tools/Eval/score.py" address --gold "$work/synthetic-address.jsonl" --pred rules="$work/rules-address.jsonl" \
  --pred libpostal="$work/libpostal-address.jsonl" --pred senzing="$work/senzing-address.jsonl" --reference libpostal --resamples 200
python3 "$repo/tools/Eval/score.py" name --gold "$work/synthetic-name.jsonl" --pred rules="$work/rules-name.jsonl" \
  --pred probablepeople="$work/names/probablepeople.jsonl" --pred nameparser="$work/names/nameparser.jsonl" --reference probablepeople --resamples 200
