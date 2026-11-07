#!/usr/bin/env bash
# Scores our extractor with and without the Tier 1 contact-span model on the extraction evaluation sets, next to the reference systems'
# stored predictions. One run per confidence cut: nothing is tuned on the sets.
#
#   usage: run-model-eval.sh <label> <cut> <out dir>
#   environment:
#     EVAL_EXTRACT            the built eval-extract tool
#     ONECONTACT_TIER1_MODEL  the saved Tier 1 pipeline (with the contact-span head), or ONECONTACT_MODELS_MODEL the Tier 1 + Tier 2 one
#     FROZEN                  folder with gold-web.jsonl and gold-ugc.jsonl
#     WEB_SET                 the web set folder (pages/, pages.jsonl)
#     BASELINES               folder with web/ and ugc/, each holding in.jsonl and the reference systems' prediction files (run-baselines.sh)
#
# Writes <out>/<label>-<set>-<mentions>.txt and .json (aggregates only; no item text) and prints nothing else.
set -euo pipefail
label=$1; cut=$2; out=$3
here=$(cd "$(dirname "$0")/../.." && pwd)
mkdir -p "$out"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

ids() { python3 -c "
import json, sys
for l in open(sys.argv[1]): print(json.loads(l)['id'])" "$1"; }

ids "$FROZEN/gold-web.jsonl" > "$tmp/web.ids"
env -u ONECONTACT_TIER1_MODEL -u ONECONTACT_MODELS_MODEL "$EVAL_EXTRACT" ours-web "$WEB_SET" "$tmp/web-rules.jsonl" --ids "$tmp/web.ids" > /dev/null
ONECONTACT_SPAN_CUT=$cut "$EVAL_EXTRACT" ours-web "$WEB_SET" "$tmp/web-model.jsonl" --ids "$tmp/web.ids" > /dev/null
env -u ONECONTACT_TIER1_MODEL -u ONECONTACT_MODELS_MODEL "$EVAL_EXTRACT" ours-text "$BASELINES/ugc/in.jsonl" "$tmp/text-rules.jsonl" > /dev/null
ONECONTACT_SPAN_CUT=$cut "$EVAL_EXTRACT" ours-text "$BASELINES/ugc/in.jsonl" "$tmp/text-model.jsonl" > /dev/null

score() { # set gold mentions
  local set=$1 gold=$2 mentions=$3 dir=$BASELINES/$4
  local args=(--gold "$gold" --mentions "$mentions" --pred "rules=$tmp/$set-rules.jsonl" --pred "model=$tmp/$set-model.jsonl")
  if [ "$mentions" = text ]; then
    args+=(--pred "libphonenumber=$dir/jar.jsonl" --produces libphonenumber=phone
           --pred "recognizers=$dir/mstext.jsonl" --produces recognizers=phone,email,website
           --pred "spacy=$dir/spacy.jsonl" --produces spacy=person,org)
  fi
  python3 "$here/Eval/score-extraction.py" "${args[@]}" --reference rules --json "$out/$label-$set-$mentions.json" > "$out/$label-$set-$mentions.txt"
}
score web "$FROZEN/gold-web.jsonl" text web
score web "$FROZEN/gold-web.jsonl" all web
score text "$FROZEN/gold-ugc.jsonl" text ugc
score text "$FROZEN/gold-ugc.jsonl" all ugc
