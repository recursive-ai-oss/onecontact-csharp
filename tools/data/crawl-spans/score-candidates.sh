#!/usr/bin/env bash
# Scores span-head candidates on the extraction evaluation sets next to the reference systems' stored predictions: each candidate is one
# saved pipeline and a choice of whether the model also reads web pages. Each configuration is run once; nothing is tuned on the sets.
# Writes aggregates only (no item text).
#
#   usage: score-candidates.sh <out dir> <label>=<model.onenlp|none>[:web] ...
#   The first label is the reference of the paired differences. `none` runs the extractor without a model; `:web` lets the model read pages.
#   environment: EVAL_EXTRACT (the built eval-extract dll, RecursiveAI.OneContact.Eval.dll), FROZEN, WEB_SET, BASELINES as in
#   tools/Eval/extraction/run-model-eval.sh; EVAL_SCORE_DIR is the folder of score-extraction.py (default tools/Eval).
set -euo pipefail
out=$1; shift
here=$(cd "$(dirname "$0")/../../Eval" && pwd)
score_dir=${EVAL_SCORE_DIR:-$here}
mkdir -p "$out"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

python3 -c "
import json, sys
for l in open(sys.argv[1]): print(json.loads(l)['id'])" "$FROZEN/gold-web.jsonl" > "$tmp/web.ids"

preds_web=(); preds_text=()
for spec in "$@"; do
  label=${spec%%=*}; rest=${spec#*=}; model=${rest%%:*}
  on=0; [ "$rest" != "$model" ] && on=1
  if [ "$model" = none ]; then unset ONECONTACT_TIER1_MODEL; else export ONECONTACT_TIER1_MODEL=$model; fi
  ONECONTACT_WEB_MODEL=$on dotnet "$EVAL_EXTRACT" ours-web "$WEB_SET" "$tmp/web-$label.jsonl" --ids "$tmp/web.ids" > /dev/null
  dotnet "$EVAL_EXTRACT" ours-text "$BASELINES/ugc/in.jsonl" "$tmp/text-$label.jsonl" > /dev/null
  preds_web+=(--pred "$label=$tmp/web-$label.jsonl")
  preds_text+=(--pred "$label=$tmp/text-$label.jsonl")
done
ref=${1%%=*}

score() { # set gold mentions baselines-dir preds...
  local set=$1 gold=$2 mentions=$3 dir=$BASELINES/$4; shift 4
  local args=(--gold "$gold" --mentions "$mentions" "$@")
  if [ "$mentions" = text ]; then
    args+=(--pred "libphonenumber=$dir/jar.jsonl" --produces libphonenumber=phone
           --pred "recognizers=$dir/mstext.jsonl" --produces recognizers=phone,email,website
           --pred "spacy=$dir/spacy.jsonl" --produces spacy=person,org)
  fi
  python3 "$score_dir/score-extraction.py" "${args[@]}" --reference "$ref" --json "$out/$set-$mentions.json" > "$out/$set-$mentions.txt"
}
score web "$FROZEN/gold-web.jsonl" text web "${preds_web[@]}"
score web "$FROZEN/gold-web.jsonl" all web "${preds_web[@]}"
score text "$FROZEN/gold-ugc.jsonl" text ugc "${preds_text[@]}"
score text "$FROZEN/gold-ugc.jsonl" all ugc "${preds_text[@]}"
