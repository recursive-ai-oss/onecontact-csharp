#!/usr/bin/env bash
# Scores our extractor with two Tier 1 + Tier 2 models (a shipped one and a candidate) on the extraction evaluation sets, next to the reference systems' stored
# predictions, with the contact-span cut given. Each model is run once; nothing is tuned on the sets. Aggregates only.
#
#   usage: run-pair.sh <cut> <out dir>
#   environment: DEFAULT_MODEL, CAND_MODEL (saved pipelines); optional CAND_EVAL_EXTRACT (the accurate-tier build of the tool, for an accurate candidate); EVAL_EXTRACT, FROZEN, WEB_SET, BASELINES as in run-model-eval.sh
# Writes <out>/<set>-<mentions>.{txt,json}; the first system, default, is the reference of every paired difference.
set -euo pipefail
cut=$1; out=$2
here=$(cd "$(dirname "$0")/.." && pwd)
mkdir -p "$out"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT
python3 -c "
import json, sys
for l in open(sys.argv[1]): print(json.loads(l)['id'])" "$FROZEN/gold-web.jsonl" > "$tmp/web.ids"
for sys in default cand; do
  case $sys in default) m=$DEFAULT_MODEL;; cand) m=$CAND_MODEL;; esac
  tool=$EVAL_EXTRACT; var=ONECONTACT_MODELS_MODEL
  # CAND_EVAL_EXTRACT: the candidate is an accurate-tier pipeline, run by its own build of the tool (it needs a newer OneNLP).
  if [ "$sys" = cand ] && [ -n "${CAND_EVAL_EXTRACT:-}" ]; then tool=$CAND_EVAL_EXTRACT; var=ONECONTACT_ACCURATE_MODEL; fi
  env "$var=$m" ONECONTACT_SPAN_CUT=$cut "$tool" ours-web "$WEB_SET" "$tmp/web-$sys.jsonl" --ids "$tmp/web.ids" > /dev/null
  env "$var=$m" ONECONTACT_SPAN_CUT=$cut "$tool" ours-text "$BASELINES/ugc/in.jsonl" "$tmp/text-$sys.jsonl" > /dev/null
done
score() { # set gold mentions baselines-dir
  local set=$1 gold=$2 mentions=$3 dir=$BASELINES/$4
  local args=(--gold "$gold" --mentions "$mentions" --pred "default=$tmp/$set-default.jsonl" --pred "cand=$tmp/$set-cand.jsonl")
  if [ "$mentions" = text ]; then
    args+=(--pred "libphonenumber=$dir/jar.jsonl" --produces libphonenumber=phone
           --pred "recognizers=$dir/mstext.jsonl" --produces recognizers=phone,email,website
           --pred "spacy=$dir/spacy.jsonl" --produces spacy=person,org)
  fi
  python3 "$here/score-extraction.py" "${args[@]}" --reference default --json "$out/$set-$mentions.json" > "$out/$set-$mentions.txt"
}
score web "$FROZEN/gold-web.jsonl" text web
score web "$FROZEN/gold-web.jsonl" all web
score text "$FROZEN/gold-ugc.jsonl" text ugc
score text "$FROZEN/gold-ugc.jsonl" all ugc
