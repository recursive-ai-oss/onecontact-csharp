#!/usr/bin/env bash
# Scores address challengers against the default model on the evaluation sets of one set directory, rules first and the model where the rules leave a
# decision open, with the country as hint ("hint") and without one ("nohint"). Prints aggregates only; predictions are written next to the gold files.
# usage: tools/Eval/run-challenger.sh <set directory> <out directory>
# Needs: MODELS_DLL (tools/Eval/Models build) and DEFAULT_MODEL, C1_MODEL, C2_MODEL (saved pipelines). Writes <out>/<cc>/<system>-<mode>.jsonl and
# <out>/<cc>/scores-<mode>.json, and a table with one row per country, mode and metric to <out>/summary.tsv. The first system, default, is the reference of
# every paired difference. A challenger is ODbL-trained: results are information for choosing a default and no model file leaves the machine.
set -euo pipefail
repo=$(cd "$(dirname "$0")/../.." && pwd)
dir=${1:?set directory}
out=${2:?output directory}
SYSTEMS=${SYSTEMS:-c1 c2}   # systems scored next to the default: c1 (C1_MODEL), c2 (C2_MODEL), cand (CAND_MODEL)
: "${MODELS_DLL:?}" "${DEFAULT_MODEL:?}"
dn=${DOTNET:-dotnet}
mkdir -p "$out"
for gold in "$dir"/gold-address-*.jsonl; do
  cc=$(basename "$gold" .jsonl); cc=${cc#gold-address-}
  mkdir -p "$out/$cc"
  for mode in hint nohint; do
    args=(); [ "$mode" = hint ] && args=(hint)
    preds=()
    for sys in default $SYSTEMS; do
      case $sys in default) m=$DEFAULT_MODEL;; c1) m=$C1_MODEL;; c2) m=$C2_MODEL;; cand) m=$CAND_MODEL;; esac
      ONECONTACT_MODELS_MODEL=$m "$dn" "$MODELS_DLL" address "$gold" "$out/$cc/$sys-$mode.jsonl" "${args[@]}" > /dev/null
      preds+=(--pred "$sys=$out/$cc/$sys-$mode.jsonl")
    done
    python3 "$repo/tools/Eval/score.py" address --gold "$gold" "${preds[@]}" --reference default --json "$out/$cc/scores-$mode.json" > "$out/$cc/scores-$mode.txt"
  done
done
python3 "$repo/tools/Eval/challenger-summary.py" "$out" "${SYSTEMS// /,}" > "$out/summary.tsv"
